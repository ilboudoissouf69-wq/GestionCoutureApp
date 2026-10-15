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

        // Horloge injectée pour les tests (IClock) ; SystemClock par défaut en production.
        private readonly IClock _clock;

        public CommandeService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CommandeService> logger,
            IAuditService? auditService = null,
            IClock? clock = null)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _auditService = auditService;
            _clock = clock ?? new SystemClock();
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
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
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

        /// <summary>
        /// Surcharge complète avec filtres : recherche texte, statut, couturier, période.
        /// Utilisée par CommandesView pour "Aujourd'hui" et le sélecteur de dates.
        /// </summary>
        public async Task<PagedResult<Commande>> ObtenirPageLightAsync(
            int page, int pageSize,
            string? motCle,
            string? statut,
            int? idCouturier,
            DateTime? dateDebut,
            DateTime? dateFin,
            bool inclureSupprimees = false)
        {
            using var context = _contextFactory.CreateDbContext();
            DateTime maintenant = DateTime.Now;

            // Étape 2 : les commandes supprimées/annulées ne sont jamais effacées ;
            // le Boss peut les réafficher (filtre « Afficher les supprimées »).
            var query = context.Commandes
                .Where(c => inclureSupprimees || !c.EstSupprimee)
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .AsQueryable();

            // Filtre texte
            if (!string.IsNullOrWhiteSpace(motCle))
                query = query.Where(c =>
                    (c.Client != null && (c.Client.Nom.Contains(motCle) || c.Client.Prenom.Contains(motCle))) ||
                    c.Pieces.Any(p => p.TypeVetement.Contains(motCle)));

            // Filtre statut (logique même qu'ObtenirPageCommandesStatutAsync)
            if (!string.IsNullOrEmpty(statut))
            {
                if (statut == "Retard")
                    query = query.Where(c =>
                        c.DateFin < maintenant &&
                        c.Pieces.Any(p => p.Statut == "A faire" || p.Statut == "En cours"));
                else
                    query = query.Where(c => c.Pieces.Any(p => p.Statut == statut));
            }

            // Filtre couturier
            if (idCouturier.HasValue)
                query = query.Where(c => c.Pieces.Any(p => p.IdCouturier == idCouturier.Value));

            // Filtre période — sur DateDebut (date de dépôt)
            if (dateDebut.HasValue)
                query = query.Where(c => c.DateDebut.Date >= dateDebut.Value.Date);
            if (dateFin.HasValue)
                query = query.Where(c => c.DateDebut.Date <= dateFin.Value.Date);

            var totalCount = await query.CountAsync();
            var items = await query
                .OrderByDescending(c => c.DateDebut)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            // Affinage statut en mémoire si filtre exact
            if (!string.IsNullOrEmpty(statut) && statut != "Retard")
                items = items.Where(c =>
                    c.StatutGlobal == statut ||
                    c.StatutGlobal.Replace(" partiellement", "") == statut).ToList();

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
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .FirstOrDefault(c => c.IdCommande == id);

            // Filtrer les pièces fantômes (TypeVetement vide) créées par les
            // opérations de mise à jour partielle. Ces pièces ne doivent jamais
            // apparaître dans la fiche atelier ni dans le reçu.
            if (commande != null)
                commande.Pieces = commande.Pieces
                    .Where(p => !string.IsNullOrWhiteSpace(p.TypeVetement))
                    .ToList();

            return commande;
        }

        public void Ajouter(Commande commande, PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, List<MaterielSupplement>? materiaux = null,
            List<(PieceCommande Piece, List<Mesure> Mesures, List<MaterielSupplement>? Materiaux)>? piecesSupplementaires = null)
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
            // IClock : la fenêtre d'annulation rapide (Étape 2) se mesure depuis ici.
            commande.DateCreation         = _clock.UtcNow;

            // Le statut est TOUJOURS forcé à "A faire" à la création —
            // peu importe ce que l'écran ou l'appelant a pu mettre.
            piece.Statut = "A faire";

            // Commande + pièce(s) + mesures + matériaux en UN SEUL SaveChanges :
            // EF Core l'exécute dans une transaction, donc une coupure de courant
            // ne peut plus laisser une commande à moitié enregistrée.
            // Étape 2 : les pièces saisies dans la modale de création font partie
            // de la création — elles ne passent PAS par AjouterPiece (qui, lui,
            // exige un motif de la Secrétaire pour tout ajout APRÈS création).
            commande.Pieces.Add(piece);
            RattacherMesuresEtMateriaux(commande, piece, mesures, materiaux, idOperateur, nomOperateur);
            foreach (var (pieceSup, mesuresSup, materiauxSup) in piecesSupplementaires ?? new())
            {
                pieceSup.Statut = "A faire";
                commande.Pieces.Add(pieceSup);
                RattacherMesuresEtMateriaux(commande, pieceSup, mesuresSup, materiauxSup, idOperateur, nomOperateur);
            }

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
        /// L'écriture est ATTENDUE (plus de tire-et-oublie) : l'indicateur anti-fraude
        /// du Boss ne doit jamais rater une modification de prix. Task.Run + Wait
        /// exécute l'async hors du contexte de synchronisation WPF (pas d'interblocage).
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

            Task.Run(async () =>
            {
                try
                {
                    await _auditService.EnregistrerActionAsync(
                        idOperateur: operateur.IdEmploye,
                        nomOperateur: nomOperateur,
                        roleOperateur: operateur.Role,
                        typeAction: typeAction,
                        entite: entite,
                        idEntite: idEntite,
                        valeursAvant: avant,
                        valeursApres: apres,
                        motif: motif);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex,
                        "Échec audit — {Action} sur {Entite} #{Id}", typeAction, entite, idEntite);
                }
            }).Wait(TimeSpan.FromSeconds(10));
        }

        // =====================================================================
        // NOYAU DE TRANSITION — UN SEUL CHEMIN POUR TOUT CHANGEMENT DE STATUT
        // =====================================================================

        /// <summary>
        /// Point d'entrée UNIQUE pour tout changement de statut d'une pièce.
        /// Toutes les règles métier sont ici ; aucun autre code ne modifie Piece.Statut.
        ///
        /// Flux strict  : A faire → En cours → Terminee → Livree
        ///   • Sauts en avant (ex. A faire → Livree) : INTERDITS.
        ///   • Livree → tout autre statut : INTERDIT pour tous ; passer par le module Retour.
        ///   • Retour arrière : Boss uniquement, motif obligatoire.
        ///   • Terminee → En cours/A faire : si IdCommission renseigné, refuse et propose
        ///     «Annuler la commission puis rétrograder» dans la même transaction (CommissionService
        ///     doit être passé si disponible).
        ///
        /// Livraison :
        ///   • Terminee → Livree : bloqué si ResteAPayer > 0 (LivraisonNonSoldeeException).
        ///     Boss + motif peut forcer (retourne le resteAPayer pour audit).
        ///
        /// DateTerminee / IClock :
        ///   • En cours/A faire → Terminee/Livree : horodate DateTerminee (UTC) et IdOperateurTerminee.
        ///   • Retour arrière depuis Terminee/Livree : efface DateTerminee et IdOperateurTerminee.
        ///   • Terminee → Livree : conserve DateTerminee existante.
        ///
        /// ATTENTION : cette méthode ne fait PAS SaveChanges. C'est l'appelant qui commite,
        /// pour permettre des transactions multi-pièces atomiques.
        /// </summary>
        /// <returns>
        /// Le montant RestAAPayer si le Boss a forcé une livraison non soldée (à auditer après commit),
        /// null sinon.
        /// </returns>
        private decimal? TransitionnerStatutPieceCoeur(
            PieceCommande piece,
            string nouveauStatut,
            Employe operateur,
            string? motif,
            Commande? commande,
            CommissionService? commissionService = null)
        {
            string ancienStatut = piece.Statut;
            if (ancienStatut == nouveauStatut) return null; // idempotent

            static int Rang(string s) => s switch
            {
                "A faire"  => 0,
                "En cours" => 1,
                "Terminee" => 2,
                "Livree"   => 3,
                _          => -1
            };

            int r1 = Rang(ancienStatut);
            int r2 = Rang(nouveauStatut);

            // ── 1. Livree → autre : interdit pour tous (module Retour) ────────
            if (ancienStatut == "Livree" && r2 < r1)
                throw new InvalidOperationException(
                    "Une pièce Livrée ne peut pas être rétrogradée directement. " +
                    "Utilisez le module « Retour » pour traiter ce cas.");

            // ── 2. Saut interdit (avance de plus d'un cran) ──────────────────
            if (r2 > r1 + 1)
                throw new InvalidOperationException(
                    $"Transition interdite : \"{ancienStatut}\" → \"{nouveauStatut}\". " +
                    $"Le flux strict est A faire → En cours → Terminée → Livrée. " +
                    $"Passez d'abord par « {(r1 == 0 ? "En cours" : "Terminée")} ».");

            // ── 3. Statut inconnu ─────────────────────────────────────────────
            if (r2 == -1)
                throw new InvalidOperationException($"Statut inconnu : \"{nouveauStatut}\".");

            // ── 4. Retour arrière ─────────────────────────────────────────────
            if (r2 < r1)
            {
                if (operateur.RoleEnum != RoleEmploye.Boss)
                    throw new InvalidOperationException(
                        $"Seul le Boss peut rétrograder le statut d'une pièce " +
                        $"(\"{ancienStatut}\" → \"{nouveauStatut}\"). " +
                        "Contactez le Boss pour effectuer cette opération.");

                if (string.IsNullOrWhiteSpace(motif))
                    throw new InvalidOperationException(
                        $"Un motif est obligatoire pour rétrograder le statut " +
                        $"(\"{ancienStatut}\" → \"{nouveauStatut}\").");

                // Pièce commissionnée : refus OU annulation+rétrogradation dans la même transaction
                if (piece.IdCommission.HasValue)
                {
                    if (commissionService == null)
                        throw new PieceCommissionneeException(piece.IdCommission.Value, motif);

                    // Annuler la commission DANS la même transaction (l'appelant gère le commit)
                    commissionService.AnnulerDansTransaction(
                        piece.IdCommission.Value, motif!,
                        operateur.IdEmploye, $"{operateur.Prenom} {operateur.Nom}".Trim());
                    // IdCommission mis à null par AnnulerDansTransaction
                }
            }

            // ── 5. Garde livraison non soldée ─────────────────────────────────
            decimal? resteLivraisonForcee = null;
            if (nouveauStatut == "Livree" && commande != null)
            {
                decimal reste = commande.ResteAPayer;
                if (reste > 0.01m)
                {
                    if (operateur.RoleEnum != RoleEmploye.Boss)
                        throw new LivraisonNonSoldeeException(reste, peutForcer: false);
                    if (string.IsNullOrWhiteSpace(motif))
                        throw new LivraisonNonSoldeeException(reste, peutForcer: true);
                    resteLivraisonForcee = reste;
                }
            }

            // ── 6. DateTerminee via IClock ────────────────────────────────────
            bool passeEnTerminee = (nouveauStatut == "Terminee" || nouveauStatut == "Livree")
                                   && ancienStatut != "Terminee" && ancienStatut != "Livree";
            bool reprise = (nouveauStatut == "A faire" || nouveauStatut == "En cours")
                         && (ancienStatut == "Terminee" || ancienStatut == "Livree");

            if (passeEnTerminee && piece.DateTerminee == null)
            {
                piece.DateTerminee = _clock.UtcNow;
                piece.IdOperateurTerminee = operateur.IdEmploye > 0 ? operateur.IdEmploye : null;
            }
            else if (reprise)
            {
                piece.DateTerminee = null;
                piece.IdOperateurTerminee = null;
            }
            // Terminee → Livree : on conserve DateTerminee existante

            // ── 7. Appliquer le nouveau statut ────────────────────────────────
            piece.Statut = nouveauStatut;

            return resteLivraisonForcee;
        }

        /// <summary>
        /// Exception levée quand une pièce commissionnée est rétrogradée sans fournir
        /// un CommissionService pour annuler atomiquement.
        /// La vue doit proposer «Annuler commission + rétrograder» à l'écran.
        /// </summary>
        public sealed class PieceCommissionneeException : InvalidOperationException
        {
            public int IdCommission { get; }
            public string? Motif { get; }

            public PieceCommissionneeException(int idCommission, string? motif)
                : base(
                    $"Cette pièce est rattachée à la commission #{idCommission}. " +
                    "Son statut ne peut pas être rétrogradé directement.\n\n" +
                    "Option disponible pour le Boss : annuler cette commission ET rétrograder " +
                    "la pièce en une seule opération (bouton « Annuler commission + Rétrograder »).")
            {
                IdCommission = idCommission;
                Motif        = motif;
            }
        }

        /// <summary>
        /// Livraison d'une pièce alors que la commande n'est pas soldée.
        /// Conservé pour rétrocompatibilité avec les vues.
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

        // =====================================================================
        // ÉTAPE 2 — VERROUILLAGE SUPPRESSION / ANNULATION / MODIFICATION
        // =====================================================================

        /// <summary>Formule affichée à la Secrétaire chaque fois qu'une action lui est refusée.</summary>
        public const string MessageDemandezAuBoss = "Demandez au Boss.";

        private static bool EstTermineeOuLivree(string statut) =>
            statut == "Terminee" || statut == "Livree";

        /// <summary>
        /// Commande Terminée/Livrée (TOUTES ses pièces) : lecture seule pour tous.
        /// Le Boss doit d'abord rétrograder une pièce (TransitionnerStatutPiece + motif).
        /// </summary>
        private static bool EstEnLectureSeule(Commande commande) =>
            commande.Pieces.Count > 0 && commande.Pieces.All(p => EstTermineeOuLivree(p.Statut));

        private static bool APaiementEncaisse(Commande commande) =>
            commande.Paiements.Any(p => !p.EstAnnule);

        private static void ExigerCommandeActive(Commande commande)
        {
            if (commande.EstSupprimee)
                throw new InvalidOperationException(
                    commande.EstAnnulee
                        ? "Cette commande a été annulée : elle n'est plus modifiable."
                        : "Cette commande a été supprimée : elle n'est plus modifiable.");
        }

        private static void ExigerCommandeModifiable(Commande commande)
        {
            ExigerCommandeActive(commande);
            if (EstEnLectureSeule(commande))
                throw new InvalidOperationException(
                    "Cette commande est terminée ou livrée : elle est en lecture seule. " +
                    "Seul le Boss peut d'abord rétrograder une pièce (avec motif) pour la modifier.");
        }

        private static string ExigerMotif(string? motif, string action)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException($"Un motif est obligatoire pour {action}.");
            return motif.Trim();
        }

        private static string Normaliser(string? s) => (s ?? string.Empty).Trim();

        /// <summary>
        /// Délai (minutes) d'annulation rapide, lu dans Parametres à chaque appel
        /// pour qu'une modification du Boss soit prise en compte immédiatement.
        /// </summary>
        private static int LireDelaiAnnulationSecretaire(ApplicationDbContext context)
        {
            var valeur = context.Parametres.Find(ParametresService.CleDelaiAnnulationSecretaire)?.Valeur;
            return int.TryParse(valeur, out int minutes) && minutes >= 0
                ? minutes
                : ParametresService.DelaiAnnulationSecretaireDefautMinutes;
        }

        /// <summary>
        /// Raison pour laquelle la Secrétaire ne peut PAS annuler la commande, ou null si elle le peut.
        /// Les quatre conditions du cahier sont cumulatives.
        /// </summary>
        private string? RaisonRefusAnnulation(ApplicationDbContext context, Commande commande, Employe operateur)
        {
            if (operateur.RoleEnum != RoleEmploye.Secretaire)
                return "L'annulation rapide est réservée à la Secrétaire (le Boss utilise la suppression).";
            if (commande.EstSupprimee)
                return "Cette commande est déjà supprimée ou annulée.";
            if (commande.IdOperateurCreation != operateur.IdEmploye)
                return "Vous ne pouvez annuler que les commandes que vous avez créées vous-même.";

            int delaiMinutes = LireDelaiAnnulationSecretaire(context);
            if (_clock.UtcNow - commande.DateCreation >= TimeSpan.FromMinutes(delaiMinutes))
                return $"Le délai d'annulation ({delaiMinutes} minute(s) après la création) est dépassé.";
            if (commande.Paiements.Any())
                return "Un paiement (même annulé) est lié à cette commande.";
            if (commande.Pieces.Any(p => p.Statut != "A faire"))
                return "Au moins une pièce a déjà dépassé le statut « À faire ».";
            if (commande.DateImpressionRecu.HasValue)
                return "Le reçu / ticket de cette commande a déjà été imprimé.";
            return null;
        }

        public (bool Autorise, string Raison) PeutAnnulerParSecretaire(int idCommande, int idOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            var operateur = context.Employes.Find(idOperateur);
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Pieces)
                .FirstOrDefault(c => c.IdCommande == idCommande);
            if (operateur == null || commande == null)
                return (false, "Commande ou opérateur introuvable.");

            string? raison = RaisonRefusAnnulation(context, commande, operateur);
            return raison == null ? (true, string.Empty) : (false, raison);
        }

        public async Task AnnulerParSecretaireAsync(int idCommande, int idOperateur,
            string nomOperateur, string? motif = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Secretaire);

            var commande = context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces)
                .FirstOrDefault(c => c.IdCommande == idCommande)
                ?? throw new InvalidOperationException("Commande introuvable.");

            string? raison = RaisonRefusAnnulation(context, commande, operateur);
            if (raison != null)
                throw new InvalidOperationException(
                    $"Annulation impossible : {raison}\n\n{MessageDemandezAuBoss}");

            var snapshot = new
            {
                commande.IdCommande,
                Client = commande.Client?.Nom + " " + commande.Client?.Prenom,
                commande.DateCreation,
                MontantTotal = commande.MontantTotalAvecMateriaux,
                Pieces = commande.Pieces.Select(p => new { p.TypeVetement, p.MontantCouture }).ToList()
            };
            string motifFinal = string.IsNullOrWhiteSpace(motif)
                ? "Annulation rapide par la secrétaire"
                : motif.Trim();

            // Annulée = retirée de l'activité exactement comme une suppression
            // (EstSupprimee), mais distinguable par le Boss (EstAnnulee).
            commande.EstSupprimee            = true;
            commande.EstAnnulee              = true;
            commande.MotifSuppression        = motifFinal;
            commande.DateSuppression         = _clock.Now;
            commande.IdOperateurSuppression  = idOperateur;
            commande.NomOperateurSuppression = nomOperateur;
            context.SaveChanges();

            if (_auditService != null)
                await _auditService.EnregistrerActionAsync(
                    idOperateur, nomOperateur, operateur.Role,
                    ActionsAntiFraude.Annulation, "Commande", idCommande,
                    valeursAvant: snapshot, valeursApres: null, motif: motifFinal);

            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = idCommande,
                TypeChangement = "CommandeAnnulee",
                Details = motifFinal
            });
        }

        public void MarquerRecuImprime(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes.Find(idCommande);
            // Seule la PREMIÈRE impression compte : une réimpression ne déplace pas la date.
            if (commande == null || commande.DateImpressionRecu.HasValue) return;
            commande.DateImpressionRecu = _clock.UtcNow;
            context.SaveChanges();
        }

        /// <summary>
        /// Vérifie les droits de modification d'UNE pièce et renvoie ce qui change.
        /// Partagé par Modifier() et ModifierPiece() pour qu'aucun chemin n'y échappe.
        /// </summary>
        private (bool PrixModifie, bool AutresModifies) VerifierModificationPiece(
            Employe operateur, Commande commande, PieceCommande existante,
            PieceCommande demandee, string? motifModification)
        {
            bool estBoss = operateur.RoleEnum == RoleEmploye.Boss;
            bool prixModifie = existante.MontantCouture != demandee.MontantCouture;
            bool typeModifie = Normaliser(existante.TypeVetement) != Normaliser(demandee.TypeVetement);
            bool autresModifies = typeModifie
                || existante.IdCouturier != demandee.IdCouturier
                || Normaliser(existante.DescriptionPrecision) != Normaliser(demandee.DescriptionPrecision)
                || Normaliser(existante.CheminPhoto) != Normaliser(demandee.CheminPhoto);

            ExigerCommandeActive(commande);

            // Pièce Terminée/Livrée : lecture seule pour TOUS. Seul un changement
            // de statut (rétrogradation Boss + motif via le noyau) reste possible.
            if (EstTermineeOuLivree(existante.Statut))
            {
                if (!estBoss)
                    throw new InvalidOperationException(
                        "Cette pièce est terminée ou livrée. " +
                        "La Secrétaire ne peut plus la modifier à ce stade. " + MessageDemandezAuBoss);
                if (prixModifie || autresModifies)
                    throw new InvalidOperationException(
                        "Cette pièce est terminée ou livrée : elle est en lecture seule. " +
                        "Rétrogradez-la d'abord (avec motif) pour pouvoir la modifier.");
            }

            if (!estBoss)
            {
                bool aPaiement = APaiementEncaisse(commande);
                if (aPaiement && typeModifie)
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut pas modifier le type de vêtement " +
                        "après qu'un paiement a été encaissé. " + MessageDemandezAuBoss);

                if (prixModifie)
                {
                    if (aPaiement)
                        throw new InvalidOperationException(
                            "Seul le Boss peut modifier le prix d'une pièce après qu'un paiement " +
                            "a été encaissé.\n" +
                            $"Prix actuel : {existante.MontantCouture:N0} FCFA. " + MessageDemandezAuBoss);
                    if (commande.DateImpressionRecu.HasValue)
                        throw new InvalidOperationException(
                            "Le reçu de cette commande a déjà été imprimé : le prix est verrouillé. " +
                            MessageDemandezAuBoss);
                    ExigerMotif(motifModification, "modifier le prix d'une pièce");
                }
            }

            return (prixModifie, autresModifies);
        }

        /// <summary>
        /// Règles d'ajout / suppression de pièce APRÈS la création (la Secrétaire
        /// change ainsi le total facturé) : motif obligatoire, interdit après
        /// impression du reçu. Renvoie le motif normalisé (null si non requis).
        /// </summary>
        private static string? VerifierAjoutSuppressionPieceSecretaire(Commande commande, string? motif, string action)
        {
            if (commande.DateImpressionRecu.HasValue)
                throw new InvalidOperationException(
                    $"Le reçu de cette commande a déjà été imprimé : impossible de {action}. " +
                    MessageDemandezAuBoss);
            return ExigerMotif(motif, action);
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
            int idOperateur, string nomOperateur, string? motifModification = null)
        {
            using var context = _contextFactory.CreateDbContext();

            // ── Contrôle des droits : Boss et Secrétaire uniquement ──────────
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);
            bool estBoss = operateur.RoleEnum == RoleEmploye.Boss;

            var existante = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .FirstOrDefault(c => c.IdCommande == commande.IdCommande);

            if (existante == null) return;
            ExigerCommandeActive(existante);

            // Règle 3A : dès qu'une pièce est Terminee/Livree → lecture seule pour la Secrétaire
            if (!estBoss && existante.Pieces.Any(p => EstTermineeOuLivree(p.Statut)))
                throw new InvalidOperationException(
                    "Cette commande est terminée ou livrée. " +
                    "La Secrétaire ne peut plus la modifier. " + MessageDemandezAuBoss);

            // Règle 3B : après paiement → client verrouillé (Boss uniquement)
            bool clientModifie = existante.IdClient != commande.IdClient;
            if (clientModifie && !estBoss && APaiementEncaisse(existante))
                throw new InvalidOperationException(
                    "La Secrétaire ne peut pas modifier le client d'une commande " +
                    "après qu'un paiement a été encaissé. " + MessageDemandezAuBoss);

            bool infosModifiees = clientModifie
                || existante.DateFin != commande.DateFin
                || existante.HeureDebut != commande.HeureDebut
                || existante.HeureFin != commande.HeureFin;
            if (infosModifiees)
                ExigerCommandeModifiable(existante);

            var avantInfos = new { existante.IdClient, existante.DateFin, existante.HeureDebut, existante.HeureFin };

            // ── Mise à jour de la pièce existante ───────────────────────────
            var pieceExistante = existante.Pieces.FirstOrDefault();
            bool prixModifie = false, autresModifies = false;
            decimal ancienMontant = 0m;
            object? avantPiece = null;

            if (pieceExistante != null)
            {
                (prixModifie, autresModifies) = VerifierModificationPiece(
                    operateur, existante, pieceExistante, piece, motifModification);
                bool pieceVerrouillee = EstTermineeOuLivree(pieceExistante.Statut);

                // Verrouillage commission — ni Boss ni Secrétaire ne peut changer le montant
                if (pieceExistante.IdCommission.HasValue && prixModifie)
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

                ancienMontant = pieceExistante.MontantCouture;
                avantPiece = new
                {
                    pieceExistante.TypeVetement, pieceExistante.IdCouturier,
                    pieceExistante.DescriptionPrecision, pieceExistante.Statut
                };

                // Les droits ont été vérifiés par VerifierModificationPiece : tout
                // champ qui diffère ici est un changement autorisé pour cet opérateur.
                pieceExistante.TypeVetement = piece.TypeVetement;
                pieceExistante.MontantCouture = piece.MontantCouture;
                pieceExistante.IdCouturier = piece.IdCouturier;
                pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
                pieceExistante.CheminPhoto = piece.CheminPhoto;
                if (!string.IsNullOrWhiteSpace(piece.Statut) && piece.Statut != pieceExistante.Statut)
                {
                    // Passe par le noyau : flux strict, IClock, garde commission, garde livraison
                    TransitionnerStatutPieceCoeur(pieceExistante, piece.Statut, operateur,
                        null, existante);
                }

                // Pièce terminée/livrée : ses mesures sont figées.
                if (!pieceVerrouillee)
                {
                    context.Mesures.RemoveRange(pieceExistante.Mesures);
                    foreach (var mesure in mesures)
                    {
                        mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                        mesure.IdCommande = existante.IdCommande;
                        context.Mesures.Add(mesure);
                    }
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

            existante.IdClient   = commande.IdClient;
            existante.DateFin    = commande.DateFin;
            existante.HeureDebut = commande.HeureDebut;
            existante.HeureFin   = commande.HeureFin;

            context.SaveChanges();

            // ── Journal d'audit (Étape 2) ────────────────────────────────────
            if (prixModifie && pieceExistante != null)
                AuditerModificationPrix(operateur, nomOperateur, existante.IdCommande,
                    pieceExistante.IdPieceCommande, ancienMontant, piece.MontantCouture, motifModification);
            if (infosModifiees || autresModifies)
                Auditer(operateur, nomOperateur, "COMMANDE_MODIFIEE", "Commande", existante.IdCommande,
                    avant: new { Infos = avantInfos, Piece = avantPiece },
                    apres: new
                    {
                        Infos = new { commande.IdClient, commande.DateFin, commande.HeureDebut, commande.HeureFin },
                        Piece = new { piece.TypeVetement, piece.IdCouturier, piece.DescriptionPrecision, piece.Statut }
                    },
                    motif: string.IsNullOrWhiteSpace(motifModification) ? null : motifModification.Trim());

            _logger.LogInformation(
                "Modifier — Commande #{IdCommande} par {Operateur} (Id={IdOp}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                existante.IdCommande, nomOperateur, idOperateur, DateTime.Now);
        }

        private void AuditerModificationPrix(Employe operateur, string nomOperateur, int idCommande,
            int idPieceCommande, decimal ancienMontant, decimal nouveauMontant, string? motif)
        {
            Auditer(operateur, nomOperateur, ActionsAntiFraude.PrixModifie, "Commande", idCommande,
                avant: new { IdPieceCommande = idPieceCommande, MontantCouture = ancienMontant },
                apres: new { IdPieceCommande = idPieceCommande, MontantCouture = nouveauMontant },
                motif: string.IsNullOrWhiteSpace(motif) ? null : motif.Trim());
        }

        /// <summary>
        /// Modifie UNIQUEMENT les métadonnées de la commande (client, dates, heures)
        /// sans toucher aux pièces. Utilisé depuis la modale multi-pièces pour éviter
        /// de corrompre les pièces avec une pièce fantôme vide.
        /// Étape 2 : client verrouillé après paiement (Boss uniquement), lecture
        /// seule si la commande est Terminée/Livrée, toute modification auditée.
        /// Un appel sans aucun changement est accepté (la modale renvoie toujours tout).
        /// </summary>
        public void ModifierInfosCommande(int idCommande, int idClient, DateTime dateFin,
            TimeSpan heureDebut, TimeSpan? heureFin, int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var existante = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Pieces)
                .FirstOrDefault(c => c.IdCommande == idCommande);
            if (existante == null) return;

            bool clientModifie = existante.IdClient != idClient;
            bool modifie = clientModifie
                || existante.DateFin != dateFin
                || existante.HeureDebut != heureDebut
                || existante.HeureFin != heureFin;
            if (!modifie) return;

            ExigerCommandeModifiable(existante);

            if (clientModifie && operateur.RoleEnum != RoleEmploye.Boss && APaiementEncaisse(existante))
                throw new InvalidOperationException(
                    "La Secrétaire ne peut pas modifier le client d'une commande " +
                    "après qu'un paiement a été encaissé. " + MessageDemandezAuBoss);

            var avant = new { existante.IdClient, existante.DateFin, existante.HeureDebut, existante.HeureFin };

            existante.IdClient   = idClient;
            existante.DateFin    = dateFin;
            existante.HeureDebut = heureDebut;
            existante.HeureFin   = heureFin;

            context.SaveChanges();

            Auditer(operateur, nomOperateur, "COMMANDE_MODIFIEE", "Commande", idCommande,
                avant: avant,
                apres: new { IdClient = idClient, DateFin = dateFin, HeureDebut = heureDebut, HeureFin = heureFin },
                motif: null);

            _logger.LogInformation(
                "ModifierInfosCommande — Commande #{IdCommande} par {Operateur} (Id={IdOp}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                idCommande, nomOperateur, idOperateur, DateTime.Now);
        }


        /// <summary>
        /// Suppression LOGIQUE d'une commande — Boss uniquement (Étape 2), motif
        /// obligatoire, audit immuable. Une commande ne doit jamais être supprimée
        /// physiquement : son numéro n'est donc jamais réutilisé, et elle reste
        /// consultable par le Boss (filtre « Afficher les supprimées »).
        /// La Secrétaire n'a plus ce droit : elle dispose de
        /// <see cref="AnnulerParSecretaireAsync"/> (fenêtre courte, conditions strictes).
        /// </summary>
        /// <param name="id">ID de la commande</param>
        /// <param name="idOperateur">ID de l'opérateur effectuant la suppression</param>
        /// <param name="nomOperateur">Nom de l'opérateur (traçabilité)</param>
        /// <param name="motif">Motif OBLIGATOIRE de la suppression</param>
        /// <param name="auditService">Service d'audit (à défaut, celui injecté dans le service)</param>
        public async Task SupprimerAsync(int id, int idOperateur, string nomOperateur, string motif, IAuditService? auditService = null)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException(
                    "Le motif de suppression est OBLIGATOIRE pour des raisons de traçabilité.");

            using var context = _contextFactory.CreateDbContext();

            var operateur = context.Employes.Find(idOperateur);
            if (operateur == null)
                throw new UnauthorizedAccessException("Opérateur introuvable.");

            bool estBoss = operateur.RoleEnum == RoleEmploye.Boss;
            if (!estBoss)
                throw new UnauthorizedAccessException(
                    "Seul le Boss peut supprimer une commande. " +
                    "La Secrétaire peut uniquement ANNULER une commande qu'elle vient de créer " +
                    "(aucun paiement, rien commencé, reçu non imprimé). " + MessageDemandezAuBoss);

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

            // ── Bloque si paiements (historique comptable) ──────────────────
            if (commande.Paiements.Any())
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
            commande.DateSuppression = _clock.Now;
            commande.IdOperateurSuppression = idOperateur;
            commande.NomOperateurSuppression = nomOperateur;

            context.SaveChanges();

            // Enregistrer dans le journal d'audit immuable avec notification
            auditService ??= _auditService;
            if (auditService != null)
            {
                await auditService.EnregistrerActionAsync(
                    idOperateur: idOperateur,
                    nomOperateur: nomOperateur,
                    roleOperateur: operateur.Role,
                    typeAction: ActionsAntiFraude.Suppression,
                    entite: "Commande",
                    idEntite: id,
                    valeursAvant: snapshot,
                    valeursApres: null,
                    motif: motif,
                    envoyerNotification: true
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
                .Include(c => c.Paiements)
                .Include(c => c.MaterielSupplements)
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
                .Include(c => c.Pieces)
                .FirstOrDefault(c => c.IdCommande == idCommande)
                ?? throw new InvalidOperationException("Commande introuvable.");

            // Étape 2 : dès qu'un opérateur est identifié, son rôle RÉEL (en base)
            // fait foi — le booléen roleBoss de l'appelant ne suffit plus.
            Employe? operateur = null;
            if (idOperateur > 0)
            {
                operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);
                roleBoss = operateur.RoleEnum == RoleEmploye.Boss;
            }

            ExigerCommandeModifiable(commande);

            bool aPaiements = APaiementEncaisse(commande);
            string? motif = null;

            if (aPaiements)
            {
                if (!roleBoss)
                {
                    throw new InvalidOperationException(
                        "Impossible d'ajouter une pièce : un acompte a déjà été encaissé sur cette commande. " +
                        "Seul le Boss peut ajouter une pièce avec motif obligatoire. " + MessageDemandezAuBoss);
                }
                motif = ExigerMotif(motifException, "ajouter une pièce après encaissement (Boss)");
            }
            else if (!roleBoss)
            {
                // Secrétaire, avant paiement : ajouter une pièce après la création
                // change le total facturé → motif obligatoire, interdit après impression.
                motif = VerifierAjoutSuppressionPieceSecretaire(commande, motifException,
                    "ajouter une pièce après la création de la commande");
            }

            // Le statut est TOUJOURS forcé à "A faire" à l'ajout de pièce.
            piece.IdCommande = idCommande;
            piece.Statut = "A faire";

            // Le motif est conservé avec la pièce pour traçabilité complète —
            // un simple message de dialogue serait perdu à la fermeture de la fenêtre.
            if (aPaiements)
                piece.MotifAjoutApresEncaissement = motif;

            // Pièce + mesures + matériaux en un seul SaveChanges (transaction).
            RattacherMesuresEtMateriaux(commande, piece, mesures, materiaux, idOperateur, nomOperateur);
            context.PiecesCommande.Add(piece);
            context.SaveChanges();

            if (operateur != null)
                Auditer(operateur, nomOperateur, ActionsAntiFraude.PieceAjoutee, "PieceCommande",
                    piece.IdPieceCommande, avant: null,
                    apres: new { piece.IdCommande, piece.TypeVetement, piece.MontantCouture },
                    motif: motif);
            else
                _logger.LogWarning(
                    "AjouterPiece sans opérateur identifié — Commande #{IdCommande}, pièce #{IdPiece} non auditée.",
                    idCommande, piece.IdPieceCommande);

            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = idCommande,
                TypeChangement = "PieceAjoutee",
                Details = $"Pièce #{piece.IdPieceCommande} ({piece.TypeVetement}) - {piece.MontantCouture:N0} FCFA"
            });
        }

        public void ModifierPiece(PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null,
            string? motifModification = null)
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

            var commande = pieceExistante.Commande
                ?? throw new InvalidOperationException("Commande de la pièce introuvable.");

            decimal ancienMontant = pieceExistante.MontantCouture;
            var avantPiece = new
            {
                pieceExistante.TypeVetement, pieceExistante.IdCouturier,
                pieceExistante.DescriptionPrecision, pieceExistante.CheminPhoto
            };

            // Étape 2 : toutes les règles par rôle (lecture seule Terminée/Livrée,
            // verrous après paiement / après impression, motif de prix) sont ici.
            var (montantModifie, autresModifies) = VerifierModificationPiece(
                operateur, commande, pieceExistante, piece, motifModification);
            bool pieceVerrouillee = EstTermineeOuLivree(pieceExistante.Statut);

            // Verrouillage commission
            if (pieceExistante.IdCommission.HasValue && montantModifie)
            {
                throw new InvalidOperationException(
                    "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                    "une commission déjà calculée. Annulez d'abord cette commission.");
            }

            // Garde financière : montant ne peut pas descendre sous l'encaissé total
            decimal dejaEncaisse = commande.Paiements
                .Where(p => !p.EstAnnule)
                .Sum(p => p.MontantPaye);

            // Total actuel de toutes les pièces (sauf celle modifiée)
            decimal totalAutresPieces = commande.Pieces
                .Where(p => p.IdPieceCommande != piece.IdPieceCommande)
                .Sum(p => p.MontantCouture);

            // Le client paie couture + matériaux : on compare au total facturé
            if (totalAutresPieces + piece.MontantCouture + commande.TotalMateriaux < dejaEncaisse)
            {
                throw new InvalidOperationException(
                    $"Le montant de la pièce ({piece.MontantCouture:N0} FCFA) ferait descendre " +
                    $"le total de la commande sous le montant déjà encaissé ({dejaEncaisse:N0} FCFA).");
            }

            pieceExistante.TypeVetement = piece.TypeVetement;
            pieceExistante.IdCouturier = piece.IdCouturier;
            pieceExistante.MontantCouture = piece.MontantCouture;
            pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
            pieceExistante.CheminPhoto = piece.CheminPhoto;
            decimal? resteLivraisonForcee = null;
            if (!string.IsNullOrWhiteSpace(piece.Statut) && piece.Statut != pieceExistante.Statut)
            {
                // Passe par le noyau unique : flux strict, IClock, garde commission, garde livraison
                resteLivraisonForcee = TransitionnerStatutPieceCoeur(
                    pieceExistante, piece.Statut, operateur, motifLivraisonNonSoldee, commande);
            }

            // Remplacement des mesures — figées une fois la pièce terminée/livrée.
            if (!pieceVerrouillee)
            {
                context.Mesures.RemoveRange(pieceExistante.Mesures);
                foreach (var mesure in mesures)
                {
                    mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                    mesure.IdCommande = pieceExistante.IdCommande;
                    context.Mesures.Add(mesure);
                }
            }

            context.SaveChanges();

            // ── Journal d'audit ──────────────────────────────────────────────
            if (montantModifie)
                AuditerModificationPrix(operateur, nomOperateur, pieceExistante.IdCommande,
                    pieceExistante.IdPieceCommande, ancienMontant, piece.MontantCouture, motifModification);
            if (autresModifies)
                Auditer(operateur, nomOperateur, "PIECE_MODIFIEE", "PieceCommande",
                    pieceExistante.IdPieceCommande,
                    avant: avantPiece,
                    apres: new { piece.TypeVetement, piece.IdCouturier, piece.DescriptionPrecision, piece.CheminPhoto },
                    motif: string.IsNullOrWhiteSpace(motifModification) ? null : motifModification.Trim());
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

        public void SupprimerPiece(int idPieceCommande, int idOperateur, string nomOperateur,
            string? motif = null)
        {
            using var context = _contextFactory.CreateDbContext();

            // TÂCHE 4 : La Secrétaire peut supprimer une pièce vierge (statut "A faire",
            // aucun paiement sur la commande, aucune commission). Le Boss peut toujours.
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var piece = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces)
                .Include(p => p.Mesures)
                .Include(p => p.MaterielSupplements)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            if (piece.IdCommission.HasValue)
                throw new InvalidOperationException(
                    "Impossible de supprimer cette pièce : elle est rattachée à une commission.");

            var commande = piece.Commande
                ?? throw new InvalidOperationException("Commande de la pièce introuvable.");
            ExigerCommandeModifiable(commande);
            bool aPaiement = APaiementEncaisse(commande);
            string? motifFinal = string.IsNullOrWhiteSpace(motif) ? null : motif.Trim();

            // Secrétaire : conditions strictes (pièce vierge uniquement)
            if (operateur.RoleEnum == RoleEmploye.Secretaire)
            {
                if (aPaiement)
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut supprimer une pièce que si aucun paiement " +
                        "n'a été encaissé sur la commande. " + MessageDemandezAuBoss);

                if (piece.Statut != "A faire")
                    throw new InvalidOperationException(
                        "La Secrétaire ne peut supprimer que les pièces encore au statut " +
                        "\"À faire\" (vierges). Cette pièce est déjà en cours ou terminée. " +
                        MessageDemandezAuBoss);

                // Étape 2 : change le total facturé → motif obligatoire, interdit après impression
                motifFinal = VerifierAjoutSuppressionPieceSecretaire(commande, motif, "supprimer une pièce");
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

            Auditer(operateur, nomOperateur, ActionsAntiFraude.PieceSupprimee, "PieceCommande",
                idPieceCommande, avant: snapshot, apres: null, motif: motifFinal);

            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = piece.IdCommande,
                TypeChangement = "PieceSupprimee",
                Details = $"Pièce #{idPieceCommande} supprimée"
            });
        }

        public PieceCommande DupliquerPiece(int idPieceCommandeSource, int idOperateur,
            string nomOperateur, string? motif = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var operateur = ExigerRole(context, idOperateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var source = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommandeSource)
                ?? throw new InvalidOperationException("Pièce source introuvable.");
            var commande = source.Commande
                ?? throw new InvalidOperationException("Commande de la pièce introuvable.");

            ExigerCommandeModifiable(commande);
            if (APaiementEncaisse(commande))
            {
                throw new InvalidOperationException(
                    "Impossible de dupliquer : des paiements ont été encaissés sur cette commande.");
            }

            // Étape 2 : dupliquer = ajouter une pièce (le total change) → mêmes règles.
            string? motifFinal = operateur.RoleEnum == RoleEmploye.Boss
                ? (string.IsNullOrWhiteSpace(motif) ? null : motif.Trim())
                : VerifierAjoutSuppressionPieceSecretaire(commande, motif,
                    "ajouter une pièce après la création de la commande");

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

            Auditer(operateur, nomOperateur, ActionsAntiFraude.PieceAjoutee, "PieceCommande",
                nouvellePiece.IdPieceCommande, avant: null,
                apres: new { nouvellePiece.IdCommande, nouvellePiece.TypeVetement, nouvellePiece.MontantCouture,
                             DupliqueeDe = idPieceCommandeSource },
                motif: motifFinal);

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
            ExigerCommandeActive(commande!);

            // On filtre les pièces déjà au statut cible pour ne pas lever une erreur inutile
            var piecesATraiter = pieces.Where(p => p.Statut != nouveauStatut).ToList();
            if (piecesATraiter.Count == 0) return;

            // InMemory ne supporte pas les vraies transactions : on les saute silencieusement.
            // SQLite (production) les supporte. On détecte par le nom du provider.
            bool avecTransaction = context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
            using var transaction = avecTransaction ? context.Database.BeginTransaction() : null;
            try
            {
                decimal? resteLivraisonForcee = null;

                foreach (var piece in piecesATraiter)
                {
                    decimal? reste = TransitionnerStatutPieceCoeur(
                        piece, nouveauStatut, operateur, motifLivraisonNonSoldee, commande);
                    // On garde le dernier reste (toutes les pièces d'une commande ont le même)
                    if (reste.HasValue) resteLivraisonForcee = reste;
                }

                context.SaveChanges();
                transaction?.Commit();

                if (resteLivraisonForcee.HasValue)
                    AuditerLivraisonNonSoldee(operateur, nomOperateur, idCommande,
                        resteLivraisonForcee.Value, motifLivraisonNonSoldee!,
                        "Forçage du statut de toutes les pièces");

                _logger.LogInformation(
                    "ForcerStatutToutesPieces — Commande #{IdCommande} → statut « {Statut} » " +
                    "par {Operateur} (Id={IdOperateur}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                    idCommande, nouveauStatut,
                    string.IsNullOrWhiteSpace(nomOperateur) ? "INCONNU" : nomOperateur,
                    idOperateur, _clock.Now);
            }
            catch
            {
                transaction?.Rollback();
                throw;
            }
        }

        public List<PieceCommande> ObtenirPiecesCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Mesures)
                .Include(p => p.MaterielSupplements)
                .Where(p => p.IdCommande == idCommande
                         && p.TypeVetement != null && p.TypeVetement != "")
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
        /// Point d'entrée PUBLIC unique pour changer le statut d'une pièce.
        /// Appelé par toutes les vues (CommandesView, StatutView, dashboard couturier).
        /// Délègue à <see cref="TransitionnerStatutPieceCoeur"/> après chargement du contexte.
        /// Pour une rétrogradation de pièce commissionnée, passe commissionService afin
        /// d'annuler et rétrograder dans la même transaction.
        /// </summary>
        public void TransitionnerStatutPiece(int idPieceCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motif = null,
            CommissionService? commissionService = null)
        {
            using var context = _contextFactory.CreateDbContext();
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
            if (piece.Commande != null) ExigerCommandeActive(piece.Commande);

            string ancienStatut = piece.Statut;

            // InMemory ne supporte pas les vraies transactions.
            bool avecTransaction = context.Database.ProviderName != "Microsoft.EntityFrameworkCore.InMemory";
            using var transaction = avecTransaction ? context.Database.BeginTransaction() : null;
            try
            {
                decimal? resteLivraisonForcee = TransitionnerStatutPieceCoeur(
                    piece, nouveauStatut, operateur, motif, piece.Commande, commissionService);

                context.SaveChanges();
                transaction?.Commit();

                // Audit
                Auditer(operateur, nomOperateur, "STATUT_PIECE_MODIFIE", "PieceCommande",
                    idPieceCommande,
                    avant: new { Statut = ancienStatut },
                    apres: new { Statut = nouveauStatut, piece.DateTerminee },
                    motif: motif);

                if (resteLivraisonForcee.HasValue && piece.Commande != null)
                    AuditerLivraisonNonSoldee(operateur, nomOperateur, piece.IdCommande,
                        resteLivraisonForcee.Value, motif!,
                        $"Changement statut pièce #{idPieceCommande} ({piece.TypeVetement})");

                CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
                {
                    IdCommande = piece.IdCommande,
                    TypeChangement = nouveauStatut == "Terminee" ? "PieceTerminee" : "StatutModifie",
                    Details = $"Pièce #{idPieceCommande} : {ancienStatut} → {nouveauStatut}"
                });

                _logger.LogInformation(
                    "TransitionnerStatutPiece — Pièce #{IdPiece} ({Type}) : « {Ancien} » → « {Nouveau} » " +
                    "par {Operateur} (Id={IdOp}) le {Date:dd/MM/yyyy HH:mm:ss}.",
                    idPieceCommande, piece.TypeVetement, ancienStatut, nouveauStatut,
                    nomOperateur, idOperateur, _clock.Now);
            }
            catch
            {
                transaction?.Rollback();
                throw;
            }
        }

        /// <summary>
        /// Change le statut d'une seule pièce sans toucher aux mesures ni au prix.
        /// Délègue à <see cref="TransitionnerStatutPiece"/> — conservé pour rétrocompatibilité.
        /// </summary>
        public void ChangerStatutPiece(int idPieceCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null)
            => TransitionnerStatutPiece(idPieceCommande, nouveauStatut, idOperateur, nomOperateur,
                                        motifLivraisonNonSoldee);

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
