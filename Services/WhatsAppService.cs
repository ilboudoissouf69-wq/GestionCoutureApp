using System.Diagnostics;
using System.Text.RegularExpressions;

namespace GestionCoutureApp.Services
{
    public class WhatsAppService : IWhatsAppService
    {
        // Indicatif du Burkina Faso. A deplacer dans Parametres si l'atelier
        // recoit un jour des commandes depuis un autre pays.
        private const string IndicatifParDefaut = "226";

        public string NormaliserNumero(string numeroLocal)
        {
            if (string.IsNullOrWhiteSpace(numeroLocal))
                throw new InvalidOperationException(
                    "Ce client n'a pas de numero de telephone enregistre.");

            // Ne garde que les chiffres et le + eventuel (retire espaces,
            // tirets, points saisis par la secretaire)
            string nettoye = Regex.Replace(numeroLocal.Trim(), @"[^\d+]", "");

            if (nettoye.StartsWith("+"))
                return nettoye.Substring(1);

            if (nettoye.StartsWith("00"))
                return nettoye.Substring(2);

            if (nettoye.StartsWith(IndicatifParDefaut))
                return nettoye;

            // Numero local (8 chiffres au Burkina Faso) : on ajoute l'indicatif
            return IndicatifParDefaut + nettoye;
        }

        public void OuvrirConversation(string numeroLocal, string message)
        {
            string numeroInternational = NormaliserNumero(numeroLocal);
            string messageEncode = Uri.EscapeDataString(message);
            string url = $"https://wa.me/{numeroInternational}?text={messageEncode}";

            // UseShellExecute=true : Windows ouvre le lien avec le
            // gestionnaire par defaut (l'appli WhatsApp si elle est
            // enregistree comme gestionnaire des liens wa.me, sinon le
            // navigateur par defaut vers WhatsApp Web).
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
    }
}