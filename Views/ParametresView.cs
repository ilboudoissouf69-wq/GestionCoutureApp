using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class ParametresView : Page
    {
        private readonly IParametresService _p;
        private readonly GoogleDriveBackupService _drive;
        private readonly IAuthService _auth;
        private bool _initialise = false;

        public ParametresView()
        {
            var auth = App.Services.GetRequiredService<IAuthService>();
            if (auth.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            _auth  = auth;
            _p     = App.Services.GetRequiredService<IParametresService>();
            _drive = App.Services.GetRequiredService<GoogleDriveBackupService>();

            InitializeComponent();

            // Aperçu reçu : mise à jour en temps réel
            TxtNomAtelier.TextChanged    += (s, e) => { if (_initialise) MettreAJourApercuRecu(); };
            TxtTelAtelier.TextChanged    += (s, e) => { if (_initialise) MettreAJourApercuRecu(); };
            TxtAdresseAtelier.TextChanged += (s, e) => { if (_initialise) MettreAJourApercuRecu(); };
            TxtPiedRecu.TextChanged      += (s, e) => { if (_initialise) MettreAJourApercuRecu(); };

            Loaded += async (s, e) =>
            {
                await ChargerTout();
                _initialise = true;
                MettreAJourPastilleDrive();
            };
        }

        // ==================================================================
        // Chargement de tous les onglets
        // ==================================================================
        private async Task ChargerTout()
        {
            // ── Onglet 1 — Comptabilité ──
            string modeCA = await _p.ObtenirModeCA();
            RbModeEncaisse.IsChecked    = modeCA != "Total";
            RbModeTotal.IsChecked       = modeCA == "Total";
            TxtSeuilDepenses.Text       = (await _p.ObtenirSeuilAlerteDépenses()).ToString("N0");
            TxtBudgetLoyer.Text         = (await _p.ObtenirBudgetLoyer()).ToString("N0");
            ChkApprobationDep.IsChecked = await _p.ObtenirApprobationDepenses();

            // ── Onglet 2 — WhatsApp ──
            TxtMsgCommandePrete.Text   = await _p.ObtenirMsgCommandePrete();
            TxtMsgRappelRdv.Text       = await _p.ObtenirMsgRappelRdv();
            TxtMsgRetouchePrete.Text   = await _p.ObtenirMsgRetouchePrete();

            // ── Onglet 3 — Métier ──
            TxtDelaiAlerte.Text        = (await _p.ObtenirDelaiAlerteRendezVousHeures()).ToString();
            TxtSeuiRetard.Text         = (await _p.ObtenirSeuiRetardHeures()).ToString();
            TxtSalaireSecretaire.Text  = (await _p.ObtenirSalaireMensuelSecretaire()).ToString("N0");
            TxtTauxCommission.Text     = (await _p.ObtenirTauxCommissionDefaut()).ToString();
            TxtPrimeZeroDefaut.Text    = (await _p.ObtenirPrimeZeroDefaut()).ToString("N0");

            // ── Onglet 4 — Sauvegarde ──
            TxtFrequenceSync.Text      = (await _p.ObtenirFrequenceSyncHeures()).ToString();
            ChkCompresserPhotos.IsChecked = await _p.ObtenirCompresserPhotos();

            // ── Onglet 5 — Impression ──
            TxtNomAtelier.Text         = await _p.ObtenirNomAtelier();
            TxtTelAtelier.Text         = await _p.ObtenirTelAtelier();
            TxtAdresseAtelier.Text     = await _p.ObtenirAdresseAtelier();
            TxtPiedRecu.Text           = await _p.ObtenirPiedRecu();
            MettreAJourApercuRecu();
        }

        // ==================================================================
        // Onglet 1 — Comptabilité
        // ==================================================================
        private async void BtnSauvegarderComptabilite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string modeCA = RbModeTotal.IsChecked == true ? "Total" : "Encaisse";
                await _p.DefinirModeCA(modeCA);

                if (!decimal.TryParse(TxtSeuilDepenses.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal seuil) || seuil < 0)
                { AfficherErreur(TxtMsgComptabilite, "Seuil invalide."); return; }
                await _p.DefinirSeuilAlerteDépenses(seuil);

                if (!decimal.TryParse(TxtBudgetLoyer.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal loyer) || loyer < 0)
                { AfficherErreur(TxtMsgComptabilite, "Budget loyer invalide."); return; }
                await _p.DefinirBudgetLoyer(loyer);

                await _p.DefinirApprobationDepenses(ChkApprobationDep.IsChecked == true);

                AfficherSucces(TxtMsgComptabilite, "✅  Comptabilité enregistrée.");
            }
            catch (Exception ex) { AfficherErreur(TxtMsgComptabilite, ex.Message); }
        }

        // ==================================================================
        // Onglet 2 — WhatsApp
        // ==================================================================
        private async void BtnSauvegarderMessages_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtMsgCommandePrete.Text))
                { AfficherErreur(TxtMsgWhatsApp, "Le message 'Commande prête' est obligatoire."); return; }

                await _p.DefinirMsgCommandePrete(TxtMsgCommandePrete.Text.Trim());
                await _p.DefinirMsgRappelRdv(TxtMsgRappelRdv.Text.Trim());
                await _p.DefinirMsgRetouchePrete(TxtMsgRetouchePrete.Text.Trim());

                AfficherSucces(TxtMsgWhatsApp, "✅  Messages WhatsApp enregistrés.");
            }
            catch (Exception ex) { AfficherErreur(TxtMsgWhatsApp, ex.Message); }
        }

        private async void BtnResetMessages_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                "Remettre tous les messages WhatsApp aux valeurs par défaut ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            // Supprimer les clés → ObtenirMsg...() retournera les valeurs par défaut
            await _p.DefinirMsgCommandePrete("");
            await _p.DefinirMsgRappelRdv("");
            await _p.DefinirMsgRetouchePrete("");

            TxtMsgCommandePrete.Text  = await _p.ObtenirMsgCommandePrete();
            TxtMsgRappelRdv.Text      = await _p.ObtenirMsgRappelRdv();
            TxtMsgRetouchePrete.Text  = await _p.ObtenirMsgRetouchePrete();

            AfficherSucces(TxtMsgWhatsApp, "✅  Messages remis par défaut.");
        }

        // ==================================================================
        // Onglet 3 — Métier
        // ==================================================================
        private async void BtnSauvegarderMetier_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(TxtDelaiAlerte.Text, out int delai) || delai <= 0)
                { AfficherErreur(TxtMsgMetier, "Délai d'alerte invalide."); return; }
                await _p.DefinirDelaiAlerteRendezVousHeures(delai);

                if (!int.TryParse(TxtSeuiRetard.Text, out int retard) || retard <= 0)
                { AfficherErreur(TxtMsgMetier, "Seuil retard invalide."); return; }
                await _p.DefinirSeuiRetardHeures(retard);

                if (!decimal.TryParse(TxtSalaireSecretaire.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal salaire) || salaire < 0)
                { AfficherErreur(TxtMsgMetier, "Salaire invalide."); return; }
                await _p.DefinirSalaireMensuelSecretaire(salaire);

                if (!decimal.TryParse(TxtTauxCommission.Text, out decimal taux) || taux <= 0)
                { AfficherErreur(TxtMsgMetier, "Taux commission invalide."); return; }
                await _p.DefinirTauxCommissionDefaut(taux);

                if (!decimal.TryParse(TxtPrimeZeroDefaut.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal prime) || prime < 0)
                { AfficherErreur(TxtMsgMetier, "Prime invalide."); return; }
                await _p.DefinirPrimeZeroDefaut(prime);

                AfficherSucces(TxtMsgMetier, "✅  Réglages métier enregistrés.");
            }
            catch (Exception ex) { AfficherErreur(TxtMsgMetier, ex.Message); }
        }

        // ==================================================================
        // Onglet 4 — Sauvegarde
        // ==================================================================
        private async void BtnSauvegarderSauvegarde_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(TxtFrequenceSync.Text, out int freq) || freq < 0)
                { AfficherErreur(TxtMsgDrive, "Fréquence invalide."); return; }
                await _p.DefinirFrequenceSyncHeures(freq);
                await _p.DefinirCompresserPhotos(ChkCompresserPhotos.IsChecked == true);
                AfficherSucces(TxtMsgDrive, "✅  Paramètres sauvegarde enregistrés.");
            }
            catch (Exception ex) { AfficherErreur(TxtMsgDrive, ex.Message); }
        }

        private async void BtnSauvegarderDrive_Click(object sender, RoutedEventArgs e)
        {
            BtnSauvegarderDrive.IsEnabled = false;
            PanelProgression.Visibility   = Visibility.Visible;
            TxtMsgDrive.Text = "";

            var progression = new Progress<string>(msg => TxtProgression.Text = msg);
            bool ok = await _drive.SauvegarderAsync(progression);

            PanelProgression.Visibility   = Visibility.Collapsed;
            BtnSauvegarderDrive.IsEnabled = true;

            if (ok) AfficherSucces(TxtMsgDrive, "✅  Sauvegarde Google Drive réussie !");
            else    AfficherErreur(TxtMsgDrive, "❌  " + _drive.StatutDerniereSync);

            MettreAJourPastilleDrive();
        }

        private void MettreAJourPastilleDrive()
        {
            bool ok = _drive.DerniereSyncReussie;
            TxtStatutDrive.Text   = ok ? "🟢  Synchronisé" : "⚪  Non synchronisé";
            PastilleStatut.Background = ok
                ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                : new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            TxtDernierSync.Text = _drive.DateDerniereSync.HasValue
                ? $"Dernière sauvegarde : {_drive.DateDerniereSync.Value:dd/MM/yyyy à HH:mm}"
                : "Dernière sauvegarde : jamais";
        }

        // ==================================================================
        // Onglet 5 — Impression
        // ==================================================================
        private void MettreAJourApercuRecu()
        {
            PreviewNom.Text     = TxtNomAtelier.Text;
            PreviewTel.Text     = TxtTelAtelier.Text;
            PreviewAdresse.Text = TxtAdresseAtelier.Text;
            PreviewPied.Text    = TxtPiedRecu.Text;
        }

        private async void BtnSauvegarderImpression_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtNomAtelier.Text))
                { AfficherErreur(TxtMsgImpression, "Le nom de l'atelier est obligatoire."); return; }

                await _p.DefinirNomAtelier(TxtNomAtelier.Text.Trim());
                await _p.DefinirTelAtelier(TxtTelAtelier.Text.Trim());
                await _p.DefinirAdresseAtelier(TxtAdresseAtelier.Text.Trim());
                await _p.DefinirPiedRecu(TxtPiedRecu.Text.Trim());

                AfficherSucces(TxtMsgImpression, "✅  Informations impression enregistrées.");
            }
            catch (Exception ex) { AfficherErreur(TxtMsgImpression, ex.Message); }
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private static void AfficherSucces(TextBlock tb, string msg)
        {
            tb.Text = msg;
            tb.Foreground = System.Windows.Media.Brushes.Green;
        }

        private static void AfficherErreur(TextBlock tb, string msg)
        {
            tb.Text = msg;
            tb.Foreground = System.Windows.Media.Brushes.Red;
        }
    }
}
