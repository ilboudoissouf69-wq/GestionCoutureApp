namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Point 6 — Message WhatsApp semi-automatique.
    /// L'application ouvre WhatsApp (application ou WhatsApp Web) avec le
    /// message déjà rédigé pour le client ; la secrétaire vérifie que la
    /// commande est réellement terminée puis clique elle-même sur "Envoyer".
    /// Aucun envoi n'est jamais automatique.
    /// </summary>
    public interface IWhatsAppService
    {
        /// <summary>
        /// Convertit un numéro local (ex. "70 12 34 56") au format
        /// international attendu par WhatsApp (ex. "22670123456", sans le +).
        /// </summary>
        string NormaliserNumero(string numeroLocal);

        /// <summary>
        /// Ouvre WhatsApp (application ou WhatsApp Web) avec le message déjà
        /// rédigé pour ce numéro. N'envoie jamais rien tout seul.
        /// </summary>
        void OuvrirConversation(string numeroLocal, string message);
    }
}