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

            // AsEnumerable() : SQLite ne supporte pas Sum() sur decimal côté SQL
            var ca = context.Paiements
                .AsNoTracking()
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
            using var t = PerfLogger.Start("TresorerieService.CalculerTotalDepenses", _logger);
            using var context = _contextFactory.CreateDbContext();

            var totalDepenses = context.Depenses
                .AsNoTracking()
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
            using var t = PerfLogger.Start("TresorerieService.CalculerTotalCommissions", _logger);
            using var context = _contextFactory.CreateDbContext();

            var commissions = context.Commissions
                .AsNoTracking()
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
            using var t = PerfLogger.Start("TresorerieService.CalculerTotalMateriaux", _logger);
            using var context = _contextFactory.CreateDbContext();

            // Récupérer toutes les commandes de la période
            var commandesPeriode = context.Commandes
                .AsNoTracking()
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

            // Toutes les commandes de la période (DateFin dans la période)
            // On inclut toutes les commandes non supprimées (pas seulement Terminee/Livree)
            // car une commande En cours peut avoir des paiements.
            var commandes = context.Commandes
                .AsNoTracking()
                .Include(c => c.Pieces)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Paiements)
                .Where(c => !c.EstSupprimee
                         && c.DateFin.Date >= dateDebut.Date
                         && c.DateFin.Date <= dateFin.Date)
                .ToList();

            decimal caEncaisseMateriaux  = 0m;
            decimal caEncaisseCouture    = 0m;
            decimal totalMateriauxFactures = 0m;
            decimal materiauxNonRembourses = 0m;

            foreach (var cmd in commandes)
            {
                decimal totalMat  = cmd.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire);
                decimal paye      = cmd.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

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
                decimal totalCmd = cmd.Pieces.Sum(p => p.MontantCouture) + totalMat;
                if (paye < totalCmd - 0.01m)
                    materiauxNonRembourses += totalMat;
            }

            // Pour les paiements hors-période (paiements dont la commande n'est pas dans la période)
            // On récupère les paiements de la période sur des commandes hors-DateFin
            var idsCommandesPeriode = commandes.Select(c => c.IdCommande).ToHashSet();
            var paiementsHorsPeriode = context.Paiements
                .AsNoTracking()
                .Where(p => !p.EstAnnule
                         && p.DatePaiement.Date >= dateDebut.Date
                         && p.DatePaiement.Date <= dateFin.Date
                         && !idsCommandesPeriode.Contains(p.IdCommande))
                .AsEnumerable()
                .ToList();

            // Ces paiements sont considérés entièrement comme "couture" (pas de matériaux associés dans la période)
            caEncaisseCouture += paiementsHorsPeriode.Sum(p => p.MontantPaye);

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
        /// FORMULE UNIQUE (identique à DepenseService.ObtenirStats) :
        ///   BilanNet = CA encaissé − commissions − primes − charges d'exploitation
        ///
        /// Les matériaux NE sont PAS déduits : ce sont des avances remboursées par
        /// le client (flux neutre). Seul CaEncaisseCouture (informatif) applique la
        /// règle "matériaux remboursés EN PREMIER".
        /// </summary>
        public BilanFinancier CalculerBilan(DateTime dateDebut, DateTime dateFin)
        {
            using var t = PerfLogger.Start("TresorerieService.CalculerBilan", _logger);
            using var context = _contextFactory.CreateDbContext();

            _logger.LogInformation(
                "Calcul bilan financier — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy}",
                dateDebut, dateFin);

            // 1. Chiffre d'affaires total (encaissements réels — couture + matériaux)
            decimal ca = CalculerChiffreAffaires(dateDebut, dateFin);

            // 2. Dépenses validées
            decimal depenses = CalculerTotalDepenses(dateDebut, dateFin);

            // 3. Commissions et primes (avec détail)
            var commissionsDetaillees = context.Commissions
                .AsNoTracking()
                .Where(c => !c.EstAnnulee 
                         && c.DateCalcul.Date >= dateDebut.Date 
                         && c.DateCalcul.Date <= dateFin.Date)
                .ToList();

            decimal totalCommissions = commissionsDetaillees.Sum(c => c.MontantCommission);
            decimal totalPrimes      = commissionsDetaillees.Sum(c => c.PrimeQualite);

            // 4. Répartition couture / matériaux via la méthode centrale
            //    Règle : matériaux remboursés EN PREMIER (min(paye, totalMat))
            var repartition = CalculerRepartitionCa(dateDebut, dateFin);

            // 5. Reste à encaisser (commandes de la période non soldées)
            var commandesPeriode = context.Commandes
                .AsNoTracking()
                .Include(c => c.Pieces)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Paiements)
                .Where(c => !c.EstSupprimee
                         && c.DateFin.Date >= dateDebut.Date 
                         && c.DateFin.Date <= dateFin.Date)
                .ToList();

            decimal resteAEncaisser = 0m;
            foreach (var cmd in commandesPeriode)
            {
                decimal totalFacture = cmd.Pieces.Sum(p => p.MontantCouture) 
                                     + cmd.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire);
                decimal encaisse = cmd.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                decimal reste = totalFacture - encaisse;
                if (reste > 0)
                    resteAEncaisser += reste;
            }

            // 6. Statistiques de comptage
            int nombrePaiements = context.Paiements
                .Count(p => !p.EstAnnule 
                         && p.DatePaiement.Date >= dateDebut.Date 
                         && p.DatePaiement.Date <= dateFin.Date);

            int nombreCommandesLivrees = commandesPeriode
                .Count(c => c.Statut == "Terminee" || c.Statut == "Livree");

            // 7. FORMULE UNIQUE du bénéfice net
            //    BilanNet = CA encaissé − commissions − primes − charges d'exploitation
            //    Les matériaux ne sont PAS déduits (flux neutre : avance remboursée)
            decimal bilanNet = ca - totalCommissions - totalPrimes - depenses;

            _logger.LogInformation(
                "Bilan calculé — CA: {CA:N0} (dont mat encaissés: {MatE:N0}, couture: {CoutE:N0}), " +
                "Dépenses: {Dep:N0}, Commissions: {Com:N0}, Primes: {Pri:N0}, BILAN NET: {Bilan:N0} FCFA",
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
                .Where(c => !c.EstSupprimee // Ignorer les commandes supprimées logiquement
                         && c.DateFin.Date >= dateDebut.Date 
                         && c.DateFin.Date <= dateFin.Date
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
                .Where(p => p.DatePaiement.Date >= dateDebut.Date 
                         && p.DatePaiement.Date <= dateFin.Date)
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
