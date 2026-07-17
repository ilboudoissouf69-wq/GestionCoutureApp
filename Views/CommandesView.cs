using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class CommandesView : Page
    {
        private readonly ICommandeService _commandeService;
        private readonly IClientService _clientService;
        private readonly ApplicationDbContext _context;
        private int _commandeSelectionneeId;
        private double _prixBaseActuel;
        private List<TypeVetement> _typesVetement;
        private string _cheminPhotoTemporaire = string.Empty;
        private string _roleUtilisateur;

        public CommandesView()
        {
            InitializeComponent();
            _commandeService = App.Services.GetRequiredService<ICommandeService>();
            _clientService = App.Services.GetRequiredService<IClientService>();
            _context = App.Services.GetRequiredService<ApplicationDbContext>();

            // ===== RECUPERER LE ROLE =====
            var authService = App.Services.GetRequiredService<IAuthService>();
            _roleUtilisateur = authService.UtilisateurConnecte?.Role ?? "";

            // ===== SECRETAIRE : cacher modifier et supprimer =====
            if (_roleUtilisateur == "Secretaire")
            {
                BtnModifier.Visibility = Visibility.Collapsed;
                BtnSupprimer.Visibility = Visibility.Collapsed;
            }

            CmbClient.ItemsSource = _clientService.ObtenirTous();
            CmbCouturier.ItemsSource = _context.Employes.Where(e => e.Statut == "Actif").ToList();

            _typesVetement = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Include(t => t.Descriptions)
                .ToList();

            CmbTypeVetement.ItemsSource = _typesVetement.Select(t => new
            {
                t.IdTypeVetement,
                DisplayText = t.Nom + " (" + t.PrixBase + " FCFA)"
            }).ToList();
            CmbTypeVetement.SelectedValuePath = "IdTypeVetement";

            for (int i = 0; i <= 20; i++)
                CmbAjustement.Items.Add(i * 500);
            CmbAjustement.SelectedIndex = 0;

            // Heure debut auto
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");

            ChargerCommandes();
        }

        private void ChargerCommandes()
        {
            GridCommandes.ItemsSource = _commandeService.ObtenirTous();
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string motCle = TxtRecherche.Text.Trim();
            if (string.IsNullOrEmpty(motCle)) ChargerCommandes();
            else GridCommandes.ItemsSource = _commandeService.Rechercher(motCle);
        }

        // ------------------------------------------------------------------
        // Quand on choisit un type de vetement
        // ------------------------------------------------------------------
        private void CmbTypeVetement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (CmbTypeVetement.SelectedValue == null) return;
            int id = (int)CmbTypeVetement.SelectedValue;
            var type = _typesVetement.FirstOrDefault(t => t.IdTypeVetement == id);
            if (type == null) return;

            _prixBaseActuel = type.PrixBase;
            TxtPrixBase.Text = "Prix de base : " + type.PrixBase + " FCFA";
            TxtMontant.Text = type.PrixBase.ToString();

            CmbDescription.ItemsSource = type.Descriptions.ToList();
            CmbDescription.Text = string.Empty;

            PanelMesuresDynamiques.Children.Clear();
            TxtIndicationMesures.Text = type.MesuresRequises.Count + " mesure(s) requise(s) :";

            foreach (var mesure in type.MesuresRequises)
            {
                var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 6) };

                var label = new TextBlock
                {
                    Text = mesure.NomMesure,
                    Width = 160,
                    FontSize = 12,
                    VerticalAlignment = VerticalAlignment.Center
                };

                var combo = new ComboBox
                {
                    Width = 80,
                    FontSize = 12,
                    Tag = mesure.NomMesure
                };

                for (int i = 10; i <= 150; i++)
                    combo.Items.Add(i + " cm");
                combo.SelectedIndex = 0;

                row.Children.Add(label);
                row.Children.Add(combo);
                PanelMesuresDynamiques.Children.Add(row);
            }

            CalculerPrixTotal();
        }

        private void CmbAjustement_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            CalculerPrixTotal();
        }

        private void CalculerPrixTotal()
        {
            if (CmbAjustement.SelectedItem == null) return;
            double ajustement = (int)CmbAjustement.SelectedItem;
            double total = _prixBaseActuel + ajustement;
            TxtPrixTotal.Text = "Prix total : " + total + " FCFA";
            TxtMontant.Text = total.ToString();
        }

        // ------------------------------------------------------------------
        // Quand on clique sur une commande dans le tableau
        // ------------------------------------------------------------------
        private void GridCommandes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridCommandes.SelectedItem is Commande cmd)
            {
                _commandeSelectionneeId = cmd.IdCommande;
                CmbClient.SelectedValue = cmd.IdClient;
                if (cmd.IdCouturier.HasValue)
                    CmbCouturier.SelectedValue = cmd.IdCouturier.Value;

                var typeMatch = _typesVetement.FirstOrDefault(t => t.Nom == cmd.TypeVetement);
                if (typeMatch != null)
                    CmbTypeVetement.SelectedValue = typeMatch.IdTypeVetement;

                // Description : cherche dans la liste ou affiche le texte libre
                var descMatch = CmbDescription.Items
                    .Cast<DescriptionCourante>()
                    .FirstOrDefault(d => d.Texte == cmd.DescriptionPrecision);
                if (descMatch != null)
                    CmbDescription.SelectedItem = descMatch;
                else
                    CmbDescription.Text = cmd.DescriptionPrecision ?? "";

                TxtHeureDebut.Text = cmd.HeureDebut.ToString(@"hh\:mm");
                TxtHeureFin.Text = cmd.HeureFin?.ToString(@"hh\:mm") ?? "";

                DateFin.SelectedDate = cmd.DateFin;

                for (int i = 0; i < CmbStatut.Items.Count; i++)
                {
                    var item = (ComboBoxItem)CmbStatut.Items[i];
                    if (item.Content.ToString() == cmd.Statut) CmbStatut.SelectedIndex = i;
                }

                var mesuresExistantes = _commandeService.ObtenirMesures(cmd.IdCommande);
                foreach (var child in PanelMesuresDynamiques.Children)
                {
                    var row = (StackPanel)child;
                    var combo = (ComboBox)row.Children[1];
                    string nomMesure = combo.Tag?.ToString() ?? "";
                    var mesure = mesuresExistantes.FirstOrDefault(m => m.NomMesure == nomMesure);
                    if (mesure != null)
                    {
                        for (int j = 0; j < combo.Items.Count; j++)
                        {
                            if (combo.Items[j]?.ToString()?.StartsWith(mesure.Valeur) == true)
                            { combo.SelectedIndex = j; break; }
                        }
                    }
                }

                if (!string.IsNullOrEmpty(cmd.CheminPhoto) &&
                    System.IO.File.Exists(cmd.CheminPhoto))
                {
                    ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(cmd.CheminPhoto));
                    _cheminPhotoTemporaire = cmd.CheminPhoto;
                    TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
                else
                {
                    ImgPhoto.Source = null;
                    _cheminPhotoTemporaire = string.Empty;
                    TxtPhotoPlaceholder.Visibility = Visibility.Visible;
                    BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
                }
            }
        }

        // ------------------------------------------------------------------
        // Collecter les mesures
        // ------------------------------------------------------------------
        private List<Mesure> CollecterMesures()
        {
            var mesures = new List<Mesure>();
            foreach (var child in PanelMesuresDynamiques.Children)
            {
                var row = (StackPanel)child;
                var combo = (ComboBox)row.Children[1];
                if (combo.SelectedItem != null)
                {
                    string valeur = combo.SelectedItem.ToString()?.Replace(" cm", "") ?? "";
                    mesures.Add(new Mesure
                    {
                        NomMesure = combo.Tag?.ToString() ?? "",
                        Valeur    = valeur
                    });
                }
            }
            return mesures;
        }

        private TimeSpan? ParseHeure(string texte)
        {
            if (string.IsNullOrWhiteSpace(texte)) return null;
            if (TimeSpan.TryParse(texte, out var resultat)) return resultat;
            return null;
        }

        // ------------------------------------------------------------------
        // ===== BOUTONS CRUD =====
        // ------------------------------------------------------------------
        private void BtnCreer_Click(object sender, RoutedEventArgs e)
        {
            if (ChampsInvalides()) return;

            // SECRETAIRE : confirmation + mot de passe
            if (_roleUtilisateur == "Secretaire")
            {
                var confirm = MessageBox.Show(
                    "Voulez-vous reellement enregistrer cette commande ?\n\nCette action est irreversible.",
                    "Confirmation d'enregistrement",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (confirm != MessageBoxResult.Yes) return;

                if (!DemanderMotDePasse()) return;
            }

            var commande = new Commande
            {
                IdClient = (int)CmbClient.SelectedValue,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                TypeVetement = _typesVetement.First(t => t.IdTypeVetement == (int)CmbTypeVetement.SelectedValue).Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc1
                    ? dc1.Texte
                    : CmbDescription.Text,
                DateDebut = DateTime.Now,
                DateFin = DateFin.SelectedDate ?? DateTime.Now.AddDays(7),
                HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? DateTime.Now.TimeOfDay,
                HeureFin = ParseHeure(TxtHeureFin.Text),
                Statut     = ((ComboBoxItem)CmbStatut.SelectedItem).Content?.ToString() ?? "",
                MontantTotal = double.Parse(TxtMontant.Text),
                CheminPhoto  = _cheminPhotoTemporaire
            };

            _commandeService.Ajouter(commande, CollecterMesures());
            ChargerCommandes();
            ViderChamps();
            MessageBox.Show("Commande creee avec succes !", "Succes",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0)
            { MessageBox.Show("Selectionnez une commande.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            if (ChampsInvalides()) return;

            var commande = new Commande
            {
                IdCommande = _commandeSelectionneeId,
                IdClient = (int)CmbClient.SelectedValue,
                IdCouturier = CmbCouturier.SelectedValue as int?,
                TypeVetement = _typesVetement.First(t => t.IdTypeVetement == (int)CmbTypeVetement.SelectedValue).Nom,
                DescriptionPrecision = CmbDescription.SelectedItem is DescriptionCourante dc2
                    ? dc2.Texte
                    : CmbDescription.Text,
                DateFin = DateFin.SelectedDate ?? DateTime.Now.AddDays(7),
                HeureDebut = ParseHeure(TxtHeureDebut.Text) ?? DateTime.Now.TimeOfDay,
                HeureFin = ParseHeure(TxtHeureFin.Text),
                Statut     = ((ComboBoxItem)CmbStatut.SelectedItem).Content?.ToString() ?? "",
                MontantTotal = double.Parse(TxtMontant.Text),
                CheminPhoto  = _cheminPhotoTemporaire
            };

            _commandeService.Modifier(commande, CollecterMesures());
            ChargerCommandes();
            ViderChamps();
            MessageBox.Show("Commande modifiee !", "Succes",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_commandeSelectionneeId == 0)
            { MessageBox.Show("Selectionnez une commande.", "Attention", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
            var r = MessageBox.Show("Supprimer cette commande ?", "Confirmation",
                MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (r == MessageBoxResult.Yes)
            {
                _commandeService.Supprimer(_commandeSelectionneeId);
                ChargerCommandes();
                ViderChamps();
                MessageBox.Show("Commande supprimee.", "Succes",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnVider_Click(object sender, RoutedEventArgs e) { ViderChamps(); }

        private bool ChampsInvalides()
        {
            if (CmbClient.SelectedValue == null)
            { MessageBox.Show("Selectionnez un client.", "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning); return true; }
            if (CmbTypeVetement.SelectedValue == null)
            { MessageBox.Show("Selectionnez un type de vetement.", "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning); return true; }
            if (!double.TryParse(TxtMontant.Text, out double m) || m <= 0)
            { MessageBox.Show("Le montant doit etre positif.", "Montant invalide", MessageBoxButton.OK, MessageBoxImage.Warning); return true; }
            return false;
        }

        // ------------------------------------------------------------------
        // Import photo depuis le disque
        // ------------------------------------------------------------------
        private void BtnImporterPhoto_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog();
            dialog.Title = "Selectionner une photo";
            dialog.Filter = "Images|*.jpg;*.jpeg;*.png;*.bmp|Tous les fichiers|*.*";

            if (dialog.ShowDialog() == true)
            {
                try
                {
                    string dossierPhotos = System.IO.Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory, "photos");
                    if (!System.IO.Directory.Exists(dossierPhotos))
                        System.IO.Directory.CreateDirectory(dossierPhotos);

                    string extension = System.IO.Path.GetExtension(dialog.FileName);
                    string nomFichier = $"photo_{DateTime.Now:yyyyMMdd_HHmmss}{extension}";
                    string cheminDestination = System.IO.Path.Combine(dossierPhotos, nomFichier);

                    System.IO.File.Copy(dialog.FileName, cheminDestination, true);

                    _cheminPhotoTemporaire = cheminDestination;
                    ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(cheminDestination));
                    TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur lors de l'import : " + ex.Message,
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        // ------------------------------------------------------------------
        // Capture photo via webcam
        // ------------------------------------------------------------------
        private void BtnPrendrePhoto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var webcamWindow = new WebcamCaptureWindow();
                webcamWindow.Owner = Window.GetWindow(this);
                if (webcamWindow.ShowDialog() == true &&
                    !string.IsNullOrEmpty(webcamWindow.CapturedFilePath))
                {
                    _cheminPhotoTemporaire = webcamWindow.CapturedFilePath;
                    ImgPhoto.Source = new System.Windows.Media.Imaging.BitmapImage(
                        new Uri(_cheminPhotoTemporaire));
                    TxtPhotoPlaceholder.Visibility = Visibility.Collapsed;
                    BtnSupprimerPhoto.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur webcam : " + ex.Message,
                    "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ------------------------------------------------------------------
        // Supprimer photo
        // ------------------------------------------------------------------
        private void BtnSupprimerPhoto_Click(object sender, RoutedEventArgs e)
        {
            _cheminPhotoTemporaire = string.Empty;
            ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------
        // Vider tous les champs
        // ------------------------------------------------------------------
        private void ViderChamps()
        {
            _commandeSelectionneeId = 0;
            CmbClient.SelectedIndex = -1;
            CmbCouturier.SelectedIndex = -1;
            CmbTypeVetement.SelectedIndex = -1;
            CmbDescription.Text = "";
            CmbAjustement.SelectedIndex = 0;
            TxtPrixBase.Text = "Prix de base : -";
            TxtPrixTotal.Text = "Prix total : -";
            TxtMontant.Text = "";
            TxtHeureDebut.Text = DateTime.Now.ToString("HH:mm");
            TxtHeureFin.Text = "";
            DateFin.SelectedDate = null;
            CmbStatut.SelectedIndex = 0;
            _prixBaseActuel = 0;
            PanelMesuresDynamiques.Children.Clear();
            TxtIndicationMesures.Text = "Selectionnez un type de vetement";
            GridCommandes.SelectedItem = null;

            _cheminPhotoTemporaire = string.Empty;
            ImgPhoto.Source = null;
            TxtPhotoPlaceholder.Visibility = Visibility.Visible;
            BtnSupprimerPhoto.Visibility = Visibility.Collapsed;
        }

        // ------------------------------------------------------------------
        // ===== FENETRE MOT DE PASSE (securite secretaire) =====
        // ------------------------------------------------------------------
        private bool DemanderMotDePasse()
        {
            var dialog = new Window
            {
                Title = "Verification d'identite",
                Width = 400,
                Height = 260,
                MinHeight = 260,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = Brushes.White,
                SizeToContent = SizeToContent.Height
            };

            var mainPanel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            mainPanel.Children.Add(new TextBlock
            {
                Text = "Pour des raisons de securite,",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 2)
            });
            mainPanel.Children.Add(new TextBlock
            {
                Text = "veuillez saisir votre mot de passe :",
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            var passwordBox = new PasswordBox
            {
                Height = 38,
                FontSize = 14,
                Padding = new Thickness(10, 0, 10, 0),
                VerticalContentAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            };
            mainPanel.Children.Add(passwordBox);

            var message = new TextBlock
            {
                Text = "",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0xDC, 0x26, 0x26)),
                Margin = new Thickness(0, 0, 0, 16),
                Height = 18
            };
            mainPanel.Children.Add(message);

            // Séparateur
            mainPanel.Children.Add(new Separator
            {
                Background = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Margin = new Thickness(0, 0, 0, 14)
            });

            // Boutons
            var btnPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnOk = new Button
            {
                Content = "Confirmer",
                Width = 110,
                Height = 38,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = Brushes.White,
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 10, 0),
                Cursor = Cursors.Hand
            };

            var btnAnnuler = new Button
            {
                Content = "Annuler",
                Width = 100,
                Height = 38,
                FontSize = 13,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Background = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9)),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE2, 0xE8, 0xF0)),
                Cursor = Cursors.Hand
            };

            btnOk.Click += (s, ev) =>
            {
                var authService = App.Services.GetRequiredService<IAuthService>();
                string mdp = passwordBox.Password.Trim();
                var user = authService.UtilisateurConnecte;
                if (user != null && authService.HasherMotDePasse(mdp) == user.MotDePasse)
                {
                    dialog.DialogResult = true;
                    dialog.Close();
                }
                else
                {
                    message.Text = "Mot de passe incorrect !";
                    passwordBox.Clear();
                    passwordBox.Focus();
                }
            };

            btnAnnuler.Click += (s, ev) =>
            {
                dialog.DialogResult = false;
                dialog.Close();
            };

            // Permettre validation avec Entree
            passwordBox.KeyDown += (s, ev) =>
            {
                if (ev.Key == System.Windows.Input.Key.Enter)
                    btnOk.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            };

            btnPanel.Children.Add(btnOk);
            btnPanel.Children.Add(btnAnnuler);
            mainPanel.Children.Add(btnPanel);

            dialog.Content = mainPanel;
            dialog.Owner = Window.GetWindow(this);

            return dialog.ShowDialog() == true;
        }
    }
}