using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class StatsRetardDashboard
    {
        public int NbCommandesEnRetard { get; set; }
        public int NbReportsMois { get; set; }
        public int NbClientsPrevenusMois { get; set; }
    }

    public class BrouillonMessageExcuse
    {
        public string Message { get; set; } = string.Empty;
        public DateTime DateRdvActuelle { get; set; }
        public DateTime NouvelleDateProposee { get; set; }
        public string TypeCible { get; set; } = "Commande";
        public int IdCible { get; set; }
        public int IdClient { get; set; }
        public string Telephone { get; set; } = string.Empty;
        public int NbFoisPrevenu { get; set; }
    }

    public interface IRetardAlerteService
    {
        /// <summary>
        /// Pièces (ni Terminee ni Livree) et retours (ni Termine ni Rendu)
        /// dont l'échéance est dans moins de N minutes ou déjà dépassée,
        /// hors alertes déjà traitées pour ce même RDV.
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirAlertesRisqueRetardAsync(DateTime? maintenant = null);

        string GenererMessageExcuse(
            string modele,
            string client,
            string numeroCommande,
            string typeVetement,
            DateTime nouvelleDate,
            string atelier);

        Task<BrouillonMessageExcuse> PreparerMessageExcuseAsync(
            string typeCible,
            int idCible,
            DateTime dateRdvActuelle,
            DateTime? maintenant = null);

        /// <summary>Enregistre l'envoi (anti-doublon + compteur). N'enregistre pas de report.</summary>
        Task EnregistrerPreventionAsync(
            string typeCible,
            int idCible,
            DateTime dateRdvTraite,
            int idClient,
            int idOperateur,
            string nomOperateur);

        /// <summary>
        /// Report confirmé : déplace uniquement la date de RDV (+ retard_report_jours).
        /// Ne touche ni statut, ni paiement, ni commission.
        /// </summary>
        Task ReporterRendezVousAsync(
            string typeCible,
            int idCible,
            DateTime dateRdvActuelle,
            int idOperateur,
            string nomOperateur,
            DateTime? maintenant = null);

        Task<StatsRetardDashboard> ObtenirStatsDashboardAsync(DateTime? maintenant = null);

        /// <summary>True si le numéro peut être ouvert dans WhatsApp.</summary>
        bool NumeroClientValide(string? telephone);
    }
}
