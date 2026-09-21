using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using GestionCoutureApp.Models;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Services
{
    public class WhatsAppService : IWhatsAppService
    {
        private const string IndicatifParDefaut  = "226";
        private const string NomAtelierParDefaut = "Retoupe Choco";

        // ==================================================================
        // Normalisation numéro
        // ==================================================================
        public string NormaliserNumero(string numeroLocal)
        {
            if (string.IsNullOrWhiteSpace(numeroLocal))
                throw new InvalidOperationException(
                    "Ce client n'a pas de numéro de téléphone enregistré.");

            string nettoye = Regex.Replace(numeroLocal.Trim(), @"[^\d+]", "");

            if (nettoye.StartsWith("+"))
                return nettoye[1..];

            if (nettoye.StartsWith("00"))
                return nettoye[2..];

            if (nettoye.StartsWith(IndicatifParDefaut))
                return nettoye;

            return IndicatifParDefaut + nettoye;
        }

        // ==================================================================
        // Ouverture WhatsApp
        // ==================================================================
        public void OuvrirConversation(string numeroLocal, string message)
        {
            string numero = NormaliserNumero(numeroLocal);
            string url = $"https://wa.me/{numero}?text={Uri.EscapeDataString(message)}";
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }

        // ==================================================================
        // Raccourcis métier (versions async)
        // ==================================================================
        public async Task NotifierCommandePreteAsync(Commande commande)
        {
            string tel = commande.Client?.Telephone
                ?? throw new InvalidOperationException("Numéro de téléphone introuvable.");
            string message = await MessageCommandePreteAsync(commande);
            OuvrirConversation(tel, message);
        }

        public async Task NotifierRappelRdvAsync(Commande commande)
        {
            string tel = commande.Client?.Telephone
                ?? throw new InvalidOperationException("Numéro de téléphone introuvable.");
            string message = await MessageRappelRdvAsync(commande);
            OuvrirConversation(tel, message);
        }

        public async Task ContacterClientAsync(Client client)
        {
            if (string.IsNullOrWhiteSpace(client.Telephone))
                throw new InvalidOperationException("Ce client n'a pas de numéro de téléphone.");
            string message = await MessageContactGeneralAsync(client);
            OuvrirConversation(client.Telephone, message);
        }

        // ==================================================================
        // Génération des messages (versions async pour éviter deadlock)
        // ==================================================================

        /// <summary>
        /// Message 1 — Commande entièrement prête.
        /// Exemple :
        ///   Bonjour M./Mme *Bailou Joseph*,
        ///   Bonne nouvelle ! Votre commande *#12* est prête. ✂️🎉
        ///   📦 Détails :
        ///   - Pantalon (Couturier : Moussa)
        ///   - Chemise (Couturier : Ibrahim)
        ///   💰 Reste à payer : *2 500 FCFA*
        ///   Nous vous attendons. Merci pour votre confiance !
        ///   — Retoupe Choco
        /// </summary>
        public async Task<string> MessageCommandePreteAsync(Commande commande)
        {
            // ✅ CORRECTIF AUDIT #13 : Remplacement de .Result par await pour éviter deadlock
            string modele;
            try
            {
                var param = App.Services.GetRequiredService<IParametresService>();
                modele = await param.ObtenirMsgCommandePrete();
                string nomAtelier = await param.ObtenirNomAtelier();
                modele = modele.Replace("{Atelier}", nomAtelier);
            }
            catch
            {
                modele = "Bonjour {Nom},\n\nVotre commande *#{Commande}* ({Pieces}) est prête ! 💰 Reste : *{Reste} FCFA*\n— {Atelier}";
            }

            string nomClient = $"{commande.Client?.Nom} {commande.Client?.Prenom}".Trim();
            string pieces = commande.Pieces != null && commande.Pieces.Count > 0
                ? string.Join(", ", commande.Pieces.Select(p => p.TypeVetement))
                : "vêtement";
            decimal totalPaye = commande.Paiements?
                .Where(p => !p.EstAnnule).Sum(p => p.MontantPaye) ?? 0m;
            decimal reste = commande.MontantTotalCalcule - totalPaye;

            return modele
                .Replace("{Nom}", $"*{nomClient}*")
                .Replace("{Commande}", commande.IdCommande.ToString())
                .Replace("{Pieces}", pieces)
                .Replace("{Reste}", reste.ToString("N0"));
        }

        public async Task<string> MessageRappelRdvAsync(Commande commande)
        {
            // ✅ CORRECTIF AUDIT #13 : Remplacement de .Result par await pour éviter deadlock
            string modele;
            try
            {
                var param = App.Services.GetRequiredService<IParametresService>();
                modele = await param.ObtenirMsgRappelRdv();
                string nomAtelier = await param.ObtenirNomAtelier();
                modele = modele.Replace("{Atelier}", nomAtelier);
            }
            catch
            {
                modele = "Rappel *{Atelier}* ⏰\n\nBonjour *{Nom}*, RDV le *{Date}* à *{Heure}*.\n\nMerci !";
            }

            string nomClient = $"{commande.Client?.Nom} {commande.Client?.Prenom}".Trim();
            string date = commande.DateFin.ToString("dd/MM/yyyy");
            string heure = commande.HeureFin.HasValue
                ? commande.HeureFin.Value.ToString(@"hh\:mm")
                : commande.HeureDebut.ToString(@"hh\:mm");

            return modele
                .Replace("{Nom}", $"*{nomClient}*")
                .Replace("{Date}", $"*{date}*")
                .Replace("{Heure}", $"*{heure}*");
        }

        public async Task<string> MessageContactGeneralAsync(Client client)
        {
            // ✅ CORRECTIF AUDIT #13 : Remplacement de .Result par await pour éviter deadlock
            string nomAtelier = await ObtenirNomAtelierAsync();
            return $"Bonjour *{client.Nom} {client.Prenom}*,\n\n[Votre message ici]\n\n— *{nomAtelier}*";
        }

        private async Task<string> ObtenirNomAtelierAsync()
        {
            try
            {
                var param = App.Services.GetRequiredService<IParametresService>();
                return await param.ObtenirNomAtelier();
            }
            catch { return NomAtelierParDefaut; }
        }
    }
}
