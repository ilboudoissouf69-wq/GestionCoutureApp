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

        // Verrou statique pour éviter les numéros de reçu en doublon
        private static readonly object _verrou = new();

        public PaiementService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<PaiementService> logger,
            IClock? clock = null)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _clock = clock ?? new SystemClock();
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
                _                             => query   // Tous
            };

            // Recherche texte (côté base via EF/SQLite)
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

            query = query.OrderByDescending(p => p.DatePaiement);

            var totalCount = await query.CountAsync();
            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Paiement>
            {
                Items     = items,
                TotalCount = totalCount,
                Page      = page,
                PageSize  = pageSize
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
            if (string.IsNullOrWhiteSpace(nomOperateur))
                throw new InvalidOperationException(
                    "Le nom de l'opérateur est obligatoire pour la traçabilité financière.");

            // Phase 1 : vérifier existence, statut Actif et rôle Boss/Secrétaire.
            // Un Couturier ne peut pas enregistrer un paiement.
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(
                _contextFactory, idOperateur,
                RoleEmploye.Boss, RoleEmploye.Secretaire);

            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();

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

                // MontantTotalCommande sur le reçu = couture seule
                // (la commission sera calculée sur cette base uniquement)
                paiement.MontantTotalCommande = montantCouture;
                paiement.ResteAvantPaiement = resteReel;
                paiement.IdOperateur = idOperateur;
                paiement.NomOperateur = nomOperateur;
                paiement.DatePaiement = _clock.Now;
                paiement.RecuNumero = GenererNumeroRecu(context);
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
                    paiement.RecuNumero = GenererNumeroRecu(context);
                    context.SaveChanges();
                    
                    _logger.LogWarning(
                        "Collision numéro reçu détectée — régénéré en {Nouveau}",
                        paiement.RecuNumero);
                }

                _logger.LogInformation(
                    "Paiement {Recu} enregistré — commande {IdCommande} — {Montant:N0} FCFA — opérateur {Op}",
                    paiement.RecuNumero, paiement.IdCommande, paiement.MontantPaye, nomOperateur);
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
            paiement.NomAnnulateur = nomAnnulateur;

            context.SaveChanges();

            _logger.LogWarning(
                "Paiement {Recu} ANNULÉ par {Annulateur} (ID {IdAnnulateur}) — motif : {Motif}",
                paiement.RecuNumero, nomAnnulateur, idAnnulateur, motif);
        }

        // ----------------------------------------------------------------
        // Génération du numéro de reçu — protégée contre les doublons
        // ----------------------------------------------------------------

        public string GenererNumeroRecu()
        {
            using var context = _contextFactory.CreateDbContext();
            return GenererNumeroRecu(context);
        }

        private static string GenererNumeroRecu(ApplicationDbContext context)
        {
            string dateStr = DateTime.Now.ToString("yyyyMMdd");
            int nombreDuJour = context.Paiements
                .Count(p => p.DatePaiement.Date == DateTime.Today);

            string numero = $"REC-{dateStr}-{(nombreDuJour + 1):D4}";
            int tentative = 1;
            while (context.Paiements.Any(p => p.RecuNumero == numero))
            {
                tentative++;
                numero = $"REC-{dateStr}-{(nombreDuJour + tentative):D4}";
            }
            return numero;
        }
    }
}
