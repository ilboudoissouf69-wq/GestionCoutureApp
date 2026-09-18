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
        string MessageCommandePrete(Commande commande);

        /// <summary>
        /// Rappel de RDV de retrait pour le client.
        /// </summary>
        string MessageRappelRdv(Commande commande);

        /// <summary>
        /// Message de contact général (depuis la fiche client).
        /// </summary>
        string MessageContactGeneral(Client client);

        /// <summary>
        /// Ouvre directement WhatsApp avec le message "Commande prête".
        /// </summary>
        void NotifierCommandePrete(Commande commande);

        /// <summary>
        /// Ouvre directement WhatsApp avec le rappel de RDV.
        /// </summary>
        void NotifierRappelRdv(Commande commande);

        /// <summary>
        /// Ouvre WhatsApp pour contacter le client (message général).
        /// </summary>
        void ContacterClient(Client client);
    }
}
