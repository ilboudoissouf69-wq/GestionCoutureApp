namespace GestionCoutureApp.Services
{
    /// <summary>
    /// DTO pour l'affichage des alertes (Point 5).
    /// Les alertes sont calculées à la volée à partir des pièces existantes,
    /// aucune table dédiée n'est stockée en base de données.
    /// </summary>
    public class AlerteRendezVous
    {
        // CORRECTIF (audit) : une alerte est maintenant rattachée à une PIÈCE,
        // pas seulement à une commande — nécessaire pour honorer
        // PieceCommande.RendezVousException (Point 5, cas d'exception) et pour
        // ne pas mélanger plusieurs pièces d'une même commande sous un seul
        // couturier/statut, comme le faisait l'ancienne version
        // (elle ne regardait que c.Pieces.FirstOrDefault()).
        public int IdCommande { get; set; }
        public int IdPieceCommande { get; set; }

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

        // CORRECTIF (audit) : NOUVEAU — distingue les deux types d'alerte du
        // Point 5. "PasEncorePriseEnCharge" et "RendezVousProche" n'ont pas la
        // même urgence ni le même message pour la secrétaire, elles ne
        // doivent pas être confondues dans une seule liste indifférenciée.
        public string TypeAlerte { get; set; } = string.Empty; // "PasEncorePriseEnCharge" | "RendezVousProche"
    }

    public interface IAlerteService
    {
        /// <summary>
        /// Toutes les alertes actives en ce moment (les deux types confondus,
        /// triées par urgence) : "rendez-vous proche" (dans les N heures
        /// réglées dans Paramètres) et "pas encore prise en charge" (mi-délai
        /// entre dépôt et rendez-vous dépassé, pièce toujours "À faire").
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirAlertesActuelles();

        /// <summary>
        /// Toutes les pièces dont le rendez-vous est à venir, avec au moins
        /// un statut différent de "Livree", quel que soit le délai configuré
        /// (utilisé par l'onglet Alertes pour la vue d'ensemble complète).
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirTousRendezVousAVenir();
    }
}
