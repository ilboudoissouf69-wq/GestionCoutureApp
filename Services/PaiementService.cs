using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    public class PaiementService : IPaiementService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<PaiementService> _logger;
        private readonly IClock _clock;
        private readonly IEventAggregator? _eventAggregator;
        private readonly IAuditService? _auditService;

        // Verrou statique pour éviter les numéros de reçu en doublon
        private static readonly object _verrou = new();

        // Liste fermée des modes de paiement acceptés (Phase 1.1g)
        private static readonly HashSet<string> ModesPaiementValides = new(StringComparer.OrdinalIgnoreCase)
        {
            "Espèces", "Especes", "Orange Money", "Moov Money", "Coris Money",
            "Mobile Money", "Virement", "Chèque", "Cheque", "Autre"
        };

        public PaiementService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<PaiementService> logger,
            IClock? clock = null,
            IEventAggregator? eventAggregator = null,
            IAuditService? auditService = null)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _clock = clock ?? new SystemClock();
            _eventAggregator = eventAggregator;
            _auditService = auditService;
        }

        // ----------------------------------------------------------------
        // Lecture
        // ----------------------------------------------------------------

        public List<Paiement> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Paiements
                .Include(p => p.Commande)
                .ThenInclude(c => c!.Client)
                .OrderByDescending(p => p.DatePaiement)
                .ToList();
        }

        // Récupère les paiements avec pagination
        public async Task<PagedResult<Paiement>> ObtenirPageAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Paiements
                .Include(p => p.Commande)
                .ThenInclude(c => c!.Client)
                .OrderByDescending(p => p.DatePaiement);
            
            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Paiement>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }
        
        // Version légère pour affichage tableau
        public async Task<PagedResult<Paiement>> ObtenirPageLightAsync(int page, int pageSize)
        {
            using var context = _contextFactory.CreateDbContext();
            
            var query = context.Paiements
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Client)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces)
                .OrderByDescending(p => p.DatePaiement);
            
            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
            
            return new PagedResult<Paiement>
            {
                Items = items,
                TotalCount = totalCount,
                Page = page,
                PageSize = pageSize
            };
        }

        // ✅ SURCHARGE ADDITIVE : recherche + filtre côté base, tri DatePaiement desc conservé
        public async Task<PagedResult<Paiement>> ObtenirPageLightAsync(
            int page, int pageSize, string? recherche, StatutFiltrePaiement filtre)
            => await ObtenirPageLightAsync(page, pageSize, recherche, filtre, null, null);

        /// <summary>Surcharge complète avec filtres date (période de paiement).</summary>
        public async Task<PagedResult<Paiement>> ObtenirPageLightAsync(
            int page, int pageSize,
            string? recherche,
            StatutFiltrePaiement filtre,
            DateTime? dateDebut,
            DateTime? dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            var query = context.Paiements
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Client)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces)
                .AsQueryable();

            // Filtre statut
            query = filtre switch
            {
                StatutFiltrePaiement.Valides  => query.Where(p => !p.EstAnnule),
                StatutFiltrePaiement.Annules  => query.Where(p => p.EstAnnule),
                _                             => query
            };

            // Recherche texte
            if (!string.IsNullOrWhiteSpace(recherche))
            {
                string r = recherche.Trim();
                query = query.Where(p =>
                    p.RecuNumero.Contains(r) ||
                    p.NomOperateur.Contains(r) ||
                    (p.Commande != null && p.Commande.Client != null && (
                        p.Commande.Client.Nom.Contains(r) ||
                        p.Commande.Client.Prenom.Contains(r))));
            }

            // Filtre période sur DatePaiement — bornes sans .Date pour utiliser l'index
            if (dateDebut.HasValue)
                query = query.Where(p => p.DatePaiement >= dateDebut.Value);
            if (dateFin.HasValue)
                query = query.Where(p => p.DatePaiement < dateFin.Value.Date.AddDays(1));

            query = query.OrderByDescending(p => p.DatePaiement);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Paiement>
            {
                Items      = items,
                TotalCount = totalCount,
                Page       = page,
                PageSize   = pageSize
            };
        }

        public List<Paiement> ObtenirParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Paiements
                .Where(p => p.IdCommande == idCommande)
                .OrderBy(p => p.DatePaiement)
                .ToList();
        }

        // ----------------------------------------------------------------
        // Calculs financiers — decimal pour précision exacte
        // ----------------------------------------------------------------

        public decimal TotalPayeParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Paiements
                .Where(p => p.IdCommande == idCommande)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
        }

        public decimal TotalValideParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return TotalValideParCommande(context, idCommande);
        }

        private static decimal TotalValideParCommande(ApplicationDbContext context, int idCommande)
        {
            // AsEnumerable() : SQLite ne supporte pas Sum() sur decimal côté SQL
            return context.Paiements
                .Where(p => p.IdCommande == idCommande && !p.EstAnnule)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);
        }

        // ----------------------------------------------------------------
        // Enregistrement d'un paiement
        // ----------------------------------------------------------------

        public void Ajouter(Paiement paiement, int idOperateur, string nomOperateur)
        {
            // Tout paiement doit être associé à un opérateur identifié pour la traçabilité financière.
            if (idOperateur <= 0)
                throw new InvalidOperationException(
                    "L'identifiant de l'opérateur est obligatoire pour la traçabilité financière.");

            // Phase 1.1g : valider le mode de paiement contre la liste fermée
            if (!string.IsNullOrWhiteSpace(paiement.ModePaiement) &&
                !ModesPaiementValides.Contains(paiement.ModePaiement))
                throw new InvalidOperationException(
                    $"Mode de paiement invalide : '{paiement.ModePaiement}'. " +
                    "Valeurs acceptées : Espèces, Orange Money, Moov Money, Coris Money, Virement, Chèque, Autre.");

            // Phase 1.1g : arrondir à l'entier (FCFA sans centimes)
            paiement.MontantPaye = Math.Round(paiement.MontantPaye, 0, MidpointRounding.AwayFromZero);

            // Phase 1 : vérifier existence, statut Actif et rôle Boss/Secrétaire.
            // Un Couturier ne peut pas enregistrer un paiement.
            // Phase 1.1c : RequireRoleByIdEnum charge et renvoie l'opérateur pour relire son nom en base.
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(
                _contextFactory, idOperateur,
                RoleEmploye.Boss, RoleEmploye.Secretaire);

            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();

                // Phase 1.1c : relire le nom réel de l'opérateur depuis la base (ne pas faire confiance au paramètre UI)
                var operateurDb = context.Employes.Find(idOperateur)
                    ?? throw new InvalidOperationException("Opérateur introuvable en base.");
                string nomOperateurRelu = operateurDb.Prenom + " " + operateurDb.Nom;

                decimal totalValide = TotalValideParCommande(context, paiement.IdCommande);

                // Commande.MontantTotal est déprécié — le montant réel est la somme
                // des PieceCommande.MontantCouture. On utilise une requête explicite
                // avec Include car Find() ne charge pas les navigations.
                //
                // MontantTotalCommande sur le reçu = couture seule (base de calcul des commissions).
                // resteReel = couture + matériaux − paiements déjà effectués (ce que le CLIENT doit).
                var commande = context.Commandes
                    .Include(c => c.Pieces)
                    .Include(c => c.MaterielSupplements)
                    .FirstOrDefault(c => c.IdCommande == paiement.IdCommande)
                    ?? throw new InvalidOperationException("Commande introuvable.");

                // C7 : refuser tout encaissement sur une commande annulée ou supprimée
                if (commande.EstSupprimee || commande.EstAnnulee)
                    throw new InvalidOperationException(
                        commande.EstAnnulee
                            ? "Impossible d'encaisser un paiement : cette commande a été annulée."
                            : "Impossible d'encaisser un paiement : cette commande a été supprimée.");

                if (commande.Pieces == null || commande.Pieces.Count == 0)
                {
                    throw new InvalidOperationException(
                        "Impossible d'encaisser un paiement : cette commande n'a aucune pièce. " +
                        "Ajoutez d'abord au moins une pièce à la commande.");
                }

                decimal montantCouture = commande.Pieces.Sum(p => p.MontantCouture);
                decimal montantMateriaux = commande.MaterielSupplements?.Sum(m => m.Quantite * m.PrixUnitaire) ?? 0m;
                decimal montantTotalFacture = montantCouture + montantMateriaux;
                decimal resteReel = montantTotalFacture - totalValide;

                if (paiement.MontantPaye <= 0)
                    throw new InvalidOperationException("Le montant doit être positif.");

                // tolérance de 1 centime pour les arrondis d'affichage
                if (paiement.MontantPaye > resteReel + 0.01m)
                    throw new InvalidOperationException(
                        $"Montant ({paiement.MontantPaye:N0}) dépasse le reste réel ({resteReel:N0} FCFA).");

                // MontantTotalCommande snapshot = montant total facturé au client (couture + matériaux).
                // C'est ce montant que le reçu affiche en "TOTAL FACTURE".
                // Note : la commission couturier se calcule sur montantCouture seul — mais ce calcul
                // est fait dans CommissionService, pas ici. On stocke le vrai total facturé.
                paiement.MontantTotalCommande = montantTotalFacture;
                paiement.ResteAvantPaiement   = resteReel;
                paiement.IdOperateur = idOperateur;
                // Phase 1.1c : utiliser le nom relu en base, pas le paramètre UI
                paiement.NomOperateur = nomOperateurRelu;
                paiement.DatePaiement = _clock.Now;
                paiement.RecuNumero = GenererNumeroRecu(context, _clock);
                paiement.EstAnnule = false;

                context.Paiements.Add(paiement);

                // En cas de collision sur le numéro de reçu (race condition), régénère et réessaye.
                try
                {
                    context.SaveChanges();
                }
                catch (DbUpdateException ex) when (
                    ex.InnerException?.Message?.Contains("UNIQUE constraint") == true ||
                    ex.InnerException?.Message?.Contains("IX_Paiements_RecuNumero") == true)
                {
                    // Collision détectée : régénérer un nouveau numéro et réessayer
                    paiement.RecuNumero = GenererNumeroRecu(context, _clock);
                    context.SaveChanges();
                    
                    _logger.LogWarning(
                        "Collision numéro reçu détectée — régénéré en {Nouveau}",
                        paiement.RecuNumero);
                }

                _logger.LogInformation(
                    "Paiement {Recu} enregistré — commande {IdCommande} — {Montant:N0} FCFA — opérateur {Op}",
                    paiement.RecuNumero, paiement.IdCommande, paiement.MontantPaye, nomOperateurRelu);

                // Phase 1.1b : audit de la création du paiement
                _ = _auditService?.EnregistrerActionAsync(
                    idOperateur, nomOperateurRelu, operateurDb.Role,
                    "PAIEMENT_CREE", "Paiement", paiement.IdPaiement,
                    valeursApres: new
                    {
                        paiement.RecuNumero,
                        paiement.IdCommande,
                        paiement.MontantPaye,
                        paiement.ModePaiement,
                        paiement.DatePaiement
                    });

                _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Paiement });
            }
        }

        // ----------------------------------------------------------------
        // Annulation (jamais de suppression)
        // ----------------------------------------------------------------

        public void Annuler(int idPaiement, string motif, int idAnnulateur, string nomAnnulateur)
        {
            using var context = _contextFactory.CreateDbContext();

            // Seul le Boss peut annuler un paiement. RequireRoleByIdEnum est plus robuste
            // qu'une recherche par nom (résistant aux homonymes et aux renommages).
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(_contextFactory, idAnnulateur, RoleEmploye.Boss);

            // Phase 1.1c : relire le nom réel de l'annulateur depuis la base
            var annulateurDb = context.Employes.Find(idAnnulateur)
                ?? throw new InvalidOperationException("Annulateur introuvable en base.");
            string nomAnnulateurRelu = annulateurDb.Prenom + " " + annulateurDb.Nom;

            var paiement = context.Paiements.Find(idPaiement)
                ?? throw new InvalidOperationException("Paiement introuvable.");

            if (paiement.EstAnnule)
                throw new InvalidOperationException("Ce paiement est déjà annulé.");

            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            // Phase 2 : bloquer si la commande a des pièces rattachées à une commission non annulée.
            // Annuler un paiement alors que les commissions sont déjà calculées fausserait les
            // comptes des couturiers. Il faut d'abord annuler la commission.
            var commissionsActives = context.PiecesCommande
                .Where(p => p.IdCommande == paiement.IdCommande && p.IdCommission != null)
                .Select(p => new { p.IdCommission })
                .Distinct()
                .ToList()
                .Select(x => x.IdCommission!.Value)
                .Distinct()
                .ToList();

            if (commissionsActives.Any())
            {
                var commissions = context.Commissions
                    .Where(c => commissionsActives.Contains(c.IdCommission) && !c.EstAnnulee)
                    .Select(c => new { c.IdCommission, c.NomEmployeSnapshot })
                    .ToList();

                if (commissions.Any())
                {
                    string liste = string.Join(", ",
                        commissions.Select(c => $"Commission #{c.IdCommission} ({c.NomEmployeSnapshot})"));
                    throw new InvalidOperationException(
                        $"Impossible d'annuler ce paiement : la commande a des pièces rattachées " +
                        $"à des commissions non annulées ({liste}). " +
                        "Annulez d'abord les commissions concernées, puis réessayez.");
                }
            }

            paiement.EstAnnule = true;
            paiement.MotifsAnnulation = motif.Trim();
            paiement.DateAnnulation = _clock.Now;
            // Phase 1.1c : utiliser le nom relu en base
            paiement.NomAnnulateur = nomAnnulateurRelu;

            context.SaveChanges();

            _logger.LogWarning(
                "Paiement {Recu} ANNULÉ par {Annulateur} (ID {IdAnnulateur}) — motif : {Motif}",
                paiement.RecuNumero, nomAnnulateurRelu, idAnnulateur, motif);

            // Phase 1.1b : audit de l'annulation du paiement
            _ = _auditService?.EnregistrerActionAsync(
                idAnnulateur, nomAnnulateurRelu, annulateurDb.Role,
                "PAIEMENT_ANNULE", "Paiement", idPaiement,
                valeursAvant: new
                {
                    paiement.RecuNumero,
                    paiement.MontantPaye,
                    paiement.ModePaiement,
                    paiement.IdCommande
                },
                motif: motif.Trim(),
                envoyerNotification: true);

            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Paiement });
        }

        // ----------------------------------------------------------------
        // Génération du numéro de reçu — protégée contre les doublons
        // ----------------------------------------------------------------

        public string GenererNumeroRecu()
        {
            using var context = _contextFactory.CreateDbContext();
            return GenererNumeroRecu(context, _clock);
        }

        /// <summary>
        /// Génère un numéro de reçu unique au format REC-yyyyMMdd-NNNN.
        /// Utilise IClock (pas DateTime.Now) et MAX du suffixe (pas COUNT + .Date)
        /// pour que les index soient utilisables. Boucle bornée (max 20 tentatives)
        /// en cas de collision concurrente.
        /// </summary>
        private static string GenererNumeroRecu(ApplicationDbContext context, IClock clock)
        {
            DateTime maintenant = clock.Now;
            string dateStr = maintenant.ToString("yyyyMMdd");
            string prefixe = $"REC-{dateStr}-";

            // MAX sur le suffixe : évite COUNT()+.Date qui force un full-scan
            // (EF Core ne peut pas translater .Date en prédicat sur SQLite sans index).
            // On filtre par le préfixe texte exact → l'index IX_Paiements_RecuNumero sert.
            string? dernierNumero = context.Paiements
                .Where(p => p.RecuNumero.StartsWith(prefixe))
                .OrderByDescending(p => p.RecuNumero)
                .Select(p => p.RecuNumero)
                .FirstOrDefault();

            int prochain = 1;
            if (dernierNumero != null)
            {
                // Extraire le suffixe numérique (ex. "REC-20261023-0007" → 7)
                string suffixe = dernierNumero[prefixe.Length..];
                if (int.TryParse(suffixe, out int n))
                    prochain = n + 1;
            }

            const int maxTentatives = 20;
            string numero = $"{prefixe}{prochain:D4}";
            int tentative = 0;
            while (context.Paiements.Any(p => p.RecuNumero == numero))
            {
                tentative++;
                if (tentative >= maxTentatives)
                    throw new InvalidOperationException(
                        $"Impossible de générer un numéro de reçu unique après {maxTentatives} tentatives.");
                prochain++;
                numero = $"{prefixe}{prochain:D4}";
            }
            return numero;
        }
    }
}
