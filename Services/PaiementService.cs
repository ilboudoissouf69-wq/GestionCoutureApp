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

        // Verrou statique pour éviter les numéros de reçu en doublon
        private static readonly object _verrou = new();

        public PaiementService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<PaiementService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
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

        // ✅ PAGINATION : Récupère les paiements avec pagination
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
        
        // ✅ OPTIMISATION : Version légère pour affichage tableau
        public async Task<PagedResult<Paiement>> ObtenirPageLightAsync(int page, int pageSize)
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
            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();

                decimal totalValide = TotalValideParCommande(context, paiement.IdCommande);

                // ÉTAPE 1b-i : Commande.MontantTotal n'est plus jamais renseigné
                // par CommandeService — le montant réel est maintenant la somme
                // des PieceCommande.MontantCouture. Find() ne charge pas les
                // navigations (Pieces resterait vide) : on utilise donc une
                // requête explicite avec Include, comme partout ailleurs dans
                // l'application depuis le correctif équivalent sur ObtenirParId.
                // ÉTAPE 1b-i + Point 2 (Matériaux) :
                // - MontantTotalCommande sur le reçu = couture seule
                //   (base de calcul de la commission du couturier)
                // - resteReel = couture + matériaux — paiements déjà effectués
                //   (c'est le montant total que le CLIENT doit rembourser)
                var commande = context.Commandes
                    .Include(c => c.Pieces)
                    .Include(c => c.MaterielSupplements)
                    .FirstOrDefault(c => c.IdCommande == paiement.IdCommande)
                    ?? throw new InvalidOperationException("Commande introuvable.");

                // ✅ CORRECTIF : Protection contre les listes null
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
                paiement.DatePaiement = DateTime.Now;
                paiement.RecuNumero = GenererNumeroRecu(context);
                paiement.EstAnnule = false;

                context.Paiements.Add(paiement);

                // ✅ CORRECTIF AUDIT #5 : Gestion retry en cas de doublon (race condition)
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

            // ✅ CORRECTIF AUDIT #8 : Seul le Boss peut annuler un paiement
            // ✅ CORRECTIF AUDIT #9 : Utilisation de RequireRoleById au lieu de la recherche par nom
            // ✅ CORRECTIF AUDIT #12 : Utilisation de RequireRoleByIdEnum avec enum RoleEmploye
            // La recherche par nom ("Prénom Nom") est fragile et peut échouer si deux employés
            // ont le même nom ou si un Boss modifie son nom après connexion.
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(_contextFactory, idAnnulateur, RoleEmploye.Boss);

            var paiement = context.Paiements.Find(idPaiement)
                ?? throw new InvalidOperationException("Paiement introuvable.");

            if (paiement.EstAnnule)
                throw new InvalidOperationException("Ce paiement est déjà annulé.");

            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            paiement.EstAnnule = true;
            paiement.MotifsAnnulation = motif.Trim();
            paiement.DateAnnulation = DateTime.Now;
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
