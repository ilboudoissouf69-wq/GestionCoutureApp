namespace GestionCoutureApp.Services
{
    /// <summary>
    /// DTO pour l'affichage des alertes de rendez-vous (Point 5).
    /// Les alertes sont calculées à partir des commandes existantes,
    /// aucune table dédiée n'est stockée en base de données.
    /// </summary>
    public class AlerteRendezVous
    {
        public int IdCommande { get; set; }
        public string NomClient { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string TypeVetement { get; set; } = string.Empty;
        public DateTime DateRendezVous { get; set; }
        public string HeureRendezVous { get; set; } = string.Empty;
        public string Statut { get; set; } = string.Empty;
        public string TempsRestant { get; set; } = string.Empty;
        public string NomCouturier { get; set; } = string.Empty;
        public bool EstUrgent { get; set; }
        public bool ProposerContactWhatsApp { get; set; }
    }

    public interface IAlerteService
    {
        /// <summary>
        /// Retourne les commandes dont le rendez-vous (DateFin + HeureFin)
        /// est dans moins de N heures (N = paramètre "DelaiAlerteRendezVousHeures"),
        /// et qui ont au moins une pièce non livrée.
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirAlertesActuelles();

        /// <summary>
        /// Retourne toutes les commandes dont le rendez-vous est à venir
        /// (DateFin >= aujourd'hui) avec au moins une pièce non livrée,
        /// quel que soit le délai d'alerte configuré.
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirTousRendezVousAVenir();
    }
}