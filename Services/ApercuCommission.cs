using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Résultat d'aperçu (non enregistré) d'un calcul de commission pour un couturier.
    /// Utilise decimal pour tous les montants (précision financière exacte).
    /// </summary>
    public class ApercuCommission
    {
        public int IdEmploye { get; set; }
        public string Nom { get; set; } = string.Empty;
        public int NbCommandes { get; set; }
        public decimal CaTotal { get; set; }       // montant total des commandes concernées
        public decimal CaEncaisse { get; set; }    // montant réellement encaissé
        public decimal BaseCalcul { get; set; }    // = CaTotal ou CaEncaisse selon le mode
        public decimal Commission { get; set; }
        public List<int> IdsCommandes { get; set; } = new();

        // Propriétés d'affichage formatées pour la DataGrid
        public string CaTotalAffiche    => CaTotal.ToString("N0");
        public string CaEncaisseAffiche => CaEncaisse.ToString("N0");
        public string BaseAffichee      => BaseCalcul.ToString("N0");
        public string CommissionAffichee => Commission.ToString("N0");
    }

    public interface ICommissionService
    {
        /// <summary>
        /// Calcule un APERÇU (rien n'est enregistré) des commissions par couturier.
        /// Seules les commandes terminées/livrées et pas encore rattachées à une
        /// commission enregistrée sont prises en compte.
        /// </summary>
        List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, decimal pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre);

        /// <summary>
        /// Enregistre définitivement les commissions calculées et verrouille les
        /// commandes concernées pour qu'elles ne soient plus jamais comptées deux fois.
        /// </summary>
        void EnregistrerCommissions(
            List<ApercuCommission> apercu, DateTime dateDebut, DateTime dateFin,
            decimal pourcentage, bool surMontantEncaisse, int idOperateur, string nomOperateur);

        List<Commission> ObtenirHistorique();

        /// <summary>Annule une commission déjà enregistrée et déverrouille les commandes.</summary>
        void Annuler(int idCommission, string motif, string nomAnnulateur);
    }
}
