using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using GestionCoutureApp.Helpers;

namespace GestionCoutureApp.Views
{
    // ──────────────────────────────────────────────────────────────────────
    // DTO de carte — utilisé par ItemsTypesVetements
    // ──────────────────────────────────────────────────────────────────────
    internal sealed class TypeVetementCarte
    {
        public int    IdTypeVetement    { get; set; }
        public string Nom               { get; set; } = string.Empty;
        public decimal PrixBase         { get; set; }
        public List<string> Mesures     { get; set; } = new();
        public int    NbMesures         { get; set; }
        public string VariantesAffichage{ get; set; } = string.Empty;

        // Propriétés calculées pour le binding dans le DataTemplate
        public string PrixBaseAffiche =>
            PrixBase.ToString("N0") + " FCFA";

        public string LabelMesures =>
            NbMesures == 0
                ? "AUCUNE MESURE REQUISE"
                : $"MESURES REQUISES ({NbMesures})";

        // Visibilité de la zone variantes (masquée si vide)
        public Visibility VariantesVisibility =>
            string.IsNullOrWhiteSpace(VariantesAffichage)
                ? Visibility.Collapsed
                : Visibility.Visible;
    }

    // ──────────────────────────────────────────────────────────────────────
    public partial class TypesVetementsView : Page
    {
        private readonly ApplicationDbContext _context;
        private TypeVetement? _typeSelectionne;
        private readonly List<string> _mesuresTemporaires = new List<string>();

        // ✅ Protection contre double-clic
        private bool _enCoursEnregistrement = false;

        // Timer pour le bandeau de confirmation (3 secondes)
        private readonly DispatcherTimer _timerBandeau = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };

        public TypesVetementsView()
        {
            // Garde-fou accès Boss — inchangé
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();

            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            // Gestionnaire du timer bandeau
            _timerBandeau.Tick += (s, e) =>
            {
                _timerBandeau.Stop();
                BandeauConfirmation.Visibility = Visibility.Collapsed;
            };

            // Fermeture modale par touche Échap
            KeyDown += Page_KeyDown;

            ChargerTypes();
        }

        // ──────────────────────────────────────────────────────────────────
        // PROJECTION UNIQUE — utilisée par ChargerTypes et TxtRecherche
        // ──────────────────────────────────────────────────────────────────
        private List<TypeVetementCarte> ProjetterTypes(string? terme = null)
        {
            var query = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Include(t => t.Descriptions)
                .AsNoTracking()
                .AsQueryable();

            if (!string.IsNullOrWhiteSpace(terme))
                query = query.Where(t => t.Nom.ToLower().Contains(terme));

            return query
                .OrderBy(t => t.Nom)
                .Select(t => new TypeVetementCarte
                {
                    IdTypeVetement    = t.IdTypeVetement,
                    Nom               = t.Nom,
                    PrixBase          = t.PrixBase,
                    Mesures           = t.MesuresRequises.Select(m => m.NomMesure).ToList(),
                    NbMesures         = t.MesuresRequises.Count,
                    VariantesAffichage = t.Descriptions.Any()
                        ? string.Join(", ", t.Descriptions.Select(d => d.Texte))
                        : string.Empty
                })
                .ToList();
        }

