using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de calcul de trésorerie et bilans financiers.
    /// Centralise tous les calculs financiers critiques avec une logique métier claire.
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
            using var t = PerfLogger.Start("TresorerieService.CalculerChiffreAffaires", _logger);
            using var context = _contextFactory.CreateDbContext();

            // ÉTAPE 5 PERF — SUM SQL via (double) pour decimal SQLite
            var ca = (decimal)context.Paiements
                .AsNoTracking()
                .Where(p => !p.EstAnnule
                         && p.DatePaiement >= dateDebut.Date
                         && p.DatePaiement < dateFin.Date.AddDays(1))
                .Sum(p => (double)p.MontantPaye);

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
            using var t = PerfLogger.Start("TresorerieService.CalculerTotalDepenses", _logger);
            using var context = _contextFactory.CreateDbContext();

            // ÉTAPE 5 PERF — SUM SQL via (double) pour decimal SQLite
            var totalDepenses = (decimal)context.Depenses
                .AsNoTracking()
                .Where(d => d.DateDepense >= dateDebut.Date
                         && d.DateDepense < dateFin.Date.AddDays(1)
                         && d.StatutValidation == "Validee"
                         && !d.EstAnnulee)
                .Sum(d => (double)d.Montant);

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
            using var t = PerfLogger.Start("TresorerieService.CalculerTotalCommissions", _logger);
            using var context = _contextFactory.CreateDbContext();

            // ÉTAPE 5 PERF — SUM SQL (MontantCommission + PrimeQualite) via double
            var commissions = context.Commissions
                .AsNoTracking()
                .Where(c => !c.EstAnnulee
                         && c.DateCalcul >= dateDebut.Date
                         && c.DateCalcul < dateFin.Date.AddDays(1))
                .Sum(c => (double)(c.MontantCommission + c.PrimeQualite));
            decimal total = (decimal)commissions;

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
            using var t = PerfLogger.Start("TresorerieService.CalculerTotalMateriaux", _logger);
            using var context = _contextFactory.CreateDbContext();

            return (decimal)context.MaterielsSupplements
                .AsNoTracking()
                .Where(m => m.Commande != null
                         && m.Commande.DateFin >= dateDebut.Date
                         && m.Commande.DateFin < dateFin.Date.AddDays(1)
                         && (m.Commande.Statut == "Terminee" || m.Commande.Statut == "Livree"))
                .Sum(m => (double)(m.Quantite * m.PrixUnitaire));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // MÉTHODE CENTRALE — Répartition matériaux / couture encaissés
        // ─────────────────────────────────────────────────────────────────────────
        /// <summary>
        /// Calcule, pour chaque commande ayant des matériaux, la part encaissée attribuable
        /// aux matériaux et la part attribuable à la couture, selon la règle métier :
        ///
        ///   RÈGLE : les matériaux sont remboursés EN PREMIER.
        ///   Matériaux encaissés (cmd) = min( paiementsValides, totalMatériaux )
        ///   Couture encaissée (cmd)   = max( 0, paiementsValides − totalMatériaux )
        ///   Si paiementsValides = 0  →  matériaux = 0, couture = 0
        ///
        /// Retourne :
        ///   caEncaisseMateriaux : part du CA encaissé correspondant aux matériaux (argent de passage)
        ///   caEncaisseCouture   : part du CA encaissé correspondant à la couture pure
        ///   totalMateriauxFactures : total matériaux tous clients confondus (informatif)
        ///   materiauxNonRembourses : matériaux sur commandes non soldées
        /// </summary>
        public RepartitionCaResult CalculerRepartitionCa(DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            var debut = dateDebut.Date;
            var finExclusive = dateFin.Date.AddDays(1);
            var commandes = context.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && c.DateFin >= debut && c.DateFin < finExclusive);

            // Chaque table est agrégée séparément en SQL. Cela évite le produit
            // cartésien des trois collections chargées avec Include.
            var coutureParCommande = context.PiecesCommande.AsNoTracking()
                .Where(p => commandes.Select(c => c.IdCommande).Contains(p.IdCommande))
                .GroupBy(p => p.IdCommande)
                .Select(g => new { Id = g.Key, Total = g.Sum(p => (double)p.MontantCouture) })
                .ToDictionary(x => x.Id, x => (decimal)x.Total);
            var materiauxParCommande = context.MaterielsSupplements.AsNoTracking()
                .Where(m => commandes.Select(c => c.IdCommande).Contains(m.IdCommande))
                .GroupBy(m => m.IdCommande)
                .Select(g => new { Id = g.Key, Total = g.Sum(m => (double)(m.Quantite * m.PrixUnitaire)) })
                .ToDictionary(x => x.Id, x => (decimal)x.Total);
            var paiementsParCommande = context.Paiements.AsNoTracking()
                .Where(p => !p.EstAnnule && commandes.Select(c => c.IdCommande).Contains(p.IdCommande))
                .GroupBy(p => p.IdCommande)
                .Select(g => new { Id = g.Key, Total = g.Sum(p => (double)p.MontantPaye) })
                .ToDictionary(x => x.Id, x => (decimal)x.Total);
            var commandesIds = commandes.Select(c => c.IdCommande).ToList();

            decimal caEncaisseMateriaux  = 0m;
            decimal caEncaisseCouture    = 0m;
            decimal totalMateriauxFactures = 0m;
            decimal materiauxNonRembourses = 0m;

            foreach (var idCommande in commandesIds)
            {
                decimal totalMat = materiauxParCommande.GetValueOrDefault(idCommande);
                decimal paye = paiementsParCommande.GetValueOrDefault(idCommande);

                // Matériaux facturés (tous, quelle que soit la situation de paiement)
                totalMateriauxFactures += totalMat;

                if (totalMat <= 0m)
                {
                    // Pas de matériaux : tout le paiement va à la couture
                    caEncaisseCouture += paye;
                    continue;
                }

                // Règle "matériaux EN PREMIER" : les matériaux sont remboursés avant la couture
                decimal matEncaisses = Math.Min(paye, totalMat);
                decimal coutureEncaissee = Math.Max(0m, paye - totalMat);

                caEncaisseMateriaux += matEncaisses;
                caEncaisseCouture   += coutureEncaissee;

                // Matériaux non remboursés = commande non soldée qui a des matériaux
                // (alerte de créances : le client n'a pas encore tout réglé)
                decimal totalCmd = coutureParCommande.GetValueOrDefault(idCommande) + totalMat;
                if (paye < totalCmd - 0.01m)
                    materiauxNonRembourses += totalMat;
            }

            // Pour les paiements hors-période (paiements dont la commande n'est pas dans la période)
            // On récupère les paiements de la période sur des commandes hors-DateFin
            var paiementsHorsPeriode = context.Paiements
                .AsNoTracking()
                .Where(p => !p.EstAnnule
                         && p.DatePaiement >= debut
                         && p.DatePaiement < finExclusive
                         && !context.Commandes.Any(c => c.IdCommande == p.IdCommande
                             && !c.EstSupprimee && c.DateFin >= debut && c.DateFin < finExclusive))
                .Sum(p => (double)p.MontantPaye);

            // Ces paiements sont considérés entièrement comme "couture" (pas de matériaux associés dans la période)
            caEncaisseCouture += (decimal)paiementsHorsPeriode;

            _logger.LogDebug(
                "Répartition CA — MatEncaissés: {MatE:N0}, CoutureEncaissée: {CoutE:N0}, " +
                "MatFacturés: {MatF:N0}, MatNonRemb: {MatNR:N0}",
                caEncaisseMateriaux, caEncaisseCouture, totalMateriauxFactures, materiauxNonRembourses);

            return new RepartitionCaResult
            {
                CaEncaisseMateriaux    = caEncaisseMateriaux,
                CaEncaisseCouture      = caEncaisseCouture,
                TotalMateriauxFactures = totalMateriauxFactures,
                MateriauxNonRembourses = materiauxNonRembourses
            };
        }

        /// <summary>
        /// Calcule le bilan financier complet avec tous les détails.
        ///
        /// FORMULE UNIQUE (identique à DepenseService.ObtenirStats.BeneficeNet) :
        ///   BilanNet = CaEncaisseCouture − commissions − primes − charges d'exploitation
        ///
        /// Les matériaux refacturés (argent de passage) ne gonflent PAS le bénéfice.
        /// CaEncaisseCouture est calculé par CalculerRepartitionCa (règle "matériaux EN PREMIER").
        /// </summary>
        public BilanFinancier CalculerBilan(DateTime dateDebut, DateTime dateFin)
        {
            using var t = PerfLogger.Start("TresorerieService.CalculerBilan", _logger);
            using var context = _contextFactory.CreateDbContext();

            try
            {
                return CalculerBilanSql(context, dateDebut, dateFin);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Le calcul SQL condensé du bilan a échoué; repli sur le calcul EF.");
            }

            _logger.LogInformation(
                "Calcul bilan financier — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy}",
                dateDebut, dateFin);

            // 1. Chiffre d'affaires total (encaissements réels — couture + matériaux)
            decimal ca = CalculerChiffreAffaires(dateDebut, dateFin);

            // 2. Dépenses validées
            decimal depenses = CalculerTotalDepenses(dateDebut, dateFin);

            // 3. Commissions et primes (avec détail)
            var commissionsQuery = context.Commissions.AsNoTracking()
                .Where(c => !c.EstAnnulee && c.DateCalcul >= dateDebut.Date
                         && c.DateCalcul < dateFin.Date.AddDays(1));
            decimal totalCommissions = (decimal)commissionsQuery.Sum(c => (double)c.MontantCommission);
            decimal totalPrimes = (decimal)commissionsQuery.Sum(c => (double)c.PrimeQualite);

            // 4. Répartition couture / matériaux via la méthode centrale
            //    Règle : matériaux remboursés EN PREMIER (min(paye, totalMat))
            var repartition = CalculerRepartitionCa(dateDebut, dateFin);

            // 5. Reste à encaisser (commandes de la période non soldées)
            var debut = dateDebut.Date;
            var finExclusive = dateFin.Date.AddDays(1);
            var commandesPeriode = context.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && c.DateFin >= debut && c.DateFin < finExclusive);
            var idsPeriode = commandesPeriode.Select(c => c.IdCommande);
            var coutureParCommande = context.PiecesCommande.AsNoTracking()
                .Where(p => idsPeriode.Contains(p.IdCommande)).GroupBy(p => p.IdCommande)
                .Select(g => new { Id = g.Key, Total = g.Sum(p => (double)p.MontantCouture) })
                .ToDictionary(x => x.Id, x => (decimal)x.Total);
            var materiauxParCommande = context.MaterielsSupplements.AsNoTracking()
                .Where(m => idsPeriode.Contains(m.IdCommande)).GroupBy(m => m.IdCommande)
                .Select(g => new { Id = g.Key, Total = g.Sum(m => (double)(m.Quantite * m.PrixUnitaire)) })
                .ToDictionary(x => x.Id, x => (decimal)x.Total);
            var paiementsParCommande = context.Paiements.AsNoTracking()
                .Where(p => !p.EstAnnule && idsPeriode.Contains(p.IdCommande)).GroupBy(p => p.IdCommande)
                .Select(g => new { Id = g.Key, Total = g.Sum(p => (double)p.MontantPaye) })
                .ToDictionary(x => x.Id, x => (decimal)x.Total);

            decimal resteAEncaisser = 0m;
            foreach (var idCommande in commandesPeriode.Select(c => c.IdCommande))
            {
                decimal totalFacture = coutureParCommande.GetValueOrDefault(idCommande)
                                     + materiauxParCommande.GetValueOrDefault(idCommande);
                decimal encaisse = paiementsParCommande.GetValueOrDefault(idCommande);
                decimal reste = totalFacture - encaisse;
                if (reste > 0)
                    resteAEncaisser += reste;
            }

            // 6. Statistiques de comptage
            int nombrePaiements = context.Paiements
                .Count(p => !p.EstAnnule && p.DatePaiement >= debut && p.DatePaiement < finExclusive);

            int nombreCommandesLivrees = commandesPeriode
                .Count(c => c.Statut == "Terminee" || c.Statut == "Livree");

            // 7. FORMULE UNIQUE du bénéfice net
            //    BilanNet = CaEncaisseCouture − commissions − primes − charges d'exploitation
            //    Les matériaux refacturés (argent de passage) ne gonflent PAS le bénéfice.
            decimal bilanNet = repartition.CaEncaisseCouture - totalCommissions - totalPrimes - depenses;

            _logger.LogInformation(
                "Bilan calculé — CA total: {CA:N0}, MatEncaissés: {MatE:N0}, " +
                "CaEncaisseCouture: {CoutE:N0}, Dépenses: {Dep:N0}, Commissions: {Com:N0}, " +
                "Primes: {Pri:N0}, BILAN NET: {Bilan:N0} FCFA",
                ca, repartition.CaEncaisseMateriaux, repartition.CaEncaisseCouture,
                depenses, totalCommissions, totalPrimes, bilanNet);

            return new BilanFinancier
            {
                DateDebut  = dateDebut.Date,
                DateFin    = dateFin.Date,
                ChiffreAffaires          = ca,
                // Informatifs : répartition selon règle "matériaux EN PREMIER"
                ChiffreAffairesCouture   = repartition.CaEncaisseCouture,
                ChiffreAffairesMateriaux = repartition.CaEncaisseMateriaux,
                TotalMateriauxFactures   = repartition.TotalMateriauxFactures,
                TotalDepenses      = depenses,
                TotalCommissions   = totalCommissions,
                TotalPrimesQualite = totalPrimes,
                BilanNet           = bilanNet,
                NombrePaiements          = nombrePaiements,
                NombreCommandesLivrees   = nombreCommandesLivrees,
                ResteAEncaisser          = resteAEncaisser,
                MateriauxNonRembourses   = repartition.MateriauxNonRembourses
            };
        }

        private static BilanFinancier CalculerBilanSql(
            ApplicationDbContext context, DateTime dateDebut, DateTime dateFin)
        {
            var debut = dateDebut.Date;
            var finExclusive = dateFin.Date.AddDays(1);
            static string DbDate(DateTime d) => d.ToString(
                "yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);

            context.Database.OpenConnection();
            context.Database.ExecuteSqlRaw("PRAGMA temp_store = MEMORY;");
            context.Database.ExecuteSqlRaw("PRAGMA cache_size = -32768;");
            using var command = context.Database.GetDbConnection().CreateCommand();
            command.CommandText = @"
WITH
commandes_periode AS (
  SELECT IdCommande, Statut FROM Commandes
  WHERE EstSupprimee = 0 AND DateFin >= @debut AND DateFin < @fin
),
pieces AS (
  SELECT IdCommande, SUM(CAST(MontantCouture AS REAL)) AS total
  FROM PiecesCommande GROUP BY IdCommande
),
materiaux AS (
  SELECT IdCommande, SUM(Quantite * CAST(PrixUnitaire AS REAL)) AS total
  FROM MaterielsSupplements GROUP BY IdCommande
),
paiements AS (
  SELECT IdCommande, SUM(CAST(MontantPaye AS REAL)) AS total
  FROM Paiements WHERE EstAnnule = 0 GROUP BY IdCommande
),
resume AS (
  SELECT c.IdCommande, COALESCE(pi.total, 0) AS couture,
         COALESCE(ma.total, 0) AS materiaux, COALESCE(pa.total, 0) AS paye
  FROM commandes_periode c
  LEFT JOIN pieces pi ON pi.IdCommande = c.IdCommande
  LEFT JOIN materiaux ma ON ma.IdCommande = c.IdCommande
  LEFT JOIN paiements pa ON pa.IdCommande = c.IdCommande
)
SELECT
 (SELECT COALESCE(SUM(CAST(MontantPaye AS REAL)), 0) FROM Paiements
  WHERE EstAnnule = 0 AND DatePaiement >= @debut AND DatePaiement < @fin),
 (SELECT COALESCE(SUM(CAST(Montant AS REAL)), 0) FROM Depenses
  WHERE DateDepense >= @debut AND DateDepense < @fin
    AND StatutValidation = 'Validee' AND EstAnnulee = 0),
 (SELECT COALESCE(SUM(CAST(MontantCommission AS REAL)), 0) FROM Commissions
  WHERE EstAnnulee = 0 AND DateCalcul >= @debut AND DateCalcul < @fin),
 (SELECT COALESCE(SUM(CAST(PrimeQualite AS REAL)), 0) FROM Commissions
  WHERE EstAnnulee = 0 AND DateCalcul >= @debut AND DateCalcul < @fin),
 COALESCE((SELECT SUM(MIN(paye, materiaux)) FROM resume), 0),
 COALESCE((SELECT SUM(MAX(0, paye - materiaux)) FROM resume), 0) +
 (SELECT COALESCE(SUM(CAST(p.MontantPaye AS REAL)), 0) FROM Paiements p
  WHERE p.EstAnnule = 0 AND p.DatePaiement >= @debut AND p.DatePaiement < @fin
    AND NOT EXISTS (SELECT 1 FROM commandes_periode cp WHERE cp.IdCommande = p.IdCommande)),
 COALESCE((SELECT SUM(materiaux) FROM resume), 0),
 COALESCE((SELECT SUM(CASE WHEN paye < couture + materiaux - 0.01
                            THEN materiaux ELSE 0 END) FROM resume), 0),
 COALESCE((SELECT SUM(MAX(0, couture + materiaux - paye)) FROM resume), 0),
 (SELECT COUNT(*) FROM Paiements WHERE EstAnnule = 0 AND DatePaiement >= @debut AND DatePaiement < @fin),
 (SELECT COUNT(*) FROM commandes_periode WHERE Statut IN ('Terminee', 'Livree'));";
            var pDebut = command.CreateParameter();
            pDebut.ParameterName = "@debut"; pDebut.Value = DbDate(debut); command.Parameters.Add(pDebut);
            var pFin = command.CreateParameter();
            pFin.ParameterName = "@fin"; pFin.Value = DbDate(finExclusive); command.Parameters.Add(pFin);

            using var reader = command.ExecuteReader();
            if (!reader.Read()) throw new InvalidOperationException("Le calcul SQL du bilan n'a retourné aucune ligne.");
            decimal Amount(int ordinal) => Convert.ToDecimal(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
            int Count(int ordinal) => Convert.ToInt32(reader.GetValue(ordinal), System.Globalization.CultureInfo.InvariantCulture);
            decimal ca = Amount(0), depenses = Amount(1), commissions = Amount(2), primes = Amount(3);
            decimal caMateriaux = Amount(4), caCouture = Amount(5), materiaux = Amount(6);
            decimal nonRembourses = Amount(7), reste = Amount(8);
            return new BilanFinancier
            {
                DateDebut = debut, DateFin = dateFin.Date,
                ChiffreAffaires = ca, ChiffreAffairesCouture = caCouture,
                ChiffreAffairesMateriaux = caMateriaux, TotalMateriauxFactures = materiaux,
                TotalDepenses = depenses, TotalCommissions = commissions,
                TotalPrimesQualite = primes, BilanNet = caCouture - commissions - primes - depenses,
                NombrePaiements = Count(9), NombreCommandesLivrees = Count(10),
                ResteAEncaisser = reste, MateriauxNonRembourses = nonRembourses
            };
        }

        /// <summary>
        /// Vérifie si une période contient des transactions financières.
        /// Utile pour éviter d'afficher des bilans vides.
        /// </summary>
        public bool PeriodeADesTransactions(DateTime dateDebut, DateTime dateFin)
        {
            using var t = PerfLogger.Start("TresorerieService.PeriodeADesTransactions", _logger);
            using var context = _contextFactory.CreateDbContext();

            bool aPaiements = context.Paiements
                .Any(p => !p.EstAnnule
                       && p.DatePaiement >= dateDebut.Date
                       && p.DatePaiement < dateFin.Date.AddDays(1));

            bool aDepenses = context.Depenses
                .Any(d => d.DateDepense >= dateDebut.Date
                       && d.DateDepense < dateFin.Date.AddDays(1));

            bool aCommissions = context.Commissions
                .Any(c => !c.EstAnnulee
                       && c.DateCalcul >= dateDebut.Date
                       && c.DateCalcul < dateFin.Date.AddDays(1));

            return aPaiements || aDepenses || aCommissions;
        }

        /// <summary>
        /// Compare le montant total facturé des commandes livrées/terminées avec les paiements
        /// encaissés sur une période. Signale tout écart significatif.
        /// </summary>
        public RapportCoherenceFinanciere VerifierCoherenceArgentTravail(DateTime dateDebut, DateTime dateFin)
        {
            using var t = PerfLogger.Start("TresorerieService.VerifierCoherence", _logger);
            using var context = _contextFactory.CreateDbContext();

            _logger.LogInformation(
                "Vérification cohérence argent/travail — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy}",
                dateDebut, dateFin);

            // 1. Commandes livrées/terminées dans la période (travail effectué et facturé)
            var commandesPeriode = context.Commandes
                .AsNoTracking()
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Paiements)
                .Where(c => !c.EstSupprimee
                         && c.DateFin >= dateDebut.Date
                         && c.DateFin < dateFin.Date.AddDays(1)
                         && (c.Statut == "Terminee" || c.Statut == "Livree"
                             || c.Pieces.Any(p => p.Statut == "Terminee" || p.Statut == "Livree")))
                .ToList();

            decimal totalFactureTravailEffectue = 0m;
            int nombreCommandesLivrees = 0;
            var detailsCommandes = new List<DetailCommandeCoherence>();

            foreach (var cmd in commandesPeriode)
            {
                var piecesTerminees = cmd.Pieces.Where(p => p.Statut == "Terminee" || p.Statut == "Livree").ToList();
                if (!piecesTerminees.Any()) continue;

                nombreCommandesLivrees++;
                
                decimal montantCouture = piecesTerminees.Sum(p => p.MontantCouture);
                decimal montantMateriaux = cmd.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire);
                decimal totalFacture = montantCouture + montantMateriaux;
                
                decimal paiementsValides = cmd.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                decimal paiementsAnnules = cmd.Paiements.Where(p => p.EstAnnule).Sum(p => p.MontantPaye);
                
                decimal resteAPayer = totalFacture - paiementsValides;

                totalFactureTravailEffectue += totalFacture;

                detailsCommandes.Add(new DetailCommandeCoherence
                {
                    IdCommande = cmd.IdCommande,
                    NomClient = cmd.Client != null ? $"{cmd.Client.Prenom} {cmd.Client.Nom}" : "?",
                    DateLivraison = cmd.DateFin,
                    MontantFacture = totalFacture,
                    MontantEncaisse = paiementsValides,
                    MontantAnnule = paiementsAnnules,
                    ResteAPayer = resteAPayer,
                    EstSolde = resteAPayer <= 0
                });
            }

            // 2. Paiements encaissés dans la période (argent en caisse)
            var paiementsPeriode = context.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= dateDebut.Date
                         && p.DatePaiement < dateFin.Date.AddDays(1))
                .ToList();

            decimal totalPaiementsValides = paiementsPeriode.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
            decimal totalPaiementsAnnules = paiementsPeriode.Where(p => p.EstAnnule).Sum(p => p.MontantPaye);
            int nombrePaiementsValides = paiementsPeriode.Count(p => !p.EstAnnule);
            int nombrePaiementsAnnules = paiementsPeriode.Count(p => p.EstAnnule);

            // 3. Analyse de cohérence
            decimal ecart = totalPaiementsValides - totalFactureTravailEffectue;
            decimal tauxCoherence = totalFactureTravailEffectue > 0 
                ? (totalPaiementsValides / totalFactureTravailEffectue) * 100 
                : 100m;

            // Écarts acceptables (commandes livrées pas encore soldées, ou paiements avancés)
            bool coherenceOk = Math.Abs(ecart) <= (totalFactureTravailEffectue * 0.15m); // tolérance 15%

            string diagnostic;
            if (ecart > totalFactureTravailEffectue * 0.15m)
            {
                diagnostic = $"⚠️ ALERTE : Encaissements SUPÉRIEURS au travail livré de {ecart:N0} FCFA ({(ecart/totalFactureTravailEffectue)*100:N1}%). " +
                            "Vérifier s'il y a des avances ou des paiements pour des commandes à venir.";
            }
            else if (ecart < -(totalFactureTravailEffectue * 0.15m))
            {
                diagnostic = $"⚠️ ALERTE : Encaissements INFÉRIEURS au travail livré de {Math.Abs(ecart):N0} FCFA. " +
                            $"{detailsCommandes.Count(d => !d.EstSolde)} commande(s) livrée(s) non soldée(s).";
            }
            else
            {
                diagnostic = "✅ Cohérence acceptable : les encaissements correspondent au travail livré (±15%).";
            }

            var rapport = new RapportCoherenceFinanciere
            {
                DateDebut = dateDebut.Date,
                DateFin = dateFin.Date,
                TotalFactureTravailEffectue = totalFactureTravailEffectue,
                TotalPaiementsValides = totalPaiementsValides,
                TotalPaiementsAnnules = totalPaiementsAnnules,
                Ecart = ecart,
                TauxCoherence = tauxCoherence,
                NombreCommandesLivrees = nombreCommandesLivrees,
                NombrePaiementsValides = nombrePaiementsValides,
                NombrePaiementsAnnules = nombrePaiementsAnnules,
                CommandesNonSoldees = detailsCommandes.Where(d => !d.EstSolde).ToList(),
                DetailsCommandes = detailsCommandes,
                CoherenceOk = coherenceOk,
                Diagnostic = diagnostic
            };

            if (!coherenceOk)
            {
                _logger.LogWarning(
                    "INCOHÉRENCE FINANCIÈRE détectée — Écart: {Ecart:N0} FCFA, Taux: {Taux:N1}% — {Diagnostic}",
                    ecart, tauxCoherence, diagnostic);
            }

            return rapport;
        }
    }
}
