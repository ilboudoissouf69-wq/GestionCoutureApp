namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface pour les calculs de trésorerie et bilans financiers.
    /// Sépare clairement la logique métier des calculs financiers.
    /// </summary>
    public interface ITresorerieService
    {
        /// <summary>
        /// Calcule le Chiffre d'Affaires (CA) sur une période donnée.
        /// CA = Somme des encaissements réels (paiements non annulés).
        /// Méthode de comptabilité de trésorerie (cash basis).
        /// </summary>
        decimal CalculerChiffreAffaires(DateTime dateDebut, DateTime dateFin);

        /// <summary>
        /// Calcule le total des dépenses sur une période donnée.
        /// Inclut toutes les dépenses validées non annulées.
        /// </summary>
        decimal CalculerTotalDepenses(DateTime dateDebut, DateTime dateFin);

        /// <summary>
        /// Calcule le total des commissions versées sur une période donnée.
        /// Inclut les commissions non annulées + primes qualité.
        /// </summary>
        decimal CalculerTotalCommissions(DateTime dateDebut, DateTime dateFin);

        /// <summary>
        /// Calcule le Bilan Financier sur une période donnée.
        /// Bilan = CA - Dépenses - Commissions
        /// </summary>
        /// <returns>
        /// Objet BilanFinancier contenant le détail complet des calculs
        /// </returns>
        BilanFinancier CalculerBilan(DateTime dateDebut, DateTime dateFin);

        /// <summary>
        /// Calcule le montant total des matériaux facturés (hors commission).
        /// Utile pour distinguer CA couture vs CA matériaux.
        /// </summary>
        decimal CalculerTotalMateriaux(DateTime dateDebut, DateTime dateFin);

        /// <summary>
        /// Vérifie si une période a des données financières.
        /// </summary>
        bool PeriodeADesTransactions(DateTime dateDebut, DateTime dateFin);

        /// <summary>
        /// ✅ CORRECTIF AUDIT : Vérifie la cohérence entre l'argent encaissé et le travail livré.
        /// Signale les écarts significatifs qui pourraient indiquer une disparition d'argent.
        /// </summary>
        RapportCoherenceFinanciere VerifierCoherenceArgentTravail(DateTime dateDebut, DateTime dateFin);
    }

    /// <summary>
    /// Résultat détaillé du calcul de bilan financier.
    /// </summary>
    public class BilanFinancier
    {
        public DateTime DateDebut { get; set; }
        public DateTime DateFin { get; set; }

        /// <summary>
        /// Chiffre d'affaires total (encaissements couture + matériaux)
        /// </summary>
        public decimal ChiffreAffaires { get; set; }

        /// <summary>
        /// Chiffre d'affaires couture uniquement (base des commissions)
        /// </summary>
        public decimal ChiffreAffairesCouture { get; set; }

        /// <summary>
        /// Chiffre d'affaires matériaux uniquement (non commissionné)
        /// </summary>
        public decimal ChiffreAffairesMateriaux { get; set; }

        /// <summary>
        /// Total des dépenses (achats, charges, etc.)
        /// </summary>
        public decimal TotalDepenses { get; set; }

        /// <summary>
        /// Total des commissions versées aux couturiers
        /// </summary>
        public decimal TotalCommissions { get; set; }

        /// <summary>
        /// Total des primes qualité versées
        /// </summary>
        public decimal TotalPrimesQualite { get; set; }

        /// <summary>
        /// Bilan net = CA - Dépenses - Commissions - Primes
        /// </summary>
        public decimal BilanNet { get; set; }

        /// <summary>
        /// Nombre de paiements encaissés sur la période
        /// </summary>
        public int NombrePaiements { get; set; }

        /// <summary>
        /// Nombre de commandes livrées sur la période
        /// </summary>
        public int NombreCommandesLivrees { get; set; }

        /// <summary>
        /// Reste à encaisser (commandes livrées non soldées)
        /// </summary>
        public decimal ResteAEncaisser { get; set; }
    }
}

    /// <summary>
    /// ✅ CORRECTIF AUDIT : Rapport de cohérence entre argent encaissé et travail effectué
    /// </summary>
    public class RapportCoherenceFinanciere
    {
        public DateTime DateDebut { get; set; }
        public DateTime DateFin { get; set; }

        /// <summary>
        /// Montant total facturé pour les commandes livrées/terminées dans la période
        /// </summary>
        public decimal TotalFactureTravailEffectue { get; set; }

        /// <summary>
        /// Total des paiements valides (non annulés) encaissés dans la période
        /// </summary>
        public decimal TotalPaiementsValides { get; set; }

        /// <summary>
        /// Total des paiements annulés (avec motifs) dans la période
        /// </summary>
        public decimal TotalPaiementsAnnules { get; set; }

        /// <summary>
        /// Écart = Paiements valides - Factures travail effectué
        /// Écart > 0 : Plus d'argent encaissé que de travail facturé (avances, acomptes)
        /// Écart < 0 : Moins d'argent encaissé (commandes livrées non soldées)
        /// </summary>
        public decimal Ecart { get; set; }

        /// <summary>
        /// Taux de cohérence en % = (Paiements / Factures) * 100
        /// Proche de 100% = cohérence parfaite
        /// </summary>
        public decimal TauxCoherence { get; set; }

        public int NombreCommandesLivrees { get; set; }
        public int NombrePaiementsValides { get; set; }
        public int NombrePaiementsAnnules { get; set; }

        /// <summary>
        /// Liste des commandes livrées mais non soldées (reste à payer > 0)
        /// </summary>
        public List<DetailCommandeCoherence> CommandesNonSoldees { get; set; } = new();

        /// <summary>
        /// Détail de toutes les commandes de la période
        /// </summary>
        public List<DetailCommandeCoherence> DetailsCommandes { get; set; } = new();

        /// <summary>
        /// Indicateur de cohérence acceptable (écart < 15%)
        /// </summary>
        public bool CoherenceOk { get; set; }

        /// <summary>
        /// Diagnostic textuel de la situation
        /// </summary>
        public string Diagnostic { get; set; } = string.Empty;
    }

    /// <summary>
    /// Détail d'une commande pour le rapport de cohérence
    /// </summary>
    public class DetailCommandeCoherence
    {
        public int IdCommande { get; set; }
        public string NomClient { get; set; } = string.Empty;
        public DateTime DateLivraison { get; set; }
        public decimal MontantFacture { get; set; }
        public decimal MontantEncaisse { get; set; }
        public decimal MontantAnnule { get; set; }
        public decimal ResteAPayer { get; set; }
        public bool EstSolde { get; set; }

        public string ResumeLigne =>
            $"Cmd #{IdCommande} - {NomClient} - Facture: {MontantFacture:N0} - " +
            $"Encaissé: {MontantEncaisse:N0} - Reste: {ResteAPayer:N0} FCFA";
    }
