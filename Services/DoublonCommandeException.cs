namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Levée par <see cref="CommandeService.Ajouter"/> quand une commande quasi-identique
    /// (même opérateur + client + type de vêtement + montant) a été créée dans la
    /// fenêtre anti double-soumission (60 secondes par défaut).
    ///
    /// L'UI peut intercepter cette exception séparément d'<see cref="InvalidOperationException"/>
    /// pour afficher un message adapté (« double-clic probable ») sans bloquer les
    /// vraies nouvelles commandes identiques passé la fenêtre.
    /// </summary>
    public class DoublonCommandeException : InvalidOperationException
    {
        /// <summary>Clé de déduplication qui a déclenché l'exception.</summary>
        public string CleDoublon { get; }

        /// <summary>Temps écoulé depuis la commande précédente identique.</summary>
        public TimeSpan EcartDepuisDerniereCreation { get; }

        public DoublonCommandeException(
            string message,
            string cleDoublon,
            TimeSpan ecartDepuisDerniereCreation)
            : base(message)
        {
            CleDoublon = cleDoublon;
            EcartDepuisDerniereCreation = ecartDepuisDerniereCreation;
        }
    }
}
