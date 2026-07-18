using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Resultat d'apercu (non enregistre) d'un calcul de commission pour un couturier.
    /// </summary>
    public class ApercuCommission
    {
        public int IdEmploye { get; set; }
        public string Nom { get; set; } = string.Empty;
        public int NbCommandes { get; set; }
        public double CaTotal { get; set; }        // montant total des commandes concernees
        public double CaEncaisse { get; set; }      // montant reellement encaisse sur ces commandes
        public double BaseCalcul { get; set; }       // = CaTotal ou CaEncaisse, selon le mode choisi
        public double Commission { get; set; }
        public List<int> IdsCommandes { get; set; } = new();
    }

    public interface ICommissionService
    {
        /// <summary>
        /// Calcule un APERCU (rien n'est enregistre) des commissions par couturier
        /// pour la periode donnee. Seules les commandes terminees/livrees et pas
        /// encore rattachees a une commission enregistree sont prises en compte.
        /// </summary>
        List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, double pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre);

        /// <summary>
        /// Enregistre DEFINITIVEMENT les commissions calculees (une ligne Commission
        /// par couturier ayant du CA sur la periode) et verrouille les commandes
        /// concernees pour qu'elles ne soient plus jamais comptees deux fois.
        /// </summary>
        void EnregistrerCommissions(
            List<ApercuCommission> apercu, DateTime dateDebut, DateTime dateFin,
            double pourcentage, bool surMontantEncaisse, int idOperateur, string nomOperateur);

        List<Commission> ObtenirHistorique();

        /// <summary>Annule une commission deja enregistree (jamais de suppression) et
        /// deverrouille les commandes concernees pour un futur recalcul.</summary>
        void Annuler(int idCommission, string motif, string nomAnnulateur);
    }
}
