using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    public class CommandeService : ICommandeService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<CommandeService> _logger;

        // Événement déclenché après toute modification d'une commande.
        // Permet aux vues ouvertes (PaiementsView, etc.) de se rafraîchir automatiquement.
        public event EventHandler<CommandeChangedEventArgs>? CommandeChanged;

        // ── Détection anti double-soumission ─────────────────────────────
        // Clé : (idOperateur, idClient, typeVetement, montant) — valeur : horodatage UTC
        // de la dernière création. Thread-safe via ConcurrentDictionary.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
            _dernieresCreations = new();

        /// <summary>Durée pendant laquelle une commande identique est considérée
        /// comme un double-clic accidentel.</summary>
        private static readonly TimeSpan FenetreDoublon = TimeSpan.FromSeconds(60);

        // Optionnel : null dans les tests unitaires qui ne testent pas l'audit.
        private readonly IAuditService? _auditService;

        public CommandeService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CommandeService> logger,
            IAuditService? auditService = null)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _auditService = auditService;
        }

        public List<Commande> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
                .Where(c => !c.EstSupprimee)
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        // Récupère les commandes avec pagination
        public async Task<PagedResult<Commande>> ObtenirPageAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Where(c => !c.EstSupprimee)
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .OrderByDescending(c => c.DateDebut);
            
            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Commande>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        
        // Version légère pour affichage tableau (sans toutes les données incluses)
        public async Task<PagedResult<Commande>> ObtenirPageLightAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Where(c => !c.EstSupprimee)
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                    .ThenInclude(p => p.Couturier)
                .OrderByDescending(c => c.DateDebut);
            
            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Commande>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public Commande? ObtenirParId(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == id);
        }

        public void Ajouter(Commande commande, PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, List<MaterielSupplement>? materiaux = null)
        {
            // ── Validation de l'opérateur ────────────────────────────────────
            if (idOperateur <= 0)
                throw new InvalidOperationException(
                    "Impossible de créer une commande : aucun utilisateur connecté identifiable. " +
                    "Veuillez vous déconnecter et vous reconnecter.");

            if (string.IsNullOrWhiteSpace(nomOperateur))
                throw new InvalidOperationException(
                    "Le nom de l'opérateur est obligatoire pour la traçabilité de la commande.");

            // ── Détection anti double-soumission (fenêtre 60 s) ─────────────
            // Clé unique : opérateur + client + type de vêtement + montant.
            // Couvre le double-clic accidentel et la soumission répétée.
            // N'empêche PAS un client de recommander légitimement le même
            // vêtement au même prix après la fenêtre.
            string cleDoublon = $"{idOperateur}|{commande.IdClient}|{piece.TypeVetement}|{piece.MontantCouture:F2}";
            DateTime maintenant = DateTime.UtcNow;

            if (_dernieresCreations.TryGetValue(cleDoublon, out DateTime derniereCreation))
            {
                TimeSpan ecart = maintenant - derniereCreation;
                if (ecart < FenetreDoublon)
                {
                    int resteSecondes = (int)(FenetreDoublon - ecart).TotalSeconds;
                    _logger.LogWarning(
                        "Double-soumission détectée par {Operateur} (IdClient={IdClient}, " +
                        "Type={Type}, Montant={Montant}) — écart {Ecart:F1}s < fenêtre {Fenetre}s.",
                        nomOperateur, commande.IdClient, piece.TypeVetement, piece.MontantCouture,
                        ecart.TotalSeconds, FenetreDoublon.TotalSeconds);

                    throw new DoublonCommandeException(
                        $"Une commande identique (client #{commande.IdClient}, " +
                        $"{piece.TypeVetement}, {piece.MontantCouture:N0} FCFA) " +
                        $"vient d'être créée il y a {(int)ecart.TotalSeconds} seconde(s) " +
                        $"par {nomOperateur}.\n\n" +
                        $"S'il s'agit d'une vraie nouvelle commande, " +
                        $"attendez {resteSecondes} seconde(s) et recommencez.",
                        cleDoublon,
                        ecart);
                }
            }

            // ── Persistance ──────────────────────────────────────────────────
            using var context = _contextFactory.CreateDbContext();

            DateTime now = DateTime.Now;
            commande.DateDebut = now;

            // Traçabilité de création — automatique, invisible pour l'opérateur
            commande.IdOperateurCreation  = idOperateur;
            commande.NomOperateurCreation = nomOperateur.Trim();
            commande.DateCreation         = DateTime.UtcNow;

            // Le statut est TOUJOURS forcé à "A faire" à la création —
            // peu importe ce que l'écran ou l'appelant a pu mettre.
            piece.Statut = "A faire";

            // Commande + pièce + mesures + matériaux en UN SEUL SaveChanges :
            // EF Core l'exécute dans une transaction, donc une coupure de courant
            // ne peut plus laisser une commande à moitié enregistrée.
            commande.Pieces.Add(piece);
            RattacherMesuresEtMateriaux(commande, piece, mesures, materiaux, idOperateur, nomOperateur);

            context.Commandes.Add(commande);
            context.SaveChanges();

            // ── Enregistrement dans la fenêtre anti-doublon ──────────────────
            _dernieresCreations[cleDoublon] = maintenant;

            // Nettoyage périodique des entrées expirées (évite une croissance
            // illimitée du dictionnaire sur des sessions longues).
            NettoyerDernieresCreations();

            _logger.LogInformation(
                "Commande #{IdCommande} créée — Client #{IdClient}, {Type}, " +
                "{Montant:N0} FCFA — par {Operateur} (Id={IdOperateur}) le {Date:dd/MM/yyyy HH:mm:ss}",
                commande.IdCommande, commande.IdClient, piece.TypeVetement,
                piece.MontantCouture, nomOperateur, idOperateur, now);
        }

        /// <summary>
        /// Rattache mesures et matériaux à la pièce via les propriétés de navigation,
        /// pour qu'ils soient enregistrés dans le même SaveChanges que la pièce.
        /// </summary>
        private static void RattacherMesuresEtMateriaux(Commande commande, PieceCommande piece,
            List<Mesure> mesures, List<MaterielSupplement>? materiaux,
            int idOperateur, string nomOperateur)
        {
            foreach (var mesure in mesures)
            {
                mesure.IdMesure = 0;
                mesure.Commande = commande;
                mesure.PieceCommande = piece;
                piece.Mesures.Add(mesure);
            }

            if (materiaux == null) return;
            foreach (var mat in materiaux)
            {
                if (idOperateur <= 0)
                    throw new InvalidOperationException(
                        "Impossible d'ajouter un matériau sans opérateur identifié.");

                // Les matériaux viennent du buffer de l'écran : après un échec puis
                // un nouvel essai, ils pointeraient encore vers la pièce/commande de
                // la tentative ratée — on les rattache explicitement à la nouvelle.
                mat.IdMateriel     = 0; // l'ID est généré par la base
                mat.Commande       = commande;
                mat.PieceCommande  = piece;
                mat.IdOperateur  = idOperateur;
                mat.NomOperateur = (nomOperateur ?? string.Empty).Trim();
                piece.MaterielSupplements.Add(mat);
            }
        }

        /// <summary>
        /// Charge l'opérateur et vérifie qu'il a l'un des rôles autorisés.
        /// </summary>
        private static Employe ExigerRole(ApplicationDbContext context, int idOperateur,
            params RoleEmploye[] roles)
        {
            var operateur = context.Employes.Find(idOperateur);
            Helpers.AuthorizationHelper.RequireRoleEnum(operateur, roles);
            return operateur!;
        }

        /// <summary>
        /// Écrit dans le journal d'audit immuable (si le service est disponible).
        /// Task.Run évite tout blocage du thread UI WPF pendant l'attente.
        /// </summary>
        private void Auditer(Employe operateur, string nomOperateur, string typeAction,
            string entite, int idEntite, object? avant, object? apres, string? motif)
        {
            if (_auditService == null)
            {
                _logger.LogWarning("Audit non disponible — {Action} sur {Entite} #{Id} par {Operateur}",
                    typeAction, entite, idEntite, nomOperateur);
                return;
            }

            Task.Run(() => _auditService.EnregistrerActionAsync(
                idOperateur: operateur.IdEmploye,
                nomOperateur: nomOperateur,
                roleOperateur: operateur.Role,
                typeAction: typeAction,
                entite: entite,
                idEntite: idEntite,
                valeursAvant: avant,
                valeursApres: apres,
                motif: motif)).GetAwaiter().GetResult();
        }

        /// <summary>
        /// TÂCHE 5 — Vérifie que la transition de statut respecte le flux strict.
        /// Flux normal  : A faire → En cours → Terminee → Livree
        /// Retour arrière : uniquement Boss + motif obligatoire.
        /// </summary>
        private static void VerifierFluxStatut(
            string ancien, string nouveau, Employe operateur, string? motif)
        {
            // Ordre numérique des statuts
            static int Rang(string s) => s switch
            {
                "A faire"  => 0,
                "En cours" => 1,
                "Terminee" => 2,
                "Livree"   => 3,
                _          => -1
            };

            int r1 = Rang(ancien);
            int r2 = Rang(nouveau);

            // Avance dans le flux → toujours OK (validation non soldée gérée ailleurs)
            if (r2 > r1) return;

            // Retour en arrière : Boss obligatoire + motif
            if (r2 < r1)
            {
                if (operateur.RoleEnum != RoleEmploye.Boss)
                    throw new InvalidOperationException(
                        $"Seul le Boss peut rétrograder le statut d'une pièce " +
                        $"(\"{ancien}\" → \"{nouveau}\"). " +
                        "Contactez le Boss pour effectuer cette opération.");

                if (string.IsNullOrWhiteSpace(motif))
                    throw new InvalidOperationException(
                        $"Un motif est obligatoire pour rétrograder le statut " +
                        $"(\"{ancien}\" → \"{nouveau}\").");
            }
        }

        /// <summary>
        /// TÂCHE 5 — Met à jour DateTerminee et IdOperateurTerminee d'une pièce
        /// lors de chaque changement de statut, quel que soit le chemin d'appel.
        ///
        /// Règles :
        ///   - Passage vers "Terminee" ou "Livree" : renseigne DateTerminee (UTC)
        ///     et IdOperateurTerminee si DateTerminee est encore null. Une pièce
        ///     déjà commissionnée (IdCommission != null) ne doit jamais revenir en
        ///     arrière (cette garde est faite en amont, dans ChangerStatutPiece /
        ///     ForcerStatutToutesPieces) — ici on ne fait que horodater.
        ///   - Retour en arrière (reprise) : remet DateTerminee à null et
        ///     IdOperateurTerminee à null.
        /// </summary>
        private static void AppliquerDateTerminee(PieceCommande piece,
            string ancienStatut, string nouveauStatut, int idOperateur)
        {
            bool passe_en_terminee = (nouveauStatut == "Terminee" || nouveauStatut == "Livree")
                                   && ancienStatut != "Terminee" && ancienStatut != "Livree";
            bool reprise = (nouveauStatut == "A faire" || nouveauStatut == "En cours")
                         && (ancienStatut == "Terminee" || ancienStatut == "Livree");

            if (passe_en_terminee && piece.DateTerminee == null)
            {
                piece.DateTerminee = DateTime.UtcNow;
                piece.IdOperateurTerminee = idOperateur > 0 ? idOperateur : null;
            }
            else if (reprise)
            {
                piece.DateTerminee = null;
                piece.IdOperateurTerminee = null;
            }
            // Si déjà Terminee → Livree, on conserve la DateTerminee existante
            // (la pièce n'a pas été "refaite").
        }

        /// <summary>
        /// Livraison d'une pièce alors que la commande n'est pas soldée :
        /// interdit, sauf pour le Boss avec un motif.
        /// Retourne le reste à payer si le Boss a forcé (à tracer après SaveChanges
        /// via <see cref="AuditerLivraisonNonSoldee"/>), null si la commande est soldée.
        /// </summary>
        private static decimal? VerifierLivraisonSoldee(Commande commande, Employe operateur, string? motif)
        {
            decimal reste = commande.ResteAPayer;
            if (reste <= 0.01m) return null;

            if (operateur.RoleEnum != RoleEmploye.Boss)
                throw new LivraisonNonSoldeeException(reste, peutForcer: false);

            if (string.IsNullOrWhiteSpace(motif))
                throw new LivraisonNonSoldeeException(reste, peutForcer: true);

            return reste;
        }

        private void AuditerLivraisonNonSoldee(Employe operateur, string nomOperateur,
            int idCommande, decimal reste, string motif, string contexte)
        {
            Auditer(operateur, nomOperateur, "LIVRAISON_NON_SOLDEE", "Commande", idCommande,
                avant: new { ResteAPayer = reste, Contexte = contexte },
                apres: null,
                motif: motif.Trim());
        }

        /// <summary>
        /// Vide le cache anti-doublon. Réservé aux tests unitaires.
        /// En production, les entrées expirent naturellement via NettoyerDernieresCreations().
        /// </summary>
        internal static void ViderCacheDoublonPourTests()
            => _dernieresCreations.Clear();

        /// <summary>
        /// Retire les entrées anti-doublon dont la fenêtre est expirée.
        /// Appelé à chaque création pour éviter la croissance illimitée du dictionnaire.
        /// </summary>
        private void NettoyerDernieresCreations()
        {
            DateTime limite = DateTime.UtcNow - FenetreDoublon;
            foreach (var cle in _dernieresCreations.Keys.ToList())
            {
                if (_dernieresCreations.TryGetValue(cle, out DateTime ts) && ts < limite)
                    _dernieresCreations.TryRemove(cle, out _);
            }
        }

        public void Modifier(Commande commande, PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();

            // ── Contrôle des droits : Boss et Secrétaire uniquement ──────────
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var existante = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .FirstOrDefault(c => c.IdCommande == commande.IdCommande);

            if (existante == null) return;

            // ── Garde champs Boss-only pour la Secrétaire ────────────────────
            // TÂCHE 3 : règles basées sur l'état réel (paiements + statut),
            // et non plus sur des règles fixes qui s'appliquaient toujours.
            if (operateur.RoleEnum == RoleEmploye.Secretaire)
            {
                var pieceRef = existante.Pieces.FirstOrDefault();

                // Règle 3A : après Terminee/Livree → lecture seule pour la Secrétaire
                bool estTermineOuLivree = existante.Pieces.Any(p =>
                    p.Statut == "Terminee" || p.Statut == "Livree");
                if (estTermineOuLivree)
                    throw new InvalidOperationException(
                        "Cette commande est terminée ou livrée. " +
                        "La Secrétaire ne peut plus la modifier. " +
                        "Seul le Boss peut effectuer des modifications à ce stade.");

                bool aPaiement = existante.Paiements.Any(p => !p.EstAnnule);

                if (aPaiement)
                {
                    // Règle 3B : après paiement → montants, type vêtement et client verrouillés
                    if (existante.IdClient != commande.IdClient)
                        throw new InvalidOperationException(
                            "La Secrétaire ne peut pas modifier le client d'une commande " +
                            "après qu'un paiement a été encaissé.");

                    if (pieceRef != null)
                    {
                        if (pieceRef.TypeVetement != piece.TypeVetement)
                            throw new InvalidOperationException(
                                "La Secrétaire ne peut pas modifier le type de vêtement " +
                                "après qu'un paiement a été encaissé. Contactez le Boss.");

                        if (pieceRef.MontantCouture != piece.MontantCouture)
                            throw new InvalidOperationException(
                                "La Secrétaire ne peut pas modifier le prix " +
                                "après qu'un paiement a été encaissé. Contactez le Boss.");
                    }
                }
                // Sans paiement → la Secrétaire peut tout modifier (pas de restriction)
            }

            // ── Mise à jour de la pièce existante ───────────────────────────
            var pieceExistante = existante.Pieces.FirstOrDefault();

            if (pieceExistante != null)
            {
                // Verrouillage commission — ni Boss ni Secrétaire ne peut changer le montant
                if (pieceExistante.IdCommission.HasValue && pieceExistante.MontantCouture != piece.MontantCouture)
                {
                    throw new InvalidOperationException(
                        "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                        "une commission déjà calculée. Annulez d'abord cette commission.");
                }

                decimal dejaEncaisse = existante.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                decimal totalAutresPieces = existante.Pieces
                    .Where(p => p.IdPieceCommande != pieceExistante.IdPieceCommande)
                    .Sum(p => p.MontantCouture);

                if (totalAutresPieces + piece.MontantCouture < dejaEncaisse)
                {
                    throw new InvalidOperationException(
                        $"Le montant total de la commande ({(totalAutresPieces + piece.MontantCouture):N0} FCFA) " +
                        $"ne peut pas être inférieur au montant déjà encaissé ({dejaEncaisse:N0} FCFA).");
                }

                // La Secrétaire peut modifier : couturier, statut, description, mesures
                // Le Boss peut modifier tout cela + type de vêtement + montant
                // TÂCHE 3 : La Secrétaire peut AUSSI modifier type/montant si aucun paiement
                bool secretaireSansPaiement = operateur.RoleEnum == RoleEmploye.Secretaire
                    && !existante.Paiements.Any(p => !p.EstAnnule);
                if (operateur.RoleEnum == RoleEmploye.Boss || secretaireSansPaiement)
                {
                    pieceExistante.TypeVetement = piece.TypeVetement;
                    pieceExistante.MontantCouture = piece.MontantCouture;
                }
                pieceExistante.IdCouturier = piece.IdCouturier;
                pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
                pieceExistante.CheminPhoto = piece.CheminPhoto;
                if (!string.IsNullOrWhiteSpace(piece.Statut))
                {
                    string ancienStatutPiece = pieceExistante.Statut;
                    AppliquerDateTerminee(pieceExistante, ancienStatutPiece, piece.Statut, idOperateur);
                    pieceExistante.Statut = piece.Statut;
                }

                context.Mesures.RemoveRange(pieceExistante.Mesures);
                foreach (var mesure in mesures)
                {
                    mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                    mesure.IdCommande = existante.IdCommande;
                    context.Mesures.Add(mesure);
                }

                // ── Audit pour la Secrétaire ─────────────────────────────────
                if (operateur.RoleEnum == RoleEmploye.Secretaire)
                {
                    Auditer(operateur, nomOperateur, "COMMANDE_MODIFIEE_SECRETAIRE", "Commande",
                        existante.IdCommande,
                        avant: new {
                            DateFin = existante.DateFin,
                            HeureDebut = existante.HeureDebut,
                            IdCouturier = pieceExistante.IdCouturier,
                            Statut = pieceExistante.Statut
                        },
                        apres: new {
                            commande.DateFin,
                            commande.HeureDebut,
                            piece.IdCouturier,
                            piece.Statut
                        },
                        motif: "Modification par Secrétaire");
                }
            }
            else
            {
                piece.IdCommande = existante.IdCommande;
                piece.Statut = "A faire";
                context.PiecesCommande.Add(piece);

                foreach (var mesure in mesures)
                {
                    mesure.IdCommande = existante.IdCommande;
                    context.Mesures.Add(mesure);
                }
            }

            // Mise à jour champs commande
            // TÂCHE 3 : La Secrétaire peut changer le client si aucun paiement
            bool secretairePeutChangerClient = operateur.RoleEnum == RoleEmploye.Secretaire
                && !existante.Paiements.Any(p => !p.EstAnnule);
            if (operateur.RoleEnum == RoleEmploye.Boss || secretairePeutChangerClient)
                existante.IdClient = commande.IdClient;

            existante.DateFin   = commande.DateFin;
            existante.HeureDebut = commande.HeureDebut;
            existante.HeureFin  = commande.HeureFin;

            context.SaveChanges();

            _logger.LogInformation(
                "Modifier — Commande #{IdCommande} par {Operateur} (Id={IdOp}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                existante.IdCommande, nomOperateur, idOperateur, DateTime.Now);
        }

        /// <summary>
        /// Suppression logique d'une commande avec autorisation Boss, traçabilité complète et audit.
        /// Une commande ne doit jamais être supprimée physiquement si elle a un historique
        /// financier (paiements, même annulés). La suppression logique masque la commande
        /// de l'UI normale tout en conservant une trace immuable.
        /// </summary>
        /// <param name="id">ID de la commande</param>
        /// <param name="idOperateur">ID de l'opérateur effectuant la suppression</param>
        /// <param name="nomOperateur">Nom de l'opérateur (traçabilité)</param>
        /// <param name="motif">Motif OBLIGATOIRE de la suppression</param>
        /// <param name="auditService">Service d'audit pour enregistrer l'action</param>
        public async Task SupprimerAsync(int id, int idOperateur, string nomOperateur, string motif, IAuditService? auditService = null)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException(
                    "Le motif de suppression est OBLIGATOIRE pour des raisons de traçabilité.");

            using var context = _contextFactory.CreateDbContext();

            // TÂCHE 3 : La Secrétaire peut supprimer si aucun paiement et statut initial.
            // Le Boss garde tous les droits.
            var operateur = context.Employes.Find(idOperateur);
            if (operateur == null)
                throw new UnauthorizedAccessException("Opérateur introuvable.");

            bool estBoss = operateur.Role == "Boss";
            bool estSecretaire = operateur.Role == "Secretaire";
            if (!estBoss && !estSecretaire)
                throw new UnauthorizedAccessException(
                    "Seuls le Boss et la Secrétaire peuvent supprimer une commande.");

            var commande = context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == id);

            if (commande == null)
                throw new InvalidOperationException("Commande introuvable.");

            if (commande.EstSupprimee)
                throw new InvalidOperationException("Cette commande est déjà marquée comme supprimée.");

            // ── Règles Secrétaire (TÂCHE 3) ────────────────────────────────
            if (estSecretaire)
            {
                // Condition 1 : aucun paiement (même annulé)
                if (commande.Paiements.Any())
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut supprimer une commande que si aucun paiement " +
                        "n'a été enregistré. Des paiements existent sur cette commande " +
                        $"({commande.Paiements.Count} paiement(s)). Contactez le Boss.");

                // Condition 2 : statut initial (toutes les pièces en "A faire")
                bool statutInitial = !commande.Pieces.Any()
                    || commande.Pieces.All(p => p.Statut == "A faire");
                if (!statutInitial)
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut supprimer une commande que si elle est encore " +
                        "au statut initial (toutes les pièces \"À faire\"). " +
                        "Contactez le Boss pour les autres cas.");
            }

            // ── Règles Boss : bloque si paiements (historique comptable) ───
            if (estBoss && commande.Paiements.Any())
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : des paiements y sont rattachés " +
                    $"({commande.Paiements.Count} paiement(s), dont {commande.Paiements.Count(p => p.EstAnnule)} annulé(s)). " +
                    "Pour des raisons de traçabilité comptable, une commande avec historique de paiements " +
                    "doit rester visible dans le journal d'audit, même si tous les paiements sont annulés.");
            }

            if (commande.Pieces.Any(p => p.IdCommission.HasValue))
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : au moins une de ses pièces est " +
                    "rattachée à une commission déjà enregistrée. Annulez d'abord la commission concernée.");
            }

            // Snapshot de l'état avant suppression pour l'audit
            var snapshot = new
            {
                commande.IdCommande,
                Client = commande.Client?.Nom + " " + commande.Client?.Prenom,
                commande.DateDebut,
                commande.DateFin,
                MontantTotal = commande.MontantTotalAvecMateriaux,
                NombrePieces = commande.Pieces.Count,
                Pieces = commande.Pieces.Select(p => new { p.TypeVetement, p.MontantCouture, p.Statut }).ToList(),
                Statut = commande.StatutGlobal
            };

            // Suppression logique : marquer comme supprimée au lieu de supprimer physiquement
            commande.EstSupprimee = true;
            commande.MotifSuppression = motif.Trim();
            commande.DateSuppression = DateTime.Now;
            commande.IdOperateurSuppression = idOperateur;
            commande.NomOperateurSuppression = nomOperateur;

            context.SaveChanges();

            // Enregistrer dans le journal d'audit immuable avec notification
            if (auditService != null)
            {
                await auditService.EnregistrerActionAsync(
                    idOperateur: idOperateur,
                    nomOperateur: nomOperateur,
                    roleOperateur: operateur.Role,
                    typeAction: "COMMANDE_SUPPRIMEE",
                    entite: "Commande",
                    idEntite: id,
                    valeursAvant: snapshot,
                    valeursApres: null,
                    motif: motif,
                    envoyerNotification: estBoss // notification seulement si Boss
                );
            }
        }

        // Ancienne méthode conservée pour compatibilité ascendante — lève une exception
        // pour forcer la migration vers SupprimerAsync qui garantit la traçabilité.
        [Obsolete("Utilisez SupprimerAsync avec idOperateur/motif pour traçabilité complète")]
        public void Supprimer(int id)
        {
            throw new InvalidOperationException(
                "La suppression de commande sans traçabilité est interdite. " +
                "Utilisez SupprimerAsync(id, idOperateur, nomOperateur, motif, auditService) " +
                "pour une suppression logique avec autorisation Boss et audit complet.");
        }

        public List<Commande> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .Where(c => c.Client != null && (
                         c.Client.Nom.Contains(motCle)
                         || c.Client.Prenom.Contains(motCle)
                         || c.Pieces.Any(p => p.TypeVetement.Contains(motCle))
                         || c.Pieces.Any(p => p.Statut.Contains(motCle))))
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }
        
        // Cherche des commandes avec pagination
        public async Task<PagedResult<Commande>> RechercherPageAsync(string motCle, int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .Where(c => c.Client != null && (
                         c.Client.Nom.Contains(motCle)
                         || c.Client.Prenom.Contains(motCle)
                         || c.Pieces.Any(p => p.TypeVetement.Contains(motCle))
                         || c.Pieces.Any(p => p.Statut.Contains(motCle))))
                .OrderByDescending(c => c.DateDebut);
            
            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Commande>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        
        // Version légère de recherche pour affichage tableau
        public async Task<PagedResult<Commande>> RechercherPageLightAsync(string motCle, int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Where(c => !c.EstSupprimee)
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                    .ThenInclude(p => p.Couturier)
                .Where(c => c.Client != null && (
                         c.Client.Nom.Contains(motCle)
                         || c.Client.Prenom.Contains(motCle)
                         || c.Pieces.Any(p => p.TypeVetement.Contains(motCle))
                         || c.Pieces.Any(p => p.Statut.Contains(motCle))))
                .OrderByDescending(c => c.DateDebut);
            
            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Commande>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        public List<Mesure> ObtenirMesuresPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Mesures
                .Where(m => m.IdPieceCommande == idPieceCommande)
                .ToList();
        }

        // ==================================================================
        // Point 1 — Commandes multi-pièces (Étape 1b-ii)
        // ==================================================================

        public bool PeutAjouterPiece(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .FirstOrDefault(c => c.IdCommande == idCommande);
            if (commande == null) return false;
            // Aucun paiement encaissé = on peut ajouter librement
            return !commande.Paiements.Any(p => !p.EstAnnule);
        }

        public void AjouterPiece(int idCommande, PieceCommande piece, List<Mesure> mesures,
            bool roleBoss, string? motifException = null,
            List<MaterielSupplement>? materiaux = null, int idOperateur = 0, string nomOperateur = "")
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .FirstOrDefault(c => c.IdCommande == idCommande)
                ?? throw new InvalidOperationException("Commande introuvable.");

            bool aPaiements = commande.Paiements.Any(p => !p.EstAnnule);

            if (aPaiements)
            {
                if (!roleBoss)
                {
                    throw new InvalidOperationException(
                        "Impossible d'ajouter une pièce : un acompte a déjà été encaissé sur cette commande. " +
                        "Seul le Boss peut ajouter une pièce avec motif obligatoire.");
                }
                if (string.IsNullOrWhiteSpace(motifException))
                {
                    throw new InvalidOperationException(
                        "L'ajout d'une pièce après encaissement nécessite un motif obligatoire (Boss).");
                }
            }

            // Le statut est TOUJOURS forcé à "A faire" à l'ajout de pièce.
            piece.IdCommande = idCommande;
            piece.Statut = "A faire";

            // Le motif est conservé avec la pièce pour traçabilité complète —
            // un simple message de dialogue serait perdu à la fermeture de la fenêtre.
            if (aPaiements)
                piece.MotifAjoutApresEncaissement = motifException!.Trim();

            // Pièce + mesures + matériaux en un seul SaveChanges (transaction).
            RattacherMesuresEtMateriaux(commande, piece, mesures, materiaux, idOperateur, nomOperateur);
            context.PiecesCommande.Add(piece);
            context.SaveChanges();

            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = idCommande,
                TypeChangement = "PieceAjoutee",
                Details = $"Pièce #{piece.IdPieceCommande} ({piece.TypeVetement}) - {piece.MontantCouture:N0} FCFA"
            });
        }

        public void ModifierPiece(PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null)
        {
            using var context = _contextFactory.CreateDbContext();

            // Contrôle des droits côté service (l'écran seul ne suffit pas)
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var pieceExistante = context.PiecesCommande
                .Include(p => p.Mesures)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces) // nécessaire pour totalAutresPieces
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.MaterielSupplements) // nécessaire pour ResteAPayer
                .FirstOrDefault(p => p.IdPieceCommande == piece.IdPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            decimal ancienMontant = pieceExistante.MontantCouture;
            int? ancienCouturier = pieceExistante.IdCouturier;
            string ancienStatut = pieceExistante.Statut;
            bool montantModifie = ancienMontant != piece.MontantCouture;

            // TÂCHE 3 : Lecture seule pour la Secrétaire après Terminee/Livree
            if (operateur.RoleEnum == RoleEmploye.Secretaire &&
                (ancienStatut == "Terminee" || ancienStatut == "Livree"))
            {
                throw new InvalidOperationException(
                    "Cette pièce est terminée ou livrée. " +
                    "La Secrétaire ne peut plus la modifier à ce stade. " +
                    "Seul le Boss peut effectuer des modifications.");
            }

            // Prix : règle en fonction du contexte paiement
            // TÂCHE 3 : Sans paiement → Secrétaire peut modifier le prix.
            //           Avec paiement → Boss uniquement.
            var commandeRef = pieceExistante.Commande;
            bool aPaiementEncaisse = commandeRef?.Paiements.Any(p => !p.EstAnnule) ?? false;

            if (montantModifie && operateur.RoleEnum != RoleEmploye.Boss && aPaiementEncaisse)
            {
                throw new InvalidOperationException(
                    "Seul le Boss peut modifier le prix d'une pièce après qu'un paiement " +
                    "a été encaissé.\n" +
                    $"Prix actuel : {ancienMontant:N0} FCFA.");
            }

            // Verrouillage commission
            if (pieceExistante.IdCommission.HasValue && pieceExistante.MontantCouture != piece.MontantCouture)
            {
                throw new InvalidOperationException(
                    "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                    "une commission déjà calculée. Annulez d'abord cette commission.");
            }

            // Garde financière : montant ne peut pas descendre sous l'encaissé total
            var commande = pieceExistante.Commande;
            if (commande != null)
            {
                decimal dejaEncaisse = commande.Paiements
                    .Where(p => !p.EstAnnule)
                    .AsEnumerable()
                    .Sum(p => p.MontantPaye);

                // Total actuel de toutes les pièces (sauf celle modifiée)
                decimal totalAutresPieces = commande.Pieces
                    .Where(p => p.IdPieceCommande != piece.IdPieceCommande)
                    .AsEnumerable()
                    .Sum(p => p.MontantCouture);

                // Le client paie couture + matériaux : on compare au total facturé
                if (totalAutresPieces + piece.MontantCouture + commande.TotalMateriaux < dejaEncaisse)
                {
                    throw new InvalidOperationException(
                        $"Le montant de la pièce ({piece.MontantCouture:N0} FCFA) ferait descendre " +
                        $"le total de la commande sous le montant déjà encaissé ({dejaEncaisse:N0} FCFA).");
                }
            }

            pieceExistante.TypeVetement = piece.TypeVetement;
            pieceExistante.IdCouturier = piece.IdCouturier;
            pieceExistante.MontantCouture = piece.MontantCouture;
            pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
            pieceExistante.CheminPhoto = piece.CheminPhoto;
            if (!string.IsNullOrWhiteSpace(piece.Statut))
            {
                AppliquerDateTerminee(pieceExistante, ancienStatut, piece.Statut, idOperateur);
                pieceExistante.Statut = piece.Statut;
            }

            // Livraison : la commande doit être soldée (calculé avec le nouveau prix)
            decimal? resteLivraisonForcee = null;
            if (pieceExistante.Statut == "Livree" && ancienStatut != "Livree" && commande != null)
                resteLivraisonForcee = VerifierLivraisonSoldee(commande, operateur, motifLivraisonNonSoldee);

            // Remplacement des mesures
            context.Mesures.RemoveRange(pieceExistante.Mesures);
            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                mesure.IdCommande = pieceExistante.IdCommande;
                context.Mesures.Add(mesure);
            }

            context.SaveChanges();

            // ── Journal d'audit : prix et couturier (impact argent / commissions) ──
            if (montantModifie || ancienCouturier != piece.IdCouturier)
            {
                Auditer(operateur, nomOperateur, "PIECE_MODIFIEE", "PieceCommande",
                    pieceExistante.IdPieceCommande,
                    avant: new { MontantCouture = ancienMontant, IdCouturier = ancienCouturier },
                    apres: new { piece.MontantCouture, piece.IdCouturier },
                    motif: null);
            }
            if (resteLivraisonForcee.HasValue)
            {
                AuditerLivraisonNonSoldee(operateur, nomOperateur, pieceExistante.IdCommande,
                    resteLivraisonForcee.Value, motifLivraisonNonSoldee!,
                    $"Pièce #{pieceExistante.IdPieceCommande} ({pieceExistante.TypeVetement})");
            }

            // Notifier les vues abonnées si le montant a changé
            if (montantModifie)
            {
                CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
                {
                    IdCommande = pieceExistante.IdCommande,
                    TypeChangement = "MontantModifie",
                    Details = $"Pièce #{pieceExistante.IdPieceCommande} : {pieceExistante.MontantCouture:N0} FCFA"
                });
            }
        }

        public void SupprimerPiece(int idPieceCommande, int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();

            // TÂCHE 4 : La Secrétaire peut supprimer une pièce vierge (statut "A faire",
            // aucun paiement sur la commande, aucune commission). Le Boss peut toujours.
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var piece = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Mesures)
                .Include(p => p.MaterielSupplements)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            if (piece.IdCommission.HasValue)
                throw new InvalidOperationException(
                    "Impossible de supprimer cette pièce : elle est rattachée à une commission.");

            var commande = piece.Commande;
            bool aPaiement = commande != null && commande.Paiements.Any(p => !p.EstAnnule);

            // Secrétaire : conditions strictes (pièce vierge uniquement)
            if (operateur.RoleEnum == RoleEmploye.Secretaire)
            {
                if (aPaiement)
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut supprimer une pièce que si aucun paiement " +
                        "n'a été encaissé sur la commande. Contactez le Boss.");

                if (piece.Statut != "A faire")
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut supprimer que les pièces encore au statut " +
                        "\"À faire\" (vierges). Cette pièce est déjà en cours ou terminée. " +
                        "Contactez le Boss.");
            }
            else
            {
                // Boss : bloqué uniquement si paiements
                if (aPaiement)
                    throw new InvalidOperationException(
                        "Impossible de supprimer cette pièce : des paiements ont été encaissés sur cette commande.");
            }

            int nbPieces = context.PiecesCommande.Count(p => p.IdCommande == piece.IdCommande);
            if (nbPieces <= 1)
                throw new InvalidOperationException(
                    "Impossible de supprimer la dernière pièce d'une commande. " +
                    "Supprimez la commande entière si nécessaire.");

            if (piece.MaterielSupplements.Any())
                context.MaterielsSupplements.RemoveRange(piece.MaterielSupplements);

            var snapshot = new
            {
                piece.IdPieceCommande,
                piece.IdCommande,
                piece.TypeVetement,
                piece.MontantCouture,
                piece.IdCouturier,
                piece.Statut,
                Materiaux = piece.MaterielSupplements.Sum(m => m.Montant)
            };

            context.Mesures.RemoveRange(piece.Mesures);
            context.PiecesCommande.Remove(piece);
            context.SaveChanges();

            Auditer(operateur, nomOperateur, "PIECE_SUPPRIMEE", "PieceCommande",
                idPieceCommande, avant: snapshot, apres: null, motif: null);

            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = piece.IdCommande,
                TypeChangement = "PieceSupprimee",
                Details = $"Pièce #{idPieceCommande} supprimée"
            });
        }

        public PieceCommande DupliquerPiece(int idPieceCommandeSource)
        {
            using var context = _contextFactory.CreateDbContext();
            var source = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommandeSource)
                ?? throw new InvalidOperationException("Pièce source introuvable.");

            if (source.Commande != null && source.Commande.Paiements.Any(p => !p.EstAnnule))
            {
                throw new InvalidOperationException(
                    "Impossible de dupliquer : des paiements ont été encaissés sur cette commande.");
            }

            var nouvellePiece = new PieceCommande
            {
                IdCommande = source.IdCommande,
                TypeVetement = source.TypeVetement,
                DescriptionPrecision = source.DescriptionPrecision,
                CheminPhoto = source.CheminPhoto,
                IdCouturier = source.IdCouturier,
                MontantCouture = source.MontantCouture,
                Statut = "A faire"
            };

            context.PiecesCommande.Add(nouvellePiece);
            context.SaveChanges();

            // Dupliquer aussi les mesures
            var mesuresSource = context.Mesures
                .Where(m => m.IdPieceCommande == idPieceCommandeSource)
                .ToList();

            foreach (var m in mesuresSource)
            {
                context.Mesures.Add(new Mesure
                {
                    IdPieceCommande = nouvellePiece.IdPieceCommande,
                    IdCommande = source.IdCommande,
                    NomMesure = m.NomMesure,
                    Valeur = m.Valeur
                });
            }
            context.SaveChanges();

            // Recharger avec navigation pour renvoyer un objet complet
            context.Entry(nouvellePiece).Reference(p => p.Couturier).Load();
            return nouvellePiece;
        }

        public void ForcerStatutToutesPieces(int idCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
                .Include(c => c.Pieces)
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == idCommande);
            var pieces = commande?.Pieces ?? new List<PieceCommande>();

            if (pieces.Count == 0) return;

            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            // Vérifier qu'aucune pièce n'est verrouillée par une commission
            // (on ne force pas le statut d'une pièce commissionnée)
            var verrouillees = pieces.Where(p => p.IdCommission.HasValue).ToList();
            if (verrouillees.Any())
            {
                throw new InvalidOperationException(
                    $"{verrouillees.Count} pièce(s) sont rattachées à une commission et ne peuvent " +
                    "pas voir leur statut modifié par un forçage en cascade.");
            }

            // Livraison : même règle que pour une pièce seule
            decimal? resteLivraisonForcee = null;
            if (nouveauStatut == "Livree" && pieces.Any(p => p.Statut != "Livree"))
                resteLivraisonForcee = VerifierLivraisonSoldee(commande!, operateur, motifLivraisonNonSoldee);

            foreach (var piece in pieces)
            {
                string ancienStatut = piece.Statut;
                // Tâche 5 : flux strict — Boss peut forcer en arrière avec motif
                // (motifLivraisonNonSoldee sert aussi de motif de rétrogradation)
                VerifierFluxStatut(ancienStatut, nouveauStatut, operateur, motifLivraisonNonSoldee);
                AppliquerDateTerminee(piece, ancienStatut, nouveauStatut, idOperateur);
                piece.Statut = nouveauStatut;
            }

            context.SaveChanges();

            if (resteLivraisonForcee.HasValue)
            {
                AuditerLivraisonNonSoldee(operateur, nomOperateur, idCommande,
                    resteLivraisonForcee.Value, motifLivraisonNonSoldee!,
                    "Forçage du statut de toutes les pièces");
            }

            // ── Traçabilité : qui a forcé le statut et quand ────────────────
            // idOperateur et nomOperateur sont obligatoires (0 / vide = appelant
            // non identifié → on log quand même pour diagnostic).
            _logger.LogInformation(
                "ForcerStatutToutesPieces — Commande #{IdCommande} → statut « {Statut} » " +
                "par {Operateur} (Id={IdOperateur}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                idCommande, nouveauStatut,
                string.IsNullOrWhiteSpace(nomOperateur) ? "INCONNU" : nomOperateur,
                idOperateur,
                DateTime.Now);
        }

        public List<PieceCommande> ObtenirPiecesCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Mesures)
                .Include(p => p.MaterielSupplements)
                .Where(p => p.IdCommande == idCommande)
                .ToList();
        }

        public List<PieceCommande> ObtenirPiecesAnterieuresClient(int idClient, string typeVetement, int? exclureIdCommande = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var query = context.PiecesCommande
                .Include(p => p.Mesures)
                .Include(p => p.Commande)   // nécessaire pour LabelReutilisation (DateCreation)
                .Where(p => p.Commande != null
                    && p.Commande.IdClient == idClient
                    && p.TypeVetement == typeVetement
                    && !p.Commande.EstSupprimee);

            if (exclureIdCommande.HasValue)
                query = query.Where(p => p.IdCommande != exclureIdCommande.Value);

            return query
                .OrderByDescending(p => p.Commande!.DateCreation)  // plus récent en premier
                .ThenByDescending(p => p.IdPieceCommande)
                .Take(10)
                .ToList();
        }

        // ===== StatutView V2 — vue paginée par COMMANDE =====

        public async Task<PagedResult<Commande>> ObtenirPageCommandesStatutAsync(
            string? statut, int page, int pageSize, string? recherche = null)
        {
            using var context = _contextFactory.CreateDbContext();
            DateTime maintenant = DateTime.Now;

            // Chargement complet en une seule requête pour éviter le N+1.
            // On inclut Paiements et MaterielSupplements pour que les propriétés
            // calculées (ResteAPayer, MontantTotalAvecMateriaux) soient correctes.
            var query = context.Commandes
                .Where(c => !c.EstSupprimee)
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Pieces)
                    .ThenInclude(p => p.Couturier)
                .AsQueryable();

            // Filtre par statut (filtre virtuel "Retard" = DateFin dépassée + pièce active)
            if (!string.IsNullOrEmpty(statut))
            {
                if (statut == "Retard")
                {
                    query = query.Where(c =>
                        c.DateFin < maintenant &&
                        c.Pieces.Any(p => p.Statut == "A faire" || p.Statut == "En cours"));
                }
                else
                {
                    // Le statut global est calculé : on filtre de façon approchée
                    // (toutes les pièces au statut demandé OU au moins une pièce
                    //  pour les statuts partiels). Puis on affine en mémoire.
                    query = query.Where(c => c.Pieces.Any(p => p.Statut == statut));
                }
            }

            // Filtre recherche (nom/prénom client ou type de vêtement d'une pièce)
            if (!string.IsNullOrWhiteSpace(recherche))
            {
                query = query.Where(c =>
                    (c.Client != null &&
                     (c.Client.Nom.Contains(recherche) || c.Client.Prenom.Contains(recherche))) ||
                    c.Pieces.Any(p => p.TypeVetement.Contains(recherche)));
            }

            var totalCount = await query.CountAsync();

            // Tri : retards en tête (DateFin la plus ancienne), puis DateFin croissante
            var items = await query
                .OrderBy(c => c.DateFin)
                .ThenByDescending(c => c.DateDebut)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Affinage en mémoire si filtre statut exact (le filtre EF était approché)
            if (!string.IsNullOrEmpty(statut) && statut != "Retard")
            {
                items = items.Where(c => c.StatutGlobal == statut ||
                    c.StatutGlobal.Replace(" partiellement", "") == statut).ToList();
            }

            return new PagedResult<Commande>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        /// <summary>
        /// Change le statut d'une seule pièce sans toucher aux mesures ni au prix.
        /// Méthode légère dédiée à StatutView.
        /// </summary>
        public void ChangerStatutPiece(int idPieceCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null)
        {
            using var context = _contextFactory.CreateDbContext();

            // Contrôle des droits : Boss et Secrétaire uniquement
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var piece = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.MaterielSupplements)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            // Les pièces rattachées à une commission ne changent pas de statut en cascade
            if (piece.IdCommission.HasValue)
                throw new InvalidOperationException(
                    "Cette pièce est rattachée à une commission déjà calculée. " +
                    "Son statut ne peut pas être modifié directement. " +
                    "Annulez d'abord la commission si nécessaire.");

            string ancienStatut = piece.Statut;
            if (ancienStatut == nouveauStatut) return; // rien à faire

            // ── TÂCHE 5 : Flux strict En attente → En cours → Terminee → Livree ─
            // Retour en arrière réservé au Boss avec motif obligatoire.
            VerifierFluxStatut(ancienStatut, nouveauStatut, operateur, motifLivraisonNonSoldee);

            // Garde livraison : commande soldée ou Boss avec motif
            decimal? resteLivraisonForcee = null;
            if (nouveauStatut == "Livree" && ancienStatut != "Livree" && piece.Commande != null)
                resteLivraisonForcee = VerifierLivraisonSoldee(piece.Commande, operateur, motifLivraisonNonSoldee);

            piece.Statut = nouveauStatut;

            // ── TÂCHE 1 : mise à jour DateTerminee ───────────────────────────
            AppliquerDateTerminee(piece, ancienStatut, nouveauStatut, idOperateur);

            context.SaveChanges();

            // Audit : traçabilité statut (qui, quand, ancien → nouveau)
            Auditer(operateur, nomOperateur, "STATUT_PIECE_MODIFIE", "PieceCommande",
                idPieceCommande,
                avant: new { Statut = ancienStatut },
                apres: new { Statut = nouveauStatut, piece.DateTerminee },
                motif: null);

            if (resteLivraisonForcee.HasValue)
                AuditerLivraisonNonSoldee(operateur, nomOperateur, piece.IdCommande,
                    resteLivraisonForcee.Value, motifLivraisonNonSoldee!,
                    $"Changement statut pièce #{idPieceCommande} ({piece.TypeVetement})");

            // Notifier les autres écrans
            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = piece.IdCommande,
                TypeChangement = nouveauStatut == "Terminee" ? "PieceTerminee" : "StatutModifie",
                Details = $"Pièce #{idPieceCommande} : {ancienStatut} → {nouveauStatut}"
            });

            _logger.LogInformation(
                "ChangerStatutPiece — Pièce #{IdPiece} ({Type}) : « {Ancien} » → « {Nouveau} » " +
                "par {Operateur} (Id={IdOp}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                idPieceCommande, piece.TypeVetement, ancienStatut, nouveauStatut,
                nomOperateur, idOperateur, DateTime.Now);
        }

        // ── TÂCHE 5 : File "À attribuer" ──────────────────────────────────

        public int CompterPiecesAAttribuer()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.PiecesCommande
                .Where(p => (p.Statut == "A faire" || p.Statut == "En cours")
                         && !p.IdCouturier.HasValue
                         && p.Commande != null && !p.Commande.EstSupprimee)
                .Count();
        }

        public async Task<PagedResult<Commande>> ObtenirCommandesAAttribuerAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();

            var query = context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Paiements)
                .Where(c => !c.EstSupprimee
                         && c.Pieces.Any(p =>
                             (p.Statut == "A faire" || p.Statut == "En cours")
                             && !p.IdCouturier.HasValue))
                .OrderBy(c => c.DateFin);   // RDV les plus proches en premier

            int total = await query.CountAsync();
            var items = await query.Skip((page - 1) * pageSize).Take(pageSize).ToListAsync();

            return new PagedResult<Commande> { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
        }

        // ── TÂCHE 6 : Suggestion couturier ───────────────────────────────

        /// <summary>
        /// Suggère le couturier Actif (pas Indisponible) le moins chargé.
        /// Critère 1 : nombre de pièces A faire + En cours (le moins de travail en attente).
        /// Critère 2 à égalité : DateTerminee la plus ancienne parmi ses pièces terminées
        ///             (celui qui a terminé le moins récemment).
        /// Retourne null si aucun couturier disponible.
        /// IMPORTANT : suggestion uniquement, jamais d'affectation forcée.
        /// </summary>
        public Employe? SuggererCouturier()
        {
            using var context = _contextFactory.CreateDbContext();

            // Couturiers actifs non Indisponibles (uniquement Role=="Couturier")
            // Le Boss peut coudre mais n'est pas suggéré automatiquement : son rôle
            // est de superviser, pas d'être assigné à la création de commandes.
            var couturiers = context.Employes
                .Where(e => e.Role == "Couturier"
                         && e.Statut == "Actif")
                .ToList();

            if (couturiers.Count == 0) return null;

            // Compter les pièces actives de chaque couturier
            var charges = context.PiecesCommande
                .Where(p => (p.Statut == "A faire" || p.Statut == "En cours")
                         && p.IdCouturier.HasValue
                         && p.Commande != null && !p.Commande.EstSupprimee)
                .GroupBy(p => p.IdCouturier!.Value)
                .Select(g => new { IdCouturier = g.Key, NbActives = g.Count() })
                .ToList();

            // Dernière terminaison par couturier (pour le départage)
            var derniereTerminaison = context.PiecesCommande
                .Where(p => p.IdCouturier.HasValue && p.DateTerminee.HasValue
                         && p.Commande != null && !p.Commande.EstSupprimee)
                .GroupBy(p => p.IdCouturier!.Value)
                .Select(g => new { IdCouturier = g.Key, DerniereDate = g.Max(p => p.DateTerminee) })
                .ToList();

            Employe? meilleur = null;
            int minActives = int.MaxValue;
            DateTime? minDerniereDate = DateTime.MaxValue;

            foreach (var c in couturiers)
            {
                int actives = charges.FirstOrDefault(x => x.IdCouturier == c.IdEmploye)?.NbActives ?? 0;
                DateTime? derniere = derniereTerminaison.FirstOrDefault(x => x.IdCouturier == c.IdEmploye)?.DerniereDate;

                if (actives < minActives
                    || (actives == minActives
                        && (derniere == null
                            || (minDerniereDate.HasValue && derniere < minDerniereDate))))
                {
                    minActives      = actives;
                    minDerniereDate = derniere;
                    meilleur        = c;
                }
            }

            return meilleur;
        }

        // ===== StatutView — vue plate paginée de toutes les pièces =====

        public async Task<PagedResult<PieceCommande>> ObtenirPagePiecesAsync(
            string? statut, int page, int pageSize, string? recherche = null)
        {
            using var context = _contextFactory.CreateDbContext();

            DateTime maintenant = DateTime.Now;

            // Base : pièces dont la commande parente n'est pas supprimée
            var query = context.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Client)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Where(p => p.Commande != null && !p.Commande.EstSupprimee);

            // Filtre par statut (y compris le filtre virtuel "Retard")
            if (!string.IsNullOrEmpty(statut))
            {
                if (statut == "Retard")
                {
                    query = query.Where(p =>
                        p.Commande!.DateFin < maintenant &&
                        (p.Statut == "A faire" || p.Statut == "En cours"));
                }
                else
                {
                    query = query.Where(p => p.Statut == statut);
                }
            }

            // Filtre recherche (nom client ou type vêtement)
            if (!string.IsNullOrWhiteSpace(recherche))
            {
                query = query.Where(p =>
                    (p.Commande!.Client != null &&
                     (p.Commande.Client.Nom.Contains(recherche) ||
                      p.Commande.Client.Prenom.Contains(recherche))) ||
                    p.TypeVetement.Contains(recherche));
            }

            var totalCount = await query.CountAsync();

            // Tri : retards en premier (DateFin dépassée + statut actif),
            // puis par DateFin croissante (les RDV les plus proches en tête).
            // EF Core ne supporte pas les expressions conditionnelles complexes
            // dans OrderBy sur SQLite → on trie côté C# après pagination partielle.
            // Pour des volumes importants, un OrderBy(DateFin) suivi d'un tri
            // client est acceptable (les retards ont les DateFin les plus petites,
            // ils remontent naturellement en tête avec un tri ASC).
            var items = await query
                .OrderBy(p => p.Commande!.DateFin)
                .ThenBy(p => p.Statut)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<PieceCommande>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
    }
}
