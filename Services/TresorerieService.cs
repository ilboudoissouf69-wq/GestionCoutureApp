using GestionCoutureApp.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de calcul de trésorerie et bilans financiers.
    /// CORRECTIF AUDIT #A1 : Centralise tous les calculs financiers critiques
    /// avec une logique métier claire et documentée.
    /// </summary>
    public class TresorerieService : ITresorerieService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<TresorerieService> _logger;

        public TresorerieService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<TresorerieService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        /// <summary>
        /// Calcule le Chiffre d'Affaires selon la méthode de comptabilité de trésorerie.
        /// RÈGLE MÉTIER : CA = encaissements réels (paiements non annulés) sur la période.
        /// Cette méthode reflète l'argent EFFECTIVEMENT entré en caisse.
        /// </summary>
        public decimal CalculerChiffreAffaires(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            // AsEnumerable() : SQLite ne supporte pas Sum() sur decimal côté SQL
            var ca = context.Paiements
                .Where(p => !p.EstAnnule 
                         && p.DatePaiement.Date >= dateDebut.Date 
                         && p.DatePaiement.Date <= dateFin.Date)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);

            _logger.LogInformation(
                "CA calculé — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy} : {CA:N0} FCFA",
                dateDebut, dateFin, ca);

            return ca;
        }

        /// <summary>
        /// Calcule le total des dépenses validées sur la période.
        /// Exclut les dépenses annulées (si un tel statut existe).
        /// </summary>
        public decimal CalculerTotalDepenses(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            var totalDepenses = context.Depenses
                .Where(d => d.DateDepense.Date >= dateDebut.Date 
                         && d.DateDepense.Date <= dateFin.Date
                         && d.StatutValidation == "Validee") // Seulement les validées
                .AsEnumerable()
                .Sum(d => d.Montant);

            _logger.LogInformation(
                "Dépenses calculées — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy} : {Total:N0} FCFA",
                dateDebut, dateFin, totalDepenses);

            return totalDepenses;
        }

        /// <summary>
        /// Calcule le total des commissions + primes qualité versées.
        /// RÈGLE MÉTIER : Les commissions sont basées sur la date de CALCUL
        /// (quand elles ont été enregistrées), pas sur la date des commandes.
        /// </summary>
        public decimal CalculerTotalCommissions(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            var commissions = context.Commissions
                .Where(c => !c.EstAnnulee 
                         && c.DateCalcul.Date >= dateDebut.Date 
                         && c.DateCalcul.Date <= dateFin.Date)
                .ToList();

            decimal total = 0m;
            foreach (var c in commissions)
            {
                total += c.MontantCommission + c.PrimeQualite;
            }

            _logger.LogInformation(
                "Commissions calculées — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy} : {Total:N0} FCFA",
                dateDebut, dateFin, total);

            return total;
        }

        /// <summary>
        /// Calcule le montant total des matériaux facturés sur la période.
        /// Utile pour distinguer CA couture (commissionné) vs CA matériaux (non commissionné).
        /// </summary>
        public decimal CalculerTotalMateriaux(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            // Récupérer toutes les commandes de la période
            var commandesPeriode = context.Commandes
                .Include(c => c.MaterielSupplements)
                .Where(c => c.DateFin.Date >= dateDebut.Date 
                         && c.DateFin.Date <= dateFin.Date
                         && (c.Statut == "Terminee" || c.Statut == "Livree"))
                .ToList();

            var totalMateriaux = commandesPeriode
                .SelectMany(c => c.MaterielSupplements)
                .Sum(m => m.Quantite * m.PrixUnitaire);

            return totalMateriaux;
        }

        /// <summary>
        /// Calcule le bilan financier complet avec tous les détails.
        /// FORMULE : Bilan = CA - Dépenses - Commissions - Primes
        /// </summary>
        public BilanFinancier CalculerBilan(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            _logger.LogInformation(
                "Calcul bilan financier — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy}",
                dateDebut, dateFin);

            // 1. Chiffre d'affaires (encaissements réels)
            decimal ca = CalculerChiffreAffaires(dateDebut, dateFin);

            // 2. Dépenses
            decimal depenses = CalculerTotalDepenses(dateDebut, dateFin);

            // 3. Commissions (avec détail primes)
            var commissionsDetaillees = context.Commissions
                .Where(c => !c.EstAnnulee 
                         && c.DateCalcul.Date >= dateDebut.Date 
                         && c.DateCalcul.Date <= dateFin.Date)
                .ToList();

            decimal totalCommissions = commissionsDetaillees.Sum(c => c.MontantCommission);
            decimal totalPrimes = commissionsDetaillees.Sum(c => c.PrimeQualite);

            // 4. Détail couture vs matériaux (pour analyse)
            var commandesPeriode = context.Commandes
                .Include(c => c.Pieces)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Paiements)
                .Where(c => c.DateFin.Date >= dateDebut.Date 
                         && c.DateFin.Date <= dateFin.Date
                         && (c.Statut == "Terminee" || c.Statut == "Livree"))
                .ToList();

            decimal caCouture = commandesPeriode
                .SelectMany(c => c.Pieces)
                .Sum(p => p.MontantCouture);

            decimal caMateriaux = commandesPeriode
                .SelectMany(c => c.MaterielSupplements)
                .Sum(m => m.Quantite * m.PrixUnitaire);

            // 5. Reste à encaisser (commandes livrées non soldées)
            decimal resteAEncaisser = 0m;
            foreach (var cmd in commandesPeriode)
            {
                decimal totalFacture = cmd.Pieces.Sum(p => p.MontantCouture) 
                                     + cmd.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire);
                decimal encaisse = cmd.Paiements
                    .Where(p => !p.EstAnnule)
                    .Sum(p => p.MontantPaye);
                decimal reste = totalFacture - encaisse;
                if (reste > 0)
                    resteAEncaisser += reste;
            }

            // 6. Statistiques
            int nombrePaiements = context.Paiements
                .Count(p => !p.EstAnnule 
                         && p.DatePaiement.Date >= dateDebut.Date 
                         && p.DatePaiement.Date <= dateFin.Date);

            int nombreCommandesLivrees = commandesPeriode.Count;

            // 7. Bilan net
            decimal bilanNet = ca - depenses - totalCommissions - totalPrimes;

            _logger.LogInformation(
                "Bilan calculé — CA: {CA:N0}, Dépenses: {Dep:N0}, Commissions: {Com:N0}, " +
                "Primes: {Pri:N0}, BILAN NET: {Bilan:N0} FCFA",
                ca, depenses, totalCommissions, totalPrimes, bilanNet);

            return new BilanFinancier
            {
                DateDebut = dateDebut.Date,
                DateFin = dateFin.Date,
                ChiffreAffaires = ca,
                ChiffreAffairesCouture = caCouture,
                ChiffreAffairesMateriaux = caMateriaux,
                TotalDepenses = depenses,
                TotalCommissions = totalCommissions,
                TotalPrimesQualite = totalPrimes,
                BilanNet = bilanNet,
                NombrePaiements = nombrePaiements,
                NombreCommandesLivrees = nombreCommandesLivrees,
                ResteAEncaisser = resteAEncaisser
            };
        }

        /// <summary>
        /// Vérifie si une période contient des transactions financières.
        /// Utile pour éviter d'afficher des bilans vides.
        /// </summary>
        public bool PeriodeADesTransactions(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            bool aPaiements = context.Paiements
                .Any(p => !p.EstAnnule 
                       && p.DatePaiement.Date >= dateDebut.Date 
                       && p.DatePaiement.Date <= dateFin.Date);

            bool aDepenses = context.Depenses
                .Any(d => d.DateDepense.Date >= dateDebut.Date 
                       && d.DateDepense.Date <= dateFin.Date);

            bool aCommissions = context.Commissions
                .Any(c => !c.EstAnnulee 
                       && c.DateCalcul.Date >= dateDebut.Date 
                       && c.DateCalcul.Date <= dateFin.Date);

            return aPaiements || aDepenses || aCommissions;
        }
    }
}
