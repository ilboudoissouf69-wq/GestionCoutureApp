using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Point 6 — Notifications WhatsApp semi-automatiques (Option A : wa.me).
    /// L'application prépare le message et ouvre WhatsApp ; la secrétaire
    /// vérifie puis appuie sur Entrée. Aucun envoi n'est jamais automatique.
    /// </summary>
    public interface IWhatsAppService
    {
        /// <summary>Normalise un numéro local au format international sans '+'.</summary>
        string NormaliserNumero(string numeroLocal);

        /// <summary>Ouvre WhatsApp avec le message pré-rempli.</summary>
        void OuvrirConversation(string numeroLocal, string message);

        // ── Messages métier ────────────────────────────────────────────────

        /// <summary>
        /// Message "Commande prête" — envoyé quand toutes les pièces sont Terminées.
        /// </summary>
        Task<string> MessageCommandePreteAsync(Commande commande);

        /// <summary>
        /// Rappel de RDV de retrait pour le client.
        /// </summary>
        Task<string> MessageRappelRdvAsync(Commande commande);

        /// <summary>
        /// Message de contact général (depuis la fiche client).
        /// </summary>
        Task<string> MessageContactGeneralAsync(Client client);

        /// <summary>
        /// Ouvre directement WhatsApp avec le message "Commande prête".
        /// </summary>
        Task NotifierCommandePreteAsync(Commande commande);

        /// <summary>
        /// Ouvre directement WhatsApp avec le rappel de RDV.
        /// </summary>
        Task NotifierRappelRdvAsync(Commande commande);

        /// <summary>
        /// Ouvre WhatsApp pour contacter le client (message général).
        /// </summary>
        Task ContacterClientAsync(Client client);
    }
}
