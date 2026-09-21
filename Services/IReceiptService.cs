namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Informations de l'atelier pour les reçus.
    /// </summary>
    public class ReceiptInfo
    {
        public string NomAtelier { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string Adresse { get; set; } = string.Empty;
        public string PiedRecu { get; set; } = string.Empty;
    }

    /// <summary>
    /// Interface pour le service de gestion des reçus.
    /// Centralise les informations de l'atelier et notifie les changements.
    /// </summary>
    public interface IReceiptService
    {
        /// <summary>
        /// Obtenir les informations actuelles de l'atelier.
        /// </summary>
        Task<ReceiptInfo> GetReceiptInfoAsync();
        
        /// <summary>
        /// Mettre à jour les informations de l'atelier et notifier les abonnés.
        /// </summary>
        Task UpdateReceiptInfoAsync(ReceiptInfo info);
        
        /// <summary>
        /// S'abonner aux changements d'informations de reçus.
        /// </summary>
        void SubscribeToReceiptChanges(Action<ReceiptInfo> handler);
        
        /// <summary>
        /// Se désabonner des changements d'informations de reçus.
        /// </summary>
        void UnsubscribeFromReceiptChanges(Action<ReceiptInfo> handler);
    }
}
