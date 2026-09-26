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

        // ✅ CORRECTIF AUDIT #1 : Event pour notifier les vues des changements
        public event EventHandler<CommandeChangedEventArgs>? CommandeChanged;

        // ── Détection anti double-soumission ─────────────────────────────
        // Clé : (idOperateur, idClient, typeVetement, montant) — valeur : horodatage UTC
        // de la dernière création. Thread-safe via ConcurrentDictionary.
        private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime>
            _dernieresCreations = new();

        /// <summary>Durée pendant laquelle une commande identique est considérée
        /// comme un double-clic accidentel.</summary>
        private static readonly TimeSpan FenetreDoublon = TimeSpan.FromSeconds(60);

        public CommandeService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CommandeService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public List<Commande> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
                .Where(c => !c.EstSupprimee) // ✅ CORRECTIF : Filtrer les commandes supprimées
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.MaterielSupplements)
                .Include(c => c.MaterielSupplements)
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        // ✅ PAGINATION : Récupère les commandes avec pagination
        public async Task<PagedResult<Commande>> ObtenirPageAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Where(c => !c.EstSupprimee) // ✅ CORRECTIF : Filtrer les commandes supprimées
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
        
        // ✅ OPTIMISATION : Version légère pour affichage tableau (sans toutes les données incluses)
        public async Task<PagedResult<Commande>> ObtenirPageLightAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Where(c => !c.EstSupprimee) // ✅ CORRECTIF : Filtrer les commandes supprimées
                .Include(c => c.Client)
                .Include(c => c.Pieces) // Seulement les pièces de base, sans mesures/matériaux
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
            int idOperateur, string nomOperateur)
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

            context.Commandes.Add(commande);
            context.SaveChanges(); // génère IdCommande

            piece.IdCommande = commande.IdCommande;
            if (string.IsNullOrWhiteSpace(piece.Statut))
                piece.Statut = "A faire";
            context.PiecesCommande.Add(piece);
            context.SaveChanges(); // génère IdPieceCommande

            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = piece.IdPieceCommande;
                mesure.IdCommande      = commande.IdCommande;
                context.Mesures.Add(mesure);
            }
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

        public void Modifier(Commande commande, PieceCommande piece, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            var existante = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .FirstOrDefault(c => c.IdCommande == commande.IdCommande);

            if (existante == null) return;

            // Mise à jour de la pièce existante
            var pieceExistante = existante.Pieces.FirstOrDefault();

            if (pieceExistante != null)
            {
                // Verrouillage commission
                if (pieceExistante.IdCommission.HasValue && pieceExistante.MontantCouture != piece.MontantCouture)
                {
                    throw new InvalidOperationException(
                        "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                        "une commission déjà calculée et enregistrée. Annulez d'abord cette commission " +
                        "(avec motif) si le montant doit vraiment être corrigé.");
                }

                // CORRECTIF (audit) : BUG — cette garde ne comparait que le montant de
                // LA PREMIÈRE pièce à l'encaissé total de la commande. Si la commande a
                // déjà plusieurs pièces (AjouterPiece est utilisable dès aujourd'hui,
                // voir CommandesView.BtnAjouterPiece_Click), cette méthode Modifier() ne
                // touche que Pieces.FirstOrDefault() et pouvait donc soit bloquer à tort
                // une modification valide, soit — plus grave — laisser passer une baisse
                // qui fait descendre le TOTAL de la commande sous l'encaissé, parce que
                // les autres pièces n'étaient jamais comptées. ModifierPiece() calculait
                // déjà ça correctement (totalAutresPieces) ; on applique la même logique
                // ici pour que les deux chemins de modification soient cohérents.
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

                pieceExistante.TypeVetement = piece.TypeVetement;
                pieceExistante.IdCouturier = piece.IdCouturier;
                pieceExistante.MontantCouture = piece.MontantCouture;
                pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
                pieceExistante.CheminPhoto = piece.CheminPhoto;
                if (!string.IsNullOrWhiteSpace(piece.Statut))
                    pieceExistante.Statut = piece.Statut;
                context.Mesures.RemoveRange(pieceExistante.Mesures);
                foreach (var mesure in mesures)
                {
                    mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                    mesure.IdCommande = existante.IdCommande;
                    context.Mesures.Add(mesure);
                }
            }
            else
            {
                piece.IdCommande = existante.IdCommande;
                if (string.IsNullOrWhiteSpace(piece.Statut))
                    piece.Statut = "A faire";
                context.PiecesCommande.Add(piece);
                context.SaveChanges();

                foreach (var mesure in mesures)
                {
                    mesure.IdPieceCommande = piece.IdPieceCommande;
                    mesure.IdCommande = existante.IdCommande;
                    context.Mesures.Add(mesure);
                }
            }

            existante.DateFin = commande.DateFin;
            existante.HeureDebut = commande.HeureDebut;
            existante.HeureFin = commande.HeureFin;

            context.SaveChanges();
        }

        /// <summary>
        /// ✅ CORRECTIF AUDIT SÉCURITÉ FINANCIÈRE : Suppression LOGIQUE d'une commande
        /// (au lieu de physique) avec autorisation Boss, traçabilité complète et audit.
        /// Une commande ne doit JAMAIS être supprimée physiquement de la base si elle a
        /// un historique financier (paiements, même annulés). La suppression logique
        /// permet de garder une trace immuable tout en la masquant de l'UI normale.
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

            // ✅ CORRECTIF : Vérifier que l'opérateur est Boss (seul autorisé à supprimer)
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(_contextFactory, idOperateur, Models.RoleEmploye.Boss);

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

            // ✅ CORRECTIF : Bloquer la suppression même si les paiements sont annulés
            // (ils font partie de l'historique et prouvent qu'il y a eu une transaction)
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

            // ✅ SUPPRESSION LOGIQUE : marquer comme supprimée au lieu de supprimer physiquement
            commande.EstSupprimee = true;
            commande.MotifSuppression = motif.Trim();
            commande.DateSuppression = DateTime.Now;
            commande.IdOperateurSuppression = idOperateur;
            commande.NomOperateurSuppression = nomOperateur;

            context.SaveChanges();

            // ✅ AUDIT : Enregistrer dans le journal d'audit immuable avec notification
            if (auditService != null)
            {
                await auditService.EnregistrerActionAsync(
                    idOperateur: idOperateur,
                    nomOperateur: nomOperateur,
                    roleOperateur: "Boss", // vérifié par RequireRoleByIdEnum ci-dessus
                    typeAction: "COMMANDE_SUPPRIMEE",
                    entite: "Commande",
                    idEntite: id,
                    valeursAvant: snapshot,
                    valeursApres: null, // suppression = pas d'état "après"
                    motif: motif,
                    envoyerNotification: true // notification WhatsApp automatique pour supervision
                );
            }
        }

        // ✅ ANCIENNE MÉTHODE DÉPRÉCIÉE : gardée temporairement pour compatibilité
        // avec les appelants existants, mais lève une exception pour forcer la migration
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
        
        // ✅ PAGINATION : Cherche des commandes avec pagination
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
        
        // ✅ OPTIMISATION : Version légère de recherche
        public async Task<PagedResult<Commande>> RechercherPageLightAsync(string motCle, int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Commandes
                .Where(c => !c.EstSupprimee) // ✅ CORRECTIF : Filtrer les commandes supprimées
                .Include(c => c.Client)
                .Include(c => c.Pieces)
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
            bool roleBoss, string? motifException = null)
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

            piece.IdCommande = idCommande;
            if (string.IsNullOrWhiteSpace(piece.Statut))
                piece.Statut = "A faire";

            // CORRECTIF (audit) : conserver le motif avec la pièce, pas seulement
            // le vérifier au passage. Sans ça, rien ne prouve après coup pourquoi
            // cette pièce a été ajoutée après un encaissement — la "traçabilité"
            // promise par le cahier n'existait que dans un message de dialogue
            // qui disparaissait dès qu'on cliquait "OK".
            if (aPaiements)
                piece.MotifAjoutApresEncaissement = motifException!.Trim();

            context.PiecesCommande.Add(piece);
            context.SaveChanges();

            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = piece.IdPieceCommande;
                mesure.IdCommande = idCommande;
                context.Mesures.Add(mesure);
            }
            context.SaveChanges();

            // ✅ CORRECTIF AUDIT #1 : Notification du changement
            CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
            {
                IdCommande = idCommande,
                TypeChangement = "PieceAjoutee",
                Details = $"Pièce #{piece.IdPieceCommande} ({piece.TypeVetement}) - {piece.MontantCouture:N0} FCFA"
            });
        }

        public void ModifierPiece(PieceCommande piece, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            var pieceExistante = context.PiecesCommande
                .Include(p => p.Mesures)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .FirstOrDefault(p => p.IdPieceCommande == piece.IdPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

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

                if (totalAutresPieces + piece.MontantCouture < dejaEncaisse)
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
                pieceExistante.Statut = piece.Statut;

            // Remplacement des mesures
            context.Mesures.RemoveRange(pieceExistante.Mesures);
            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                mesure.IdCommande = pieceExistante.IdCommande;
                context.Mesures.Add(mesure);
            }

            context.SaveChanges();

            // ✅ CORRECTIF AUDIT #1 : Notification si montant modifié
            if (pieceExistante.MontantCouture != piece.MontantCouture)
            {
                CommandeChanged?.Invoke(this, new CommandeChangedEventArgs
                {
                    IdCommande = pieceExistante.IdCommande,
                    TypeChangement = "MontantModifie",
                    Details = $"Pièce #{pieceExistante.IdPieceCommande} : {pieceExistante.MontantCouture:N0} FCFA"
                });
            }
        }

        public void SupprimerPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            var piece = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Mesures)
                .Include(p => p.MaterielSupplements) // ✅ FIX FK : charger les matériaux liés
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            if (piece.IdCommission.HasValue)
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette pièce : elle est rattachée à une commission.");
            }

            var commande = piece.Commande;
            if (commande != null && commande.Paiements.Any(p => !p.EstAnnule))
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette pièce : des paiements ont été encaissés sur cette commande.");
            }

            // Vérifier qu'il reste au moins une pièce si la commande en a plusieurs
            int nbPieces = context.PiecesCommande
                .Count(p => p.IdCommande == piece.IdCommande);
            if (nbPieces <= 1)
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer la dernière pièce d'une commande. " +
                    "Supprimez la commande entière si nécessaire.");
            }

            // ✅ FIX FK : supprimer les matériaux d'abord — la relation
            // PieceCommande→MaterielSupplements est OnDelete(Restrict) (pas Cascade,
            // car MaterielSupplement a déjà une FK Commande en Cascade et EF Core
            // refuse deux chemins de cascade sur la même table). Sans ce RemoveRange,
            // SQLite lève "FOREIGN KEY constraint failed".
            if (piece.MaterielSupplements.Any())
                context.MaterielsSupplements.RemoveRange(piece.MaterielSupplements);

            context.Mesures.RemoveRange(piece.Mesures);
            context.PiecesCommande.Remove(piece);
            context.SaveChanges();

            // ✅ CORRECTIF AUDIT #1 : Notification
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

        public void ForcerStatutToutesPieces(int idCommande, string nouveauStatut)
        {
            using var context = _contextFactory.CreateDbContext();
            var pieces = context.PiecesCommande
                .Where(p => p.IdCommande == idCommande)
                .ToList();

            if (pieces.Count == 0) return;

            // Vérifier qu'aucune pièce n'est verrouillée par une commission
            // (on ne force pas le statut d'une pièce commissionnée)
            var verrouillees = pieces.Where(p => p.IdCommission.HasValue).ToList();
            if (verrouillees.Any())
            {
                throw new InvalidOperationException(
                    $"{verrouillees.Count} pièce(s) sont rattachées à une commission et ne peuvent " +
                    "pas voir leur statut modifié par un forçage en cascade.");
            }

            foreach (var piece in pieces)
            {
                piece.Statut = nouveauStatut;
            }
            context.SaveChanges();
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
                .Include(p => p.Commande)
                .Where(p => p.Commande != null
                    && p.Commande.IdClient == idClient
                    && p.TypeVetement == typeVetement);
            // Pas de filtre sur le statut : une pièce "En cours" ou "À faire"
            // a déjà des mesures utiles à réutiliser.

            if (exclureIdCommande.HasValue)
                query = query.Where(p => p.IdCommande != exclureIdCommande.Value);

            return query
                .OrderByDescending(p => p.IdPieceCommande)
                .Take(10)
                .ToList();
        }
    }
}
