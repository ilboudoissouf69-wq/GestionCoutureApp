namespace GestionCoutureApp.Services
{
    public interface IParametresService
    {
        // ── Accès générique ───────────────────────────────────────────────
        Task<string?> ObtenirValeur(string cle);
        Task DefinirValeur(string cle, string valeur);

        // ── Onglet 3 — Réglages Métier & Alertes ─────────────────────────
        Task<int>     ObtenirDelaiAlerteRendezVousHeures();
        Task          DefinirDelaiAlerteRendezVousHeures(int heures);
        Task<int>     ObtenirSeuiRetardHeures();
        Task          DefinirSeuiRetardHeures(int heures);
        Task<decimal> ObtenirSalaireMensuelSecretaire();
        Task          DefinirSalaireMensuelSecretaire(decimal montant);
        Task<decimal> ObtenirTauxCommissionDefaut();
        Task          DefinirTauxCommissionDefaut(decimal taux);
        Task<decimal> ObtenirPrimeZeroDefaut();
        Task          DefinirPrimeZeroDefaut(decimal prime);

        // ── Onglet 1 — Comptabilité & Seuils ─────────────────────────────
        Task<string>  ObtenirModeCA();           // "Encaisse" | "Total"
        Task          DefinirModeCA(string mode);
        Task<decimal> ObtenirSeuilAlerteDépenses();
        Task          DefinirSeuilAlerteDépenses(decimal seuil);
        Task<bool>    ObtenirApprobationDepenses();
        Task          DefinirApprobationDepenses(bool actif);
        Task<decimal> ObtenirBudgetLoyer();
        Task          DefinirBudgetLoyer(decimal montant);

        // ── Onglet 2 — Messages & WhatsApp ───────────────────────────────
        Task<string> ObtenirMsgCommandePrete();
        Task         DefinirMsgCommandePrete(string modele);
        Task<string> ObtenirMsgRappelRdv();
        Task         DefinirMsgRappelRdv(string modele);
        Task<string> ObtenirMsgRetouchePrete();
        Task         DefinirMsgRetouchePrete(string modele);

        // ── Onglet 4 — Sauvegarde ─────────────────────────────────────────
        Task<int>  ObtenirFrequenceSyncHeures();
        Task       DefinirFrequenceSyncHeures(int heures);
        Task<bool> ObtenirCompresserPhotos();
        Task       DefinirCompresserPhotos(bool actif);

        // ── Onglet 5 — Impression & Reçus ─────────────────────────────────
        Task<string> ObtenirNomAtelier();
        Task         DefinirNomAtelier(string nom);
        Task<string> ObtenirTelAtelier();
        Task         DefinirTelAtelier(string tel);
        Task<string> ObtenirAdresseAtelier();
        Task         DefinirAdresseAtelier(string adresse);
        Task<string> ObtenirPiedRecu();
        Task         DefinirPiedRecu(string texte);
    }
}
