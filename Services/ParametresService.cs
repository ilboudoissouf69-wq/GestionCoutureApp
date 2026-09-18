using System.Globalization;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class ParametresService : IParametresService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        // ── Clés ──────────────────────────────────────────────────────────
        // Onglet 3 — Métier
        public const string CleDelaiAlerte        = "DelaiAlerteRendezVousHeures";
        public const string CleSeuiRetard         = "SeuiRetardHeures";
        public const string CleSalaireSecretaire  = "SalaireMensuelSecretaire";
        public const string CleTauxCommission     = "TauxCommissionDefaut";
        public const string ClePrimeZeroDefaut    = "PrimeZeroDefaut";
        // Onglet 1 — Comptabilité
        public const string CleModeCA             = "ModeCA";
        public const string CleSeuilDepenses      = "SeuilAlerteDepenses";
        public const string CleApprobationDep     = "ApprobationDepenses";
        public const string CleBudgetLoyer        = "BudgetLoyer";
        // Onglet 2 — WhatsApp
        public const string CleMsgCommandePrete   = "MsgCommandePrete";
        public const string CleMsgRappelRdv       = "MsgRappelRdv";
        public const string CleMsgRetouchePrete   = "MsgRetouchePrete";
        // Onglet 4 — Sauvegarde
        public const string CleFrequenceSync      = "FrequenceSyncHeures";
        public const string CleCompresserPhotos   = "CompresserPhotos";
        // Onglet 5 — Impression
        public const string CleNomAtelier         = "NomAtelier";
        public const string CleTelAtelier         = "TelAtelier";
        public const string CleAdresseAtelier     = "AdresseAtelier";
        public const string ClePiedRecu           = "PiedRecu";

        public ParametresService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        // ── Générique ────────────────────────────────────────────────────
        public async Task<string?> ObtenirValeur(string cle)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            return (await ctx.Parametres.FindAsync(cle))?.Valeur;
        }

        public async Task DefinirValeur(string cle, string valeur)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            var p = await ctx.Parametres.FindAsync(cle);
            if (p == null) ctx.Parametres.Add(new Parametre { Cle = cle, Valeur = valeur });
            else p.Valeur = valeur;
            await ctx.SaveChangesAsync();
        }

        // ── Helpers privés ──────────────────────────────────────────────
        private async Task<int>     GetInt(string cle, int defaut)
            => int.TryParse(await ObtenirValeur(cle), out var v) ? v : defaut;
        private async Task<decimal> GetDecimal(string cle, decimal defaut)
            => decimal.TryParse(await ObtenirValeur(cle),
                NumberStyles.Any, CultureInfo.InvariantCulture, out var v) ? v : defaut;
        private async Task<bool>    GetBool(string cle, bool defaut)
        {
            var v = await ObtenirValeur(cle);
            return v == null ? defaut : v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
        private async Task SetStr(string cle, string val) => await DefinirValeur(cle, val);
        private async Task SetInt(string cle, int val)     => await DefinirValeur(cle, val.ToString(CultureInfo.InvariantCulture));
        private async Task SetDec(string cle, decimal val) => await DefinirValeur(cle, val.ToString(CultureInfo.InvariantCulture));
        private async Task SetBool(string cle, bool val)   => await DefinirValeur(cle, val ? "1" : "0");

        // ── Onglet 3 — Métier ────────────────────────────────────────────
        public Task<int>     ObtenirDelaiAlerteRendezVousHeures()  => GetInt(CleDelaiAlerte, 3);
        public Task          DefinirDelaiAlerteRendezVousHeures(int h) => SetInt(CleDelaiAlerte, h);
        public Task<int>     ObtenirSeuiRetardHeures()             => GetInt(CleSeuiRetard, 24);
        public Task          DefinirSeuiRetardHeures(int h)        => SetInt(CleSeuiRetard, h);
        public Task<decimal> ObtenirSalaireMensuelSecretaire()     => GetDecimal(CleSalaireSecretaire, 0m);
        public Task          DefinirSalaireMensuelSecretaire(decimal m) => SetDec(CleSalaireSecretaire, m);
        public Task<decimal> ObtenirTauxCommissionDefaut()         => GetDecimal(CleTauxCommission, 40m);
        public Task          DefinirTauxCommissionDefaut(decimal t)=> SetDec(CleTauxCommission, t);
        public Task<decimal> ObtenirPrimeZeroDefaut()              => GetDecimal(ClePrimeZeroDefaut, 5000m);
        public Task          DefinirPrimeZeroDefaut(decimal p)     => SetDec(ClePrimeZeroDefaut, p);

        // ── Onglet 1 — Comptabilité ──────────────────────────────────────
        public async Task<string>  ObtenirModeCA()         => await ObtenirValeur(CleModeCA) ?? "Encaisse";
        public Task                DefinirModeCA(string m) => SetStr(CleModeCA, m);
        public Task<decimal>       ObtenirSeuilAlerteDépenses()       => GetDecimal(CleSeuilDepenses, 150000m);
        public Task                DefinirSeuilAlerteDépenses(decimal s) => SetDec(CleSeuilDepenses, s);
        public Task<bool>          ObtenirApprobationDepenses()        => GetBool(CleApprobationDep, true);
        public Task                DefinirApprobationDepenses(bool a)  => SetBool(CleApprobationDep, a);
        public Task<decimal>       ObtenirBudgetLoyer()                => GetDecimal(CleBudgetLoyer, 0m);
        public Task                DefinirBudgetLoyer(decimal m)       => SetDec(CleBudgetLoyer, m);

        // ── Onglet 2 — WhatsApp ──────────────────────────────────────────
        public async Task<string> ObtenirMsgCommandePrete()
            => await ObtenirValeur(CleMsgCommandePrete)
               ?? "Bonjour {Nom},\n\nBonne nouvelle ! Votre commande *#{Commande}* ({Pieces}) est prête chez *{Atelier}*. ✂️🎉\n\n💰 Reste à payer : *{Reste} FCFA*\n\nNous vous attendons. Merci de votre confiance !\n— *{Atelier}*";
        public Task DefinirMsgCommandePrete(string m) => SetStr(CleMsgCommandePrete, m);

        public async Task<string> ObtenirMsgRappelRdv()
            => await ObtenirValeur(CleMsgRappelRdv)
               ?? "Rappel *{Atelier}* ⏰\n\nBonjour *{Nom}*, nous vous rappelons votre rendez-vous de retrait prévu le *{Date}* à *{Heure}*.\n\nÀ bientôt !\n— *{Atelier}*";
        public Task DefinirMsgRappelRdv(string m) => SetStr(CleMsgRappelRdv, m);

        public async Task<string> ObtenirMsgRetouchePrete()
            => await ObtenirValeur(CleMsgRetouchePrete)
               ?? "Bonjour *{Nom}*, la retouche de votre vêtement est terminée et disponible à l'atelier *{Atelier}*. Merci de votre confiance !";
        public Task DefinirMsgRetouchePrete(string m) => SetStr(CleMsgRetouchePrete, m);

        // ── Onglet 4 — Sauvegarde ────────────────────────────────────────
        public Task<int>  ObtenirFrequenceSyncHeures()   => GetInt(CleFrequenceSync, 4);
        public Task       DefinirFrequenceSyncHeures(int h) => SetInt(CleFrequenceSync, h);
        public Task<bool> ObtenirCompresserPhotos()      => GetBool(CleCompresserPhotos, true);
        public Task       DefinirCompresserPhotos(bool a)=> SetBool(CleCompresserPhotos, a);

        // ── Onglet 5 — Impression ────────────────────────────────────────
        public async Task<string> ObtenirNomAtelier()
            => await ObtenirValeur(CleNomAtelier) ?? "Retouche Choco — Ilassa Design";
        public Task DefinirNomAtelier(string v) => SetStr(CleNomAtelier, v);

        public async Task<string> ObtenirTelAtelier()
            => await ObtenirValeur(CleTelAtelier) ?? "+226 70 00 00 00";
        public Task DefinirTelAtelier(string v) => SetStr(CleTelAtelier, v);

        public async Task<string> ObtenirAdresseAtelier()
            => await ObtenirValeur(CleAdresseAtelier) ?? "Ouagadougou, Secteur 15";
        public Task DefinirAdresseAtelier(string v) => SetStr(CleAdresseAtelier, v);

        public async Task<string> ObtenirPiedRecu()
            => await ObtenirValeur(ClePiedRecu)
               ?? "Les articles non retirés après 30 jours seront vendus. Merci de votre confiance !";
        public Task DefinirPiedRecu(string v) => SetStr(ClePiedRecu, v);

        // ── Apparence ────────────────────────────────────────────────────
        public const string CleCouleurAccent = "CouleurAccent";
        public async Task<string> ObtenirCouleurAccent()
            => await ObtenirValeur(CleCouleurAccent) ?? "#CC0000";
        public Task DefinirCouleurAccent(string hex) => SetStr(CleCouleurAccent, hex);

        // ── Langue ───────────────────────────────────────────────────────
        public const string CleLangue = "Langue";
        public async Task<string> ObtenirLangue()
            => await ObtenirValeur(CleLangue) ?? "fr";
        public Task DefinirLangue(string code) => SetStr(CleLangue, code);
    }
}
