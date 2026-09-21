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
