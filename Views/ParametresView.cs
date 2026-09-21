using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Diagnostics;
using GestionCoutureApp.Services;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class ParametresView : Page
    {
        private readonly IParametresService _p;
        private readonly GoogleDriveBackupService _drive;
        private readonly IAuthService _auth;
        private readonly ILogService _log;
        private readonly ILanguageService _languageService;
        private readonly IThemeService _themeService;
        private readonly IReceiptService _receiptService;
        private readonly IEventAggregator _eventAggregator;
        private bool _initialise = false;
        private string _langueActive = "fr";
        private string _panneauActif = "Comptabilite";

        // ── Dictionnaire bilingue ─────────────────────────────────────────
        private static readonly Dictionary<string, Dictionary<string, string>> _t = new()
        {
            ["fr"] = new()
            {
                // Sidebar
                ["titre_page"]        = "Paramètres",
                ["sous_titre"]        = "Configuration système",
                ["groupe_gestion"]    = "GESTION",
                ["groupe_systeme"]    = "SYSTÈME",
                ["groupe_perso"]      = "PERSONNALISATION",
                ["nav_comptabilite"]  = "Comptabilité",
                ["nav_metier"]        = "Réglages Métier",
                ["nav_messages"]      = "Messages WhatsApp",
                ["nav_sauvegarde"]    = "Sauvegarde Cloud",
                ["nav_impression"]    = "Impression & Reçus",
                ["nav_apparence"]     = "Apparence",
                ["nav_langue"]        = "Langue",
                ["nav_maintenance"]   = "Maintenance",
                // Comptabilité
                ["page_compta_titre"] = "Comptabilité & Seuils",
                ["page_compta_desc"]  = "Configurez le mode de calcul du CA et les seuils d'alerte.",
                ["lbl_modeCA"]        = "Mode de calcul du Chiffre d'Affaires",
                ["desc_modeCA"]       = "Définit la base utilisée dans les tableaux de bord.",
                ["rb_encaisse"]       = "✅  Basé sur les encaissements réels (recommandé)",
                ["desc_encaisse"]     = "Acomptes + solde effectivement perçus.",
                ["rb_total"]          = "Basé sur la valeur totale des commandes engagées",
                ["desc_total"]        = "Inclut les montants non encore encaissés.",
                ["lbl_seuils"]        = "Seuils & Budgets",
                ["desc_seuils"]       = "Plafonds d'alerte et valeurs pré-remplies.",
                ["lbl_seuil_dep"]     = "Seuil d'alerte dépenses (FCFA/mois)",
                ["desc_seuil_dep"]    = "Alerte si les dépenses dépassent ce plafond.",
                ["lbl_budget_loyer"]  = "Budget fixe Loyer / Charges (FCFA/mois)",
                ["desc_budget_loyer"] = "Pré-remplit les dépenses récurrentes.",
                ["lbl_appro"]         = "Workflow d'approbation",
                ["desc_appro"]        = "Contrôle sur les dépenses de la secrétaire.",
                ["chk_appro"]         = "Exiger la validation du Boss pour les dépenses de la secrétaire",
                ["desc_chk_appro"]    = "Les dépenses restent 'En attente' jusqu'à approbation.",
                ["btn_save"]          = "💾  Enregistrer les modifications",
                // Métier
                ["page_metier_titre"] = "Réglages Métier & Alertes",
                ["page_metier_desc"]  = "Paramètres d'exploitation quotidienne de l'atelier.",
                ["lbl_alertes"]       = "Alertes & Délais",
                ["desc_alertes"]      = "Délais déclenchant les notifications automatiques.",
                ["lbl_delai"]         = "Rappel 'RDV proche' (heures avant)",
                ["lbl_retard"]        = "Seuil alerte retard (heures après livraison)",
                ["lbl_remun"]         = "Rémunération",
                ["desc_remun"]        = "Paramètres salariaux et commissions par défaut.",
                ["lbl_salaire"]       = "Salaire secrétaire (FCFA/mois)",
                ["lbl_taux"]          = "Taux commission couturier (%)",
                ["lbl_prime"]         = "Prime Zéro Défaut (FCFA)",
                ["btn_save_metier"]   = "💾  Enregistrer les réglages",
                // Messages
                ["page_msg_titre"]    = "Messages & WhatsApp",
                ["page_msg_desc"]     = "Personnalisez les messages envoyés automatiquement aux clients.",
                ["titre_balises"]     = "📌  Balises dynamiques disponibles",
                ["titre_msg1"]        = "Commande Prête",
                ["titre_msg2"]        = "Rappel de Rendez-vous",
                ["titre_msg3"]        = "Retouche / Reprise Prête",
                ["btn_reset"]         = "↺  Réinitialiser par défaut",
                ["btn_save_msg"]      = "💾  Enregistrer les messages",
                // Sauvegarde
                ["page_sav_titre"]    = "Sauvegarde & Google Drive",
                ["page_sav_desc"]     = "Synchronisation cloud et protection des données.",
                ["lbl_freq"]          = "Fréquence de synchronisation",
                ["desc_freq"]         = "0 = sauvegarde uniquement à la fermeture.",
                ["lbl_freq_val"]      = "Toutes les X heures",
                ["lbl_photos"]        = "Optimisation des photos",
                ["desc_photos"]       = "Réduit la taille du backup Google Drive.",
                ["chk_photos"]        = "Compresser les photos avant sauvegarde",
                ["desc_chk_photos"]   = "1024×768 · JPEG 70% · ~70 Ko/photo",
                ["btn_sync"]          = "☁️  Synchroniser maintenant",
                ["btn_save_sav"]      = "💾  Enregistrer les paramètres",
                ["info_drive"]        = "ℹ️ La première synchronisation ouvrira votre navigateur pour autoriser l'accès à Google Drive.",
                // Impression
                ["page_imp_titre"]    = "Impression & Reçus",
                ["page_imp_desc"]     = "Personnalisez l'en-tête et le pied des reçus thermiques 80mm.",
                ["lbl_entete"]        = "En-tête du reçu thermique (80 mm)",
                ["lbl_nom"]           = "Nom de l'atelier *",
                ["lbl_tel"]           = "Téléphones de contact",
                ["lbl_adresse"]       = "Adresse / Localisation",
                ["lbl_pied"]          = "Pied de reçu (avertissement légal)",
                ["apercu"]            = "Aperçu du reçu",
                ["btn_save_imp"]      = "💾  Enregistrer",
                // Apparence
                ["page_app_titre"]    = "Apparence",
                ["page_app_desc"]     = "Personnalisez la couleur d'accentuation de l'interface.",
                ["lbl_couleur"]       = "Couleur d'accentuation",
                ["desc_couleur"]      = "S'applique instantanément sur toute l'application.",
                ["lbl_palette"]       = "Palettes prédéfinies",
                ["lbl_custom_hex"]    = "Couleur personnalisée (code hexadécimal)",
                ["exemple_hex"]       = "Exemple : #CC0000  #1D4ED8  #0F766E  #7C3AED",
                ["btn_appliquer"]     = "Appliquer",
                // Langue
                ["page_lng_titre"]    = "Langue",
                ["page_lng_desc"]     = "Choisissez la langue d'affichage de l'interface.",
                ["lbl_langue"]        = "Langue de l'interface",
                ["desc_langue"]       = "Appliqué immédiatement sur tout le logiciel.",
                ["badge_fr"]          = "🇫🇷 Français",
                // Maintenance
                ["page_maint_titre"]  = "Maintenance & Support",
                ["page_maint_desc"]   = "Diagnostic technique et journaux système.",
                ["lbl_infos_sys"]     = "Informations Système",
                ["desc_infos_sys"]    = "Détails techniques de l'application et de la base de données.",
                ["lbl_version"]       = "Version du logiciel :",
                ["lbl_etat_bdd"]      = "État de la Base de Données :",
                ["txt_etat_bdd"]      = "Connectée (Local)",
                ["lbl_chemin_bdd"]    = "Emplacement de la BDD :",
                ["lbl_diagnostic"]    = "Diagnostic & Support Technique",
                ["desc_diagnostic"]   = "En cas d'anomalie ou de besoin d'assistance technique, accédez aux journaux d'erreurs du système.",
                ["btn_ouvrir_logs"]   = "📂  Ouvrir le dossier des Logs",
            },
            ["en"] = new()
            {
                ["titre_page"]        = "Settings",
                ["sous_titre"]        = "System configuration",
                ["groupe_gestion"]    = "MANAGEMENT",
                ["groupe_systeme"]    = "SYSTEM",
                ["groupe_perso"]      = "CUSTOMIZATION",
                ["nav_comptabilite"]  = "Accounting",
                ["nav_metier"]        = "Business Settings",
                ["nav_messages"]      = "WhatsApp Messages",
                ["nav_sauvegarde"]    = "Cloud Backup",
                ["nav_impression"]    = "Printing & Receipts",
                ["nav_apparence"]     = "Appearance",
                ["nav_langue"]        = "Language",
                ["page_compta_titre"] = "Accounting & Thresholds",
                ["page_compta_desc"]  = "Configure the revenue calculation mode and alert thresholds.",
                ["lbl_modeCA"]        = "Revenue Calculation Mode",
                ["desc_modeCA"]       = "Defines the basis used in dashboards and reports.",
                ["rb_encaisse"]       = "✅  Based on actual collections (recommended)",
                ["desc_encaisse"]     = "Deposits + amounts effectively received by the shop.",
                ["rb_total"]          = "Based on total value of engaged orders",
                ["desc_total"]        = "Includes amounts not yet collected.",
                ["lbl_seuils"]        = "Thresholds & Budgets",
                ["desc_seuils"]       = "Alert ceilings and pre-filled values.",
                ["lbl_seuil_dep"]     = "Monthly expense alert threshold (FCFA)",
                ["desc_seuil_dep"]    = "Alert if expenses exceed this ceiling.",
                ["lbl_budget_loyer"]  = "Fixed rent / recurring charges (FCFA/month)",
                ["desc_budget_loyer"] = "Pre-fills recurring expense entries.",
                ["lbl_appro"]         = "Approval Workflow",
                ["desc_appro"]        = "Control over expenses entered by the secretary.",
                ["chk_appro"]         = "Require Boss approval for secretary-entered expenses",
                ["desc_chk_appro"]    = "Expenses remain 'Pending' until your approval.",
                ["btn_save"]          = "💾  Save Changes",
                ["page_metier_titre"] = "Business Settings & Alerts",
                ["page_metier_desc"]  = "Daily workshop operational settings.",
                ["lbl_alertes"]       = "Alerts & Delays",
                ["desc_alertes"]      = "Delays triggering automatic notifications.",
                ["lbl_delai"]         = "Appointment reminder (hours before)",
                ["lbl_retard"]        = "Late alert threshold (hours after delivery)",
                ["lbl_remun"]         = "Remuneration",
                ["desc_remun"]        = "Salary and default commission settings.",
                ["lbl_salaire"]       = "Secretary monthly salary (FCFA)",
                ["lbl_taux"]          = "Default tailor commission rate (%)",
                ["lbl_prime"]         = "Zero Defect Bonus (FCFA)",
                ["btn_save_metier"]   = "💾  Save Settings",
                ["page_msg_titre"]    = "Messages & WhatsApp",
                ["page_msg_desc"]     = "Customize messages automatically sent to clients.",
                ["titre_balises"]     = "📌  Available dynamic tags",
                ["titre_msg1"]        = "Order Ready",
                ["titre_msg2"]        = "Appointment Reminder",
                ["titre_msg3"]        = "Alteration / Repair Ready",
                ["btn_reset"]         = "↺  Reset to defaults",
                ["btn_save_msg"]      = "💾  Save Messages",
                ["page_sav_titre"]    = "Backup & Google Drive",
                ["page_sav_desc"]     = "Cloud sync and data protection.",
                ["lbl_freq"]          = "Synchronization frequency",
                ["desc_freq"]         = "0 = backup only on application close.",
                ["lbl_freq_val"]      = "Every X hours",
                ["lbl_photos"]        = "Photo optimization",
                ["desc_photos"]       = "Reduces Google Drive backup size.",
                ["chk_photos"]        = "Compress photos before backup",
                ["desc_chk_photos"]   = "1024×768 · JPEG 70% · ~70 KB/photo",
                ["btn_sync"]          = "☁️  Sync Now",
                ["btn_save_sav"]      = "💾  Save Settings",
                ["info_drive"]        = "ℹ️ The first sync will open your browser to authorize Google Drive access.",
                ["page_imp_titre"]    = "Printing & Receipts",
                ["page_imp_desc"]     = "Customize the header and footer of 80mm thermal receipts.",
                ["lbl_entete"]        = "Thermal receipt header (80 mm)",
                ["lbl_nom"]           = "Workshop name *",
                ["lbl_tel"]           = "Contact phone numbers",
                ["lbl_adresse"]       = "Address / Location",
                ["lbl_pied"]          = "Receipt footer (legal notice)",
                ["apercu"]            = "Receipt preview",
                ["btn_save_imp"]      = "💾  Save",
                ["page_app_titre"]    = "Appearance",
                ["page_app_desc"]     = "Customize the application accent color.",
                ["lbl_couleur"]       = "Accent color",
                ["desc_couleur"]      = "Applied instantly throughout the application.",
                ["lbl_palette"]       = "Predefined palettes",
                ["lbl_custom_hex"]    = "Custom color (hexadecimal code)",
                ["exemple_hex"]       = "Examples: #CC0000  #1D4ED8  #0F766E  #7C3AED",
                ["btn_appliquer"]     = "Apply",
                ["page_lng_titre"]    = "Language",
                ["page_lng_desc"]     = "Choose the display language for the interface.",
                ["lbl_langue"]        = "Interface language",
                ["desc_langue"]       = "Applied immediately throughout the software.",
                ["badge_fr"]          = "🇬🇧 English",
                // Maintenance
                ["page_maint_titre"]  = "Maintenance & Support",
                ["page_maint_desc"]   = "Technical diagnostics and system logs.",
                ["lbl_infos_sys"]     = "System Information",
                ["desc_infos_sys"]    = "Technical details about the application and database.",
                ["lbl_version"]       = "Software version:",
                ["lbl_etat_bdd"]      = "Database Status:",
                ["txt_etat_bdd"]      = "Connected (Local)",
                ["lbl_chemin_bdd"]    = "Database location:",
                ["lbl_diagnostic"]    = "Diagnostics & Technical Support",
                ["desc_diagnostic"]   = "In case of anomaly or need for technical assistance, access system error logs.",
                ["btn_ouvrir_logs"]   = "📂  Open Logs Folder",
            }
        };

        private string T(string key)
        {
            if (_t.TryGetValue(_langueActive, out var dict) && dict.TryGetValue(key, out var val))
                return val;
            return key;
        }

        public ParametresView()
        {
            var auth = App.Services.GetRequiredService<IAuthService>();
            if (auth.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            _auth  = auth;
            _p     = App.Services.GetRequiredService<IParametresService>();
            _drive = App.Services.GetRequiredService<GoogleDriveBackupService>();
            _log   = App.Services.GetRequiredService<ILogService>();
            
            // ✅ CORRECTIF AUDIT #14-17 : Injection des nouveaux services
            _languageService = App.Services.GetRequiredService<ILanguageService>();
            _themeService = App.Services.GetRequiredService<IThemeService>();
            _receiptService = App.Services.GetRequiredService<IReceiptService>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();

            InitializeComponent();

            // Aperçu reçu en temps réel
            TxtNomAtelier.TextChanged     += (s, e) => { if (_initialise) MajApercuRecu(); };
            TxtTelAtelier.TextChanged     += (s, e) => { if (_initialise) MajApercuRecu(); };
            TxtAdresseAtelier.TextChanged += (s, e) => { if (_initialise) MajApercuRecu(); };
            TxtPiedRecu.TextChanged       += (s, e) => { if (_initialise) MajApercuRecu(); };

            // Prévisualisation couleur hex
            TxtCouleurHex.TextChanged += (s, e) =>
            {
                if (!_initialise) return;
                try
                {
                    var c = (Color)ColorConverter.ConvertFromString(TxtCouleurHex.Text.Trim());
                    PreviewCouleur.Background = new SolidColorBrush(c);
                }
                catch { }
            };

            Loaded += async (s, e) =>
            {
                await ChargerTout();
                _initialise = true;
                MajApercuRecu();
                MajPastilleDrive();
            };
        }

        // ==================================================================
        // Chargement
        // ==================================================================
        private async Task ChargerTout()
        {
            // Langue (en premier pour traduire le reste)
            _langueActive = await _p.ObtenirLangue();
            AppliquerTraduction();

            // Comptabilité
            string modeCA = await _p.ObtenirModeCA();
            RbModeEncaisse.IsChecked    = modeCA != "Total";
            RbModeTotal.IsChecked       = modeCA == "Total";
            TxtSeuilDepenses.Text       = (await _p.ObtenirSeuilAlerteDépenses()).ToString("N0");
            TxtBudgetLoyer.Text         = (await _p.ObtenirBudgetLoyer()).ToString("N0");
            ChkApprobationDep.IsChecked = await _p.ObtenirApprobationDepenses();

            // Métier
            TxtDelaiAlerte.Text        = (await _p.ObtenirDelaiAlerteRendezVousHeures()).ToString();
            TxtSeuiRetard.Text         = (await _p.ObtenirSeuiRetardHeures()).ToString();
            TxtSalaireSecretaire.Text  = (await _p.ObtenirSalaireMensuelSecretaire()).ToString("N0");
            TxtTauxCommission.Text     = (await _p.ObtenirTauxCommissionDefaut()).ToString();
            TxtPrimeZeroDefaut.Text    = (await _p.ObtenirPrimeZeroDefaut()).ToString("N0");

            // Messages
            TxtMsgCommandePrete.Text  = await _p.ObtenirMsgCommandePrete();
            TxtMsgRappelRdv.Text      = await _p.ObtenirMsgRappelRdv();
            TxtMsgRetouchePrete.Text  = await _p.ObtenirMsgRetouchePrete();

            // Sauvegarde
            TxtFrequenceSync.Text         = (await _p.ObtenirFrequenceSyncHeures()).ToString();
            ChkCompresserPhotos.IsChecked = await _p.ObtenirCompresserPhotos();

            // Impression
            TxtNomAtelier.Text     = await _p.ObtenirNomAtelier();
            TxtTelAtelier.Text     = await _p.ObtenirTelAtelier();
            TxtAdresseAtelier.Text = await _p.ObtenirAdresseAtelier();
            TxtPiedRecu.Text       = await _p.ObtenirPiedRecu();

            // Apparence
            string couleur = await _p.ObtenirCouleurAccent();
            TxtCouleurHex.Text = couleur;
            try
            {
                var c = (Color)ColorConverter.ConvertFromString(couleur);
                PreviewCouleur.Background = new SolidColorBrush(c);
            }
            catch { }

            // Langue UI
            MajCarteLangue();

            // Maintenance
            ChargerInfosMaintenance();
        }

        // ==================================================================
        // Navigation latérale
        // ==================================================================
        private void BtnNav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not Button btn) return;
            string tag = btn.Tag?.ToString() ?? "";
            AfficherPanneau(tag);
        }

        private void AfficherPanneau(string panneau)
        {
            _panneauActif = panneau;

            // Masquer tous les panneaux
            PanelComptabilite.Visibility = Visibility.Collapsed;
            PanelMetier.Visibility       = Visibility.Collapsed;
            PanelMessages.Visibility     = Visibility.Collapsed;
            PanelSauvegarde.Visibility   = Visibility.Collapsed;
            PanelImpression.Visibility   = Visibility.Collapsed;
            PanelApparence.Visibility    = Visibility.Collapsed;
            PanelLangue.Visibility       = Visibility.Collapsed;
            PanelMaintenance.Visibility  = Visibility.Collapsed;

            // Reset styles boutons nav
            BtnNavComptabilite.Style = FindResource("NavTopBtn") as Style;
            BtnNavMetier.Style       = FindResource("NavTopBtn") as Style;
            BtnNavMessages.Style     = FindResource("NavTopBtn") as Style;
            BtnNavSauvegarde.Style   = FindResource("NavTopBtn") as Style;
            BtnNavImpression.Style   = FindResource("NavTopBtn") as Style;
            BtnNavApparence.Style    = FindResource("NavTopBtn") as Style;
            BtnNavLangue.Style       = FindResource("NavTopBtn") as Style;
            BtnNavMaintenance.Style  = FindResource("NavTopBtn") as Style;

            switch (panneau)
            {
                case "Comptabilite":
                    PanelComptabilite.Visibility = Visibility.Visible;
                    BtnNavComptabilite.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_compta_titre");
                    TxtPageDesc.Text  = T("page_compta_desc");
                    break;
                case "Metier":
                    PanelMetier.Visibility = Visibility.Visible;
                    BtnNavMetier.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_metier_titre");
                    TxtPageDesc.Text  = T("page_metier_desc");
                    break;
                case "Messages":
                    PanelMessages.Visibility = Visibility.Visible;
                    BtnNavMessages.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_msg_titre");
                    TxtPageDesc.Text  = T("page_msg_desc");
                    break;
                case "Sauvegarde":
                    PanelSauvegarde.Visibility = Visibility.Visible;
                    BtnNavSauvegarde.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_sav_titre");
                    TxtPageDesc.Text  = T("page_sav_desc");
                    MajPastilleDrive();
                    break;
                case "Impression":
                    PanelImpression.Visibility = Visibility.Visible;
                    BtnNavImpression.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_imp_titre");
                    TxtPageDesc.Text  = T("page_imp_desc");
                    break;
                case "Apparence":
                    PanelApparence.Visibility = Visibility.Visible;
                    BtnNavApparence.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_app_titre");
                    TxtPageDesc.Text  = T("page_app_desc");
                    break;
                case "Langue":
                    PanelLangue.Visibility = Visibility.Visible;
                    BtnNavLangue.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_lng_titre");
                    TxtPageDesc.Text  = T("page_lng_desc");
                    break;
                case "Maintenance":
                    PanelMaintenance.Visibility = Visibility.Visible;
                    BtnNavMaintenance.Style = FindResource("NavTopBtnActif") as Style;
                    TxtPageTitre.Text = T("page_maint_titre");
                    TxtPageDesc.Text  = T("page_maint_desc");
                    break;
            }
        }

        // ==================================================================
        // Traduction
        // ==================================================================
        private void AppliquerTraduction()
        {
            // Badge langue + en-tête
            TxtBadgeFR.Text = T("badge_fr");

            // Boutons nav
            TxtNavComptabilite.Text = T("nav_comptabilite");
            TxtNavMetier.Text       = T("nav_metier");
            TxtNavMessages.Text     = T("nav_messages");
            TxtNavSauvegarde.Text   = T("nav_sauvegarde");
            TxtNavImpression.Text   = T("nav_impression");
            TxtNavApparence.Text    = T("nav_apparence");
            TxtNavLangue.Text       = T("nav_langue");
            TxtNavMaintenance.Text  = T("nav_maintenance");

            // Comptabilité
            TxtLblModeCA.Text        = T("lbl_modeCA");
            TxtDescModeCA.Text       = T("desc_modeCA");
            TxtRbEncaisse.Text       = T("rb_encaisse");
            TxtRbEncaisseDesc.Text   = T("desc_encaisse");
            TxtRbTotal.Text          = T("rb_total");
            TxtRbTotalDesc.Text      = T("desc_total");
            TxtLblSeuils.Text        = T("lbl_seuils");
            TxtDescSeuils.Text       = T("desc_seuils");
            TxtLblSeuilDep.Text      = T("lbl_seuil_dep");
            TxtDescSeuilDep.Text     = T("desc_seuil_dep");
            TxtLblBudgetLoyer.Text   = T("lbl_budget_loyer");
            TxtDescBudgetLoyer.Text  = T("desc_budget_loyer");
            TxtLblAppro.Text         = T("lbl_appro");
            TxtDescAppro.Text        = T("desc_appro");
            TxtChkAppro.Text         = T("chk_appro");
            TxtDescChkAppro.Text     = T("desc_chk_appro");
            BtnSauvegarderComptabilite.Content = T("btn_save");

            // Métier
            TxtLblAlertes.Text       = T("lbl_alertes");
            TxtDescAlertes.Text      = T("desc_alertes");
            TxtLblDelai.Text         = T("lbl_delai");
            TxtLblRetard.Text        = T("lbl_retard");
            TxtLblRemun.Text         = T("lbl_remun");
            TxtDescRemun.Text        = T("desc_remun");
            TxtLblSalaire.Text       = T("lbl_salaire");
            TxtLblTaux.Text          = T("lbl_taux");
            TxtLblPrime.Text         = T("lbl_prime");
            BtnSauvegarderMetier.Content = T("btn_save_metier");

            // Messages
            TxtTitreBalises.Text     = T("titre_balises");
            TxtTitreMsg1.Text        = T("titre_msg1");
            TxtTitreMsg2.Text        = T("titre_msg2");
            TxtTitreMsg3.Text        = T("titre_msg3");
            BtnResetMessages.Content = T("btn_reset");
            BtnSauvegarderMessages.Content = T("btn_save_msg");

            // Sauvegarde
            TxtLblFrequence.Text     = T("lbl_freq");
            TxtDescFrequence.Text    = T("desc_freq");
            TxtLblFreqVal.Text       = T("lbl_freq_val");
            TxtLblPhotos.Text        = T("lbl_photos");
            TxtDescPhotos.Text       = T("desc_photos");
            TxtChkPhotos.Text        = T("chk_photos");
            TxtDescChkPhotos.Text    = T("desc_chk_photos");
            TxtBtnSync.Text          = T("btn_sync").Replace("☁️  ", "");
            BtnSauvegarderSauvegarde.Content = T("btn_save_sav");
            TxtInfoDrive.Text        = T("info_drive");

            // Impression
            TxtLblEntete.Text        = T("lbl_entete");
            TxtLblNomAtelier.Text    = T("lbl_nom");
            TxtLblTel.Text           = T("lbl_tel");
            TxtLblAdresse.Text       = T("lbl_adresse");
            TxtLblPied.Text          = T("lbl_pied");
            TxtApercuLabel.Text      = T("apercu");
            BtnSauvegarderImpression.Content = T("btn_save_imp");

            // Apparence
            TxtLblCouleur.Text       = T("lbl_couleur");
            TxtDescCouleur.Text      = T("desc_couleur");
            TxtLblPalette.Text       = T("lbl_palette");
            TxtLblCustomHex.Text     = T("lbl_custom_hex");
            TxtExempleCouleur.Text   = T("exemple_hex");
            TxtBtnAppliquer.Text     = T("btn_appliquer");

            // Langue
            TxtLblLangue.Text        = T("lbl_langue");
            TxtDescLangue.Text       = T("desc_langue");

            // Maintenance
            TxtLblInfosSys.Text      = T("lbl_infos_sys");
            TxtDescInfosSys.Text     = T("desc_infos_sys");
            TxtLblVersion.Text       = T("lbl_version");
            TxtLblEtatBDD.Text       = T("lbl_etat_bdd");
            TxtEtatBDD.Text          = T("txt_etat_bdd");
            TxtLblCheminBDD.Text     = T("lbl_chemin_bdd");
            TxtLblDiagnostic.Text    = T("lbl_diagnostic");
            TxtDescDiagnostic.Text   = T("desc_diagnostic");
            BtnOuvrirLogs.Content    = T("btn_ouvrir_logs");

            // Re-afficher le panneau actif avec le bon titre/desc
            AfficherPanneau(_panneauActif);
        }

        private void MajCarteLangue()
        {
            bool isFr = _langueActive == "fr";

            // Lire la couleur accent depuis les ressources (c'est une Color, pas une string)
            Color accentColor;
            try
            {
                accentColor = (Color)Application.Current.Resources["AccentColor"];
            }
            catch
            {
                accentColor = Color.FromRgb(0xCC, 0x00, 0x00);
            }
            var accentBrush   = new SolidColorBrush(accentColor);
            var neutralBrush  = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB));
            var whiteBrush    = Brushes.White;
            var offWhiteBrush = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));

            CardFR.BorderBrush = isFr  ? accentBrush   : neutralBrush;
            CardEN.BorderBrush = !isFr ? accentBrush   : neutralBrush;
            CardFR.Background  = isFr  ? whiteBrush    : offWhiteBrush;
            CardEN.Background  = !isFr ? whiteBrush    : offWhiteBrush;
            BadgeFR.Visibility = isFr  ? Visibility.Visible : Visibility.Collapsed;
            BadgeEN.Visibility = !isFr ? Visibility.Visible : Visibility.Collapsed;
        }

        // ==================================================================
        // Sélection langue
        // ==================================================================
        private async void SelectionnerLangue(object sender, MouseButtonEventArgs e)
        {
            if (sender is StackPanel sp && sp.Tag is string code)
            {
                // ✅ CORRECTIF AUDIT #15 : Utiliser LanguageService pour changement global
                await _languageService.SetLanguageAsync(code);
                _langueActive = code;
                AppliquerTraduction();
                MajCarteLangue();
                TxtMsgLangue.Text       = code == "fr"
                    ? "✅  Langue changée en Français." : "✅  Language changed to English.";
                TxtMsgLangue.Foreground = Brushes.Green;
            }
        }

        // ==================================================================
        // Apparence — changement de couleur en temps réel
        // ==================================================================
        private void BtnCouleurPredefinie_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string hex)
            {
                TxtCouleurHex.Text = hex;
                AppliquerCouleur(hex);
            }
        }

        private void BtnAppliquerCouleur_Click(object sender, RoutedEventArgs e)
        {
            AppliquerCouleur(TxtCouleurHex.Text.Trim());
        }

        private async void AppliquerCouleur(string hex)
        {
            try
            {
                // ✅ CORRECTIF AUDIT #16 : Utiliser ThemeService pour application globale
                await _themeService.SetAccentColorAsync(hex);
                
                // Mise à jour de l'aperçu local
                var couleur = (Color)ColorConverter.ConvertFromString(hex);
                var brush = new SolidColorBrush(couleur);
                PreviewCouleur.Background = brush;
                TxtCouleurHex.Text = hex;

                // Afficher confirmation
                string msg = _langueActive == "fr" ? "✅  Couleur appliquée." : "✅  Color applied.";
                TxtMsgApparence.Text = msg;
                TxtMsgApparence.Foreground = new SolidColorBrush(Colors.Green);
            }
            catch (Exception ex)
            {
                string msg = _langueActive == "fr" ? "Erreur couleur." : "Color error.";
                TxtMsgApparence.Text = msg + " " + ex.Message;
                TxtMsgApparence.Foreground = new SolidColorBrush(Colors.Red);
            }
        }

        // ==================================================================
        // Onglet Langue
        // ==================================================================
        private async void BtnLangueFR_Click(object sender, RoutedEventArgs e)
        {
            await ChangerLangue("fr");
        }

        private async void BtnLangueEN_Click(object sender, RoutedEventArgs e)
        {
            await ChangerLangue("en");
        }

        private async Task ChangerLangue(string code)
        {
            try
            {
                // ✅ CORRECTIF AUDIT #15 : Utiliser LanguageService pour changement global
                await _languageService.SetLanguageAsync(code);
                _langueActive = code;
                
                // Mettre à jour les badges visuels
                BadgeFR.Visibility = code == "fr" ? Visibility.Visible : Visibility.Collapsed;
                BadgeEN.Visibility = code == "en" ? Visibility.Visible : Visibility.Collapsed;
                
                // Mettre à jour les cartes visuelles
                CardFR.Background = code == "fr" ? Brushes.White : new SolidColorBrush(Color.FromRgb(249, 250, 251));
                CardFR.BorderBrush = code == "fr" ? (Brush)Application.Current.Resources["AccentBrush"] : new SolidColorBrush(Color.FromRgb(229, 231, 235));
                CardEN.Background = code == "en" ? Brushes.White : new SolidColorBrush(Color.FromRgb(249, 250, 251));
                CardEN.BorderBrush = code == "en" ? (Brush)Application.Current.Resources["AccentBrush"] : new SolidColorBrush(Color.FromRgb(229, 231, 235));
                
                // Afficher confirmation
                string msg = code == "fr" ? "✅  Langue changée." : "✅  Language changed.";
                if (TxtMsgLangue != null)
                {
                    TxtMsgLangue.Text = msg;
                    TxtMsgLangue.Foreground = new SolidColorBrush(Colors.Green);
                }
            }
            catch (Exception ex)
            {
                string msg = _langueActive == "fr" ? "Erreur langue." : "Language error.";
                if (TxtMsgLangue != null)
                {
                    TxtMsgLangue.Text = msg + " " + ex.Message;
                    TxtMsgLangue.Foreground = new SolidColorBrush(Colors.Red);
                }
            }
        }

        // ==================================================================
        // Onglet Comptabilité
        // ==================================================================
        private async void BtnSauvegarderComptabilite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await _p.DefinirModeCA(RbModeTotal.IsChecked == true ? "Total" : "Encaisse");

                if (!decimal.TryParse(TxtSeuilDepenses.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal seuil) || seuil < 0)
                { Erreur(TxtMsgComptabilite, "Seuil invalide."); return; }
                await _p.DefinirSeuilAlerteDépenses(seuil);

                if (!decimal.TryParse(TxtBudgetLoyer.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal loyer) || loyer < 0)
                { Erreur(TxtMsgComptabilite, "Budget loyer invalide."); return; }
                await _p.DefinirBudgetLoyer(loyer);

                await _p.DefinirApprobationDepenses(ChkApprobationDep.IsChecked == true);
                OK(TxtMsgComptabilite, _langueActive == "fr" ? "✅  Enregistré." : "✅  Saved.");
            }
            catch (Exception ex) { Erreur(TxtMsgComptabilite, ex.Message); }
        }

        // ==================================================================
        // Onglet Métier
        // ==================================================================
        private async void BtnSauvegarderMetier_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(TxtDelaiAlerte.Text, out int d) || d <= 0)
                { Erreur(TxtMsgMetier, "Délai invalide."); return; }
                await _p.DefinirDelaiAlerteRendezVousHeures(d);

                if (!int.TryParse(TxtSeuiRetard.Text, out int r) || r <= 0)
                { Erreur(TxtMsgMetier, "Seuil retard invalide."); return; }
                await _p.DefinirSeuiRetardHeures(r);

                if (!decimal.TryParse(TxtSalaireSecretaire.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal sal) || sal < 0)
                { Erreur(TxtMsgMetier, "Salaire invalide."); return; }
                await _p.DefinirSalaireMensuelSecretaire(sal);

                if (!decimal.TryParse(TxtTauxCommission.Text, out decimal t) || t <= 0)
                { Erreur(TxtMsgMetier, "Taux invalide."); return; }
                await _p.DefinirTauxCommissionDefaut(t);

                if (!decimal.TryParse(TxtPrimeZeroDefaut.Text.Replace(" ", "").Replace("\u00a0", ""),
                    out decimal p) || p < 0)
                { Erreur(TxtMsgMetier, "Prime invalide."); return; }
                await _p.DefinirPrimeZeroDefaut(p);

                OK(TxtMsgMetier, _langueActive == "fr" ? "✅  Enregistré." : "✅  Saved.");
            }
            catch (Exception ex) { Erreur(TxtMsgMetier, ex.Message); }
        }

        // ==================================================================
        // Onglet Messages
        // ==================================================================
        private async void BtnSauvegarderMessages_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(TxtMsgCommandePrete.Text))
                { Erreur(TxtMsgWhatsApp, "Message 1 obligatoire."); return; }
                await _p.DefinirMsgCommandePrete(TxtMsgCommandePrete.Text.Trim());
                await _p.DefinirMsgRappelRdv(TxtMsgRappelRdv.Text.Trim());
                await _p.DefinirMsgRetouchePrete(TxtMsgRetouchePrete.Text.Trim());
                OK(TxtMsgWhatsApp, _langueActive == "fr" ? "✅  Messages enregistrés." : "✅  Messages saved.");
            }
            catch (Exception ex) { Erreur(TxtMsgWhatsApp, ex.Message); }
        }

        private async void BtnResetMessages_Click(object sender, RoutedEventArgs e)
        {
            var r = MessageBox.Show(
                _langueActive == "fr"
                    ? "Remettre tous les messages WhatsApp aux valeurs par défaut ?"
                    : "Reset all WhatsApp messages to defaults?",
                _langueActive == "fr" ? "Confirmation" : "Confirm",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r != MessageBoxResult.Yes) return;

            await _p.DefinirMsgCommandePrete("");
            await _p.DefinirMsgRappelRdv("");
            await _p.DefinirMsgRetouchePrete("");
            TxtMsgCommandePrete.Text = await _p.ObtenirMsgCommandePrete();
            TxtMsgRappelRdv.Text     = await _p.ObtenirMsgRappelRdv();
            TxtMsgRetouchePrete.Text = await _p.ObtenirMsgRetouchePrete();
            OK(TxtMsgWhatsApp, _langueActive == "fr" ? "✅  Remis par défaut." : "✅  Reset to defaults.");
        }

        // ==================================================================
        // Onglet Sauvegarde
        // ==================================================================
        private async void BtnSauvegarderSauvegarde_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!int.TryParse(TxtFrequenceSync.Text, out int freq) || freq < 0)
                { Erreur(TxtMsgDrive, "Fréquence invalide."); return; }
                await _p.DefinirFrequenceSyncHeures(freq);
                await _p.DefinirCompresserPhotos(ChkCompresserPhotos.IsChecked == true);
                OK(TxtMsgDrive, _langueActive == "fr" ? "✅  Paramètres enregistrés." : "✅  Settings saved.");
            }
            catch (Exception ex) { Erreur(TxtMsgDrive, ex.Message); }
        }

        private async void BtnSauvegarderDrive_Click(object sender, RoutedEventArgs e)
        {
            BtnSauvegarderDrive.IsEnabled = false;
            PanelProgression.Visibility   = Visibility.Visible;
            TxtMsgDrive.Text = "";

            bool ok = await _drive.SauvegarderAsync(
                new Progress<string>(msg => TxtProgression.Text = msg));

            PanelProgression.Visibility   = Visibility.Collapsed;
            BtnSauvegarderDrive.IsEnabled = true;

            if (ok) OK(TxtMsgDrive, "✅  Sauvegarde Google Drive réussie !");
            else    Erreur(TxtMsgDrive, "❌  " + _drive.StatutDerniereSync);
            MajPastilleDrive();
        }

        private void MajPastilleDrive()
        {
            if (!_initialise) return;
            bool ok = _drive.DerniereSyncReussie;
            PastilleStatut.Background = ok
                ? new SolidColorBrush(Color.FromRgb(0x22, 0xC5, 0x5E))
                : new SolidColorBrush(Color.FromRgb(0x37, 0x41, 0x51));
            TxtStatutDrive.Text = ok
                ? (_langueActive == "fr" ? "🟢  Synchronisé" : "🟢  Synced")
                : (_langueActive == "fr" ? "⚪  Non synchronisé" : "⚪  Not synced");
            TxtDernierSync.Text = _drive.DateDerniereSync.HasValue
                ? (_langueActive == "fr"
                    ? $"Dernière sauvegarde : {_drive.DateDerniereSync.Value:dd/MM/yyyy à HH:mm}"
                    : $"Last backup: {_drive.DateDerniereSync.Value:MM/dd/yyyy at HH:mm}")
                : (_langueActive == "fr" ? "Dernière sauvegarde : jamais" : "Last backup: never");
        }

        // ==================================================================
        // Onglet Impression
        // ==================================================================
        private void MajApercuRecu()
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
                { Erreur(TxtMsgImpression, "Nom obligatoire."); return; }
                
                // ✅ CORRECTIF AUDIT #17 : Utiliser ReceiptService pour synchronisation globale
                var info = new ReceiptInfo
                {
                    NomAtelier = TxtNomAtelier.Text.Trim(),
                    Telephone = TxtTelAtelier.Text.Trim(),
                    Adresse = TxtAdresseAtelier.Text.Trim(),
                    PiedRecu = TxtPiedRecu.Text.Trim()
                };
                
                await _receiptService.UpdateReceiptInfoAsync(info);
                
                OK(TxtMsgImpression, _langueActive == "fr" ? "✅  Enregistré." : "✅  Saved.");
            }
            catch (Exception ex) { Erreur(TxtMsgImpression, ex.Message); }
        }

        // ==================================================================
        // Onglet Maintenance & Support
        // ==================================================================
        private void ChargerInfosMaintenance()
        {
            try
            {
                // Chemin de la base de données
                var dbContext = App.Services.GetRequiredService<ApplicationDbContext>();
                string connectionString = dbContext.Database.GetConnectionString() ?? "";
                string dbPath = connectionString.Replace("Data Source=", "").Split(';')[0];
                TxtCheminBDD.Text = dbPath;

                // Chemin des logs
                string cheminLogs = _log.ObtenirCheminDossierLogs();
                TxtCheminLogs.Text = _langueActive == "fr"
                    ? $"Emplacement : {cheminLogs}"
                    : $"Location: {cheminLogs}";
            }
            catch (Exception ex)
            {
                TxtCheminBDD.Text = $"Erreur: {ex.Message}";
            }
        }

        private void BtnOuvrirLogs_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string cheminLogs = _log.ObtenirCheminDossierLogs();

                // Ouvrir le dossier dans l'Explorateur Windows
                Process.Start(new ProcessStartInfo
                {
                    FileName = cheminLogs,
                    UseShellExecute = true,
                    Verb = "open"
                });

                // Log l'action
                string utilisateur = _auth.UtilisateurConnecte?.Nom ?? "Inconnu";
                _log.LogAction("Ouverture dossier logs", $"Utilisateur: {utilisateur}", utilisateur);

                TxtMsgMaintenance.Text = _langueActive == "fr"
                    ? "✅  Dossier des logs ouvert avec succès."
                    : "✅  Logs folder opened successfully.";
                TxtMsgMaintenance.Foreground = Brushes.Green;
            }
            catch (Exception ex)
            {
                TxtMsgMaintenance.Text = _langueActive == "fr"
                    ? $"❌  Erreur lors de l'ouverture du dossier : {ex.Message}"
                    : $"❌  Error opening folder: {ex.Message}";
                TxtMsgMaintenance.Foreground = Brushes.Red;

                _log.LogError("Erreur ouverture dossier logs", ex, _auth.UtilisateurConnecte?.Nom);
            }
        }

        // ==================================================================
        // Helpers
        // ==================================================================
        private static void OK(TextBlock tb, string msg)
        {
            tb.Text = msg;
            tb.Foreground = Brushes.Green;
        }
        private static void Erreur(TextBlock tb, string msg)
        {
            tb.Text = msg;
            tb.Foreground = Brushes.Red;
        }

        // ✅ Validation des champs numériques
        private void TxtSalaireSecretaire_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtSalaireSecretaire_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            ValidationHelper.TextBox_Pasting(sender, e);
        }

        private void TxtTauxCommission_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtTauxCommission_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            ValidationHelper.TextBox_Pasting(sender, e);
        }

        private void TxtPrimeZeroDefaut_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtPrimeZeroDefaut_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            ValidationHelper.TextBox_Pasting(sender, e);
        }
    }
}
