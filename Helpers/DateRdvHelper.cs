namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// ÉTAPE 1 PERF — Calcul centralisé de la date de RDV effective et du statut
    /// de commande active. Une seule source de vérité, utilisée par tous les
    /// services qui créent ou modifient des pièces / commandes.
    ///
    /// Règle métier (unifiée) :
    ///   DateRdvEffective = RendezVousException ?? DateFin.Date + (HeureFin ?? 17:00)
    ///
    /// Choix documenté : HeureFin par défaut à 17:00 (fin de journée atelier).
    /// Avant l'unification, RetardAlerteService utilisait HeureFin ?? HeureDebut
    /// et AlerteService utilisait HeureFin ?? 17:00. Le 17:00 est retenu car
    /// c'est l'heure de fermeture standard de l'atelier — un RDV sans heure de
    /// fin explicite doit être considéré comme « en fin de journée », pas comme
    /// « à l'heure de dépôt » (HeureDebut).
    /// </summary>
    public static class DateRdvHelper
    {
        private static readonly TimeSpan HeureFinDefaut = new(17, 0, 0);

        /// <summary>
        /// Calcule la date de RDV effective d'une pièce.
        /// = RendezVousException ?? DateFin.Date + (HeureFin ?? 17:00)
        /// </summary>
        public static DateTime CalculerDateRdvEffective(
            DateTime? rendezVousException,
            DateTime dateFin,
            TimeSpan? heureFin)
        {
            if (rendezVousException.HasValue)
                return rendezVousException.Value;

            var heure = heureFin ?? HeureFinDefaut;
            return dateFin.Date + heure;
        }

        /// <summary>
        /// Détermine si une commande est « active » (non supprimée, non annulée).
        /// </summary>
        public static bool CalculerCommandeActive(bool estSupprimee, bool estAnnulee)
            => !estSupprimee && !estAnnulee;
    }
}