        private void ChargerTypes()
        {
            var cartes = ProjetterTypes();
            ItemsTypesVetements.ItemsSource = null;
            ItemsTypesVetements.ItemsSource = cartes;

            // Gestion état vide
            if (cartes.Count == 0)
            {
                PanelEtatVide.Visibility = Visibility.Visible;
                TxtEtatVide.Text = string.IsNullOrWhiteSpace(TxtRecherche.Text)
                    ? "Aucun type de vêtement enregistré. Cliquez sur + Nouveau Modèle."
                    : "Aucun résultat pour cette recherche.";
            }
            else
            {
                PanelEtatVide.Visibility = Visibility.Collapsed;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // BARRE DE RECHERCHE
        // ──────────────────────────────────────────────────────────────────
        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Gestion du placeholder
            TxtRecherchePlaceholder.Visibility =
                string.IsNullOrEmpty(TxtRecherche.Text)
                    ? Visibility.Visible
                    : Visibility.Collapsed;

            string terme = TxtRecherche.Text.Trim().ToLower();
            var cartes = ProjetterTypes(string.IsNullOrEmpty(terme) ? null : terme);

            ItemsTypesVetements.ItemsSource = null;
            ItemsTypesVetements.ItemsSource = cartes;

            if (cartes.Count == 0)
            {
                PanelEtatVide.Visibility = Visibility.Visible;
                TxtEtatVide.Text = string.IsNullOrEmpty(terme)
                    ? "Aucun type de vêtement enregistré. Cliquez sur + Nouveau Modèle."
                    : "Aucun résultat pour cette recherche.";
            }
            else
            {
                PanelEtatVide.Visibility = Visibility.Collapsed;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // CHARGEMENT D'UN TYPE POUR ÉDITION (extrait de l'ancien SelectionChanged)
        // ──────────────────────────────────────────────────────────────────
        private void ChargerTypePourEdition(int id)
        {
            _typeSelectionne = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Include(t => t.Descriptions)
                .FirstOrDefault(t => t.IdTypeVetement == id);

            if (_typeSelectionne == null) return;

            TxtNom.Text     = _typeSelectionne.Nom;
            TxtPrixBase.Text = _typeSelectionne.PrixBase.ToString();

            // Mesures temporaires
            _mesuresTemporaires.Clear();
            foreach (var m in _typeSelectionne.MesuresRequises)
                _mesuresTemporaires.Add(m.NomMesure);
            RafraichirListeMesures();

            // Variantes courantes — jointure par virgule
            TxtVariantes.Text = _typeSelectionne.Descriptions.Any()
                ? string.Join(", ", _typeSelectionne.Descriptions.Select(d => d.Texte))
                : string.Empty;
        }

        // ──────────────────────────────────────────────────────────────────
        // MODALE — ouverture / fermeture
        // ──────────────────────────────────────────────────────────────────
        private void OuvrirModal(string titre, bool afficherSupprimer = false)
        {
            TxtModalTitre.Text = titre;
            BtnSupprimerDansModal.Visibility =
                afficherSupprimer ? Visibility.Visible : Visibility.Collapsed;
            AfficherMessage(string.Empty, null);
            ModalTypeVetement.Visibility = Visibility.Visible;
            TxtNom.Focus();
        }

        private void FermerModal()
        {
            ModalTypeVetement.Visibility = Visibility.Collapsed;
        }

        // Empêche les clics dans la boîte de buller vers l'overlay
        private void Modal_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

        // ── Touche Échap ──
        private void Page_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Escape &&
                ModalTypeVetement.Visibility == Visibility.Visible)
            {
                BtnVider_Click(sender, e);
                FermerModal();
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // BOUTONS D'ACTION SUR LES CARTES
        // ──────────────────────────────────────────────────────────────────

        // « + Nouveau Modèle »
        private void BtnOuvrirNouveauType_Click(object sender, RoutedEventArgs e)
        {
            ViderFormulaire();
            OuvrirModal("Nouveau type de vêtement", afficherSupprimer: false);
        }

        // ✏️ sur une carte
        private void BtnModifierType_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is int id)
            {
                ViderFormulaire();
                ChargerTypePourEdition(id);
                if (_typeSelectionne == null) return;
                OuvrirModal("Modifier le type", afficherSupprimer: true);
            }
        }

        // 🗑 dans le pied de la modale (visible uniquement en édition)
        private void BtnSupprimerDansModal_Click(object sender, RoutedEventArgs e)
        {
            // Délègue directement à la logique existante (inchangée)
            BtnSupprimer_Click(sender, e);
        }

        // Fermer / Annuler / ✕
        private void BtnFermerModal_Click(object sender, RoutedEventArgs e)
        {
            ViderFormulaire();
            FermerModal();
        }

        // ──────────────────────────────────────────────────────────────────
        // MESURES — gestionnaires existants INCHANGÉS
        // ──────────────────────────────────────────────────────────────────
        private void BtnAjouterMesure_Click(object sender, RoutedEventArgs e)
        {
            string nom = TxtNomMesure.Text.Trim();
            if (string.IsNullOrWhiteSpace(nom))
            {
                AfficherMessage("Saisissez un nom de mesure.", System.Windows.Media.Brushes.Red);
                return;
            }

            // ✅ Validation de sécurité pour le nom de mesure
            if (!ValidationHelper.EstTexteSecurise(nom, out string erreur))
            {
                AfficherMessage($"Nom invalide : {erreur}", System.Windows.Media.Brushes.Red);
                return;
            }

            if (_mesuresTemporaires.Any(m => m.Equals(nom, StringComparison.OrdinalIgnoreCase)))
            {
                AfficherMessage("Cette mesure existe déjà.", System.Windows.Media.Brushes.Red);
                return;
            }

            _mesuresTemporaires.Add(nom);
            RafraichirListeMesures();
            TxtNomMesure.Clear();
            AfficherMessage(string.Empty, null);
        }

        // Touche Entrée dans TxtNomMesure = Ajouter
        private void TxtNomMesure_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                BtnAjouterMesure_Click(sender, new RoutedEventArgs());
                e.Handled = true;
            }
        }

        // Bouton × sur une pilule de mesure (NOUVEAU)
        private void BtnSupprimerMesureTag_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string nomMesure)
            {
                _mesuresTemporaires.Remove(nomMesure);
                RafraichirListeMesures();
            }
        }

        private void RafraichirListeMesures()
        {
            ListeMesures.ItemsSource = null;
            ListeMesures.ItemsSource = _mesuresTemporaires.ToList();
        }

        // ──────────────────────────────────────────────────────────────────
        // ENREGISTRER — logique INCHANGÉE + gestion Descriptions + FermerModal
        // ──────────────────────────────────────────────────────────────────
        private void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            // ✅ Protection contre double-clic
            if (_enCoursEnregistrement)
            {
                AfficherMessage("Enregistrement en cours...", System.Windows.Media.Brushes.Orange);
                return;
            }

            _enCoursEnregistrement = true;
            BtnEnregistrer.IsEnabled = false;

            try
            {
                // ── Validations inchangées ──
                if (string.IsNullOrWhiteSpace(TxtNom.Text))
                {
                    AfficherMessage("Saisissez le nom du type.", System.Windows.Media.Brushes.Red);
                    return;
                }

                if (!decimal.TryParse(TxtPrixBase.Text, out decimal prixBase) || prixBase <= 0)
                {
                    AfficherMessage("Saisissez un prix valide.", System.Windows.Media.Brushes.Red);
                    return;
                }

                // ── Parsing et validation des variantes ──
                var variantes = ParserVariantes(TxtVariantes.Text);
                if (variantes == null)
                    return; // message déjà affiché par ParserVariantes

                if (_typeSelectionne == null)
                {
                    // Création
                    var nouveau = new TypeVetement
                    {
                        Nom      = TxtNom.Text.Trim(),
                        PrixBase = prixBase,
                        MesuresRequises = _mesuresTemporaires.Select(nom => new MesureRequise
                        {
                            NomMesure = nom
                        }).ToList(),
                        Descriptions = variantes.Select(v => new DescriptionCourante
                        {
                            Texte = v
                        }).ToList()
                    };

                    _context.TypesVetements.Add(nouveau);
                    _context.SaveChanges();
                    AfficherBandeau("Type ajouté avec succès.");
                }
                else
                {
                    // Modification — logique mesures inchangée
                    _context.MesuresRequises.RemoveRange(
                        _context.MesuresRequises.Where(m => m.IdTypeVetement == _typeSelectionne.IdTypeVetement));

                    // Modification descriptions
                    _context.DescriptionsCourantes.RemoveRange(
                        _context.DescriptionsCourantes.Where(d => d.IdTypeVetement == _typeSelectionne.IdTypeVetement));

                    _typeSelectionne.Nom      = TxtNom.Text.Trim();
                    _typeSelectionne.PrixBase = prixBase;
                    _typeSelectionne.MesuresRequises = _mesuresTemporaires.Select(nom => new MesureRequise
                    {
                        NomMesure = nom
                    }).ToList();
                    _typeSelectionne.Descriptions = variantes.Select(v => new DescriptionCourante
                    {
                        Texte = v
                    }).ToList();

                    _context.SaveChanges();
                    AfficherBandeau("Type modifié avec succès.");
                }

                ChargerTypes();
                ViderFormulaire();
                FermerModal();   // ← seul ajout dans la branche succès
            }
            catch (Exception ex)
            {
                // La modale reste ouverte en cas d'erreur
                AfficherMessage("Erreur : " + ex.Message, System.Windows.Media.Brushes.Red);
            }
            finally
            {
                // ✅ Toujours réactiver le bouton
                _enCoursEnregistrement = false;
                BtnEnregistrer.IsEnabled = true;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // PARSING VARIANTES
        // Retourne null si une validation échoue (message affiché).
        // ──────────────────────────────────────────────────────────────────
        private List<string>? ParserVariantes(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte))
                return new List<string>();

            var parties = texte
                .Split(',')
                .Select(v => v.Trim())
                .Where(v => !string.IsNullOrEmpty(v))
                // Dédoublonnage insensible à la casse
                .GroupBy(v => v.ToLowerInvariant())
                .Select(g => g.First())
                .ToList();

            foreach (var v in parties)
            {
                if (v.Length > 200)
                {
                    AfficherMessage(
                        $"Variante trop longue (max 200 caractères) : « {v.Substring(0, 30)}… »",
                        System.Windows.Media.Brushes.Red);
                    return null;
                }

                if (!ValidationHelper.EstTexteSecurise(v, out string erreur))
                {
                    AfficherMessage($"Variante invalide « {v} » : {erreur}",
                        System.Windows.Media.Brushes.Red);
                    return null;
                }
            }

            return parties;
        }

        // ──────────────────────────────────────────────────────────────────
        // SUPPRIMER — logique INCHANGÉE
        // ──────────────────────────────────────────────────────────────────
        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_typeSelectionne == null)
            {
                AfficherMessage("Sélectionnez un type.", System.Windows.Media.Brushes.Red);
                return;
            }

            var r = MessageBox.Show(
                $"Supprimer le type \"{_typeSelectionne.Nom}\" ?",
                "Confirmation", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (r != MessageBoxResult.Yes) return;

            _context.TypesVetements.Remove(_typeSelectionne);
            _context.SaveChanges();
            AfficherBandeau("Type supprimé.");
            ChargerTypes();
            ViderFormulaire();
            FermerModal();
        }

        // Bouton Vider (Annuler dans la modale) — INCHANGÉ
        private void BtnVider_Click(object sender, RoutedEventArgs e)
        {
            ViderFormulaire();
        }

        private void ViderFormulaire()
        {
            _typeSelectionne = null;
            TxtNom.Clear();
            TxtPrixBase.Clear();
            TxtNomMesure.Clear();
            TxtVariantes.Clear();
            _mesuresTemporaires.Clear();
            RafraichirListeMesures();
            // Pas de GridTypes.SelectedItem = null (remplacé par ItemsControl sans sélection)
            AfficherMessage(string.Empty, null);
        }

        // ──────────────────────────────────────────────────────────────────
        // VALIDATIONS — gestionnaires INCHANGÉS
        // ──────────────────────────────────────────────────────────────────
        private void TxtPrixBase_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            ValidationHelper.TextBox_PreviewTextInputDecimal(sender, e);
        }

        private void TxtPrixBase_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            ValidationHelper.TextBox_Pasting(sender, e);
        }

        // ✅ Validation sécurisée pour le nom de mesure
        private void TxtNomMesure_PreviewTextInput(object sender, TextCompositionEventArgs e)
        {
            try
            {
                ValidationHelper.TextBox_PreviewTextInputTexteSecurise(sender, e);
            }
            catch
            {
                e.Handled = true;
            }
        }

        // ──────────────────────────────────────────────────────────────────
        // HELPERS D'AFFICHAGE
        // ──────────────────────────────────────────────────────────────────

        /// <summary>
        /// Affiche ou masque le message d'erreur/info dans la modale.
        /// Passer null comme brosse pour masquer.
        /// </summary>
        private void AfficherMessage(string message,
            System.Windows.Media.SolidColorBrush? couleur)
        {
            if (string.IsNullOrEmpty(message))
            {
                TxtMessage.Visibility = Visibility.Collapsed;
                TxtMessage.Text = string.Empty;
            }
            else
            {
                TxtMessage.Text = message;
                if (couleur != null) TxtMessage.Foreground = couleur;
                TxtMessage.Visibility = Visibility.Visible;
            }
        }

        /// <summary>
        /// Affiche le bandeau vert de confirmation sur la page pendant 3 secondes.
        /// </summary>
        private void AfficherBandeau(string message)
        {
            TxtBandeauMessage.Text = message;
            BandeauConfirmation.Visibility = Visibility.Visible;
            _timerBandeau.Stop();
            _timerBandeau.Start();
        }
    }
}
