// Views/RetoursView.cs
// =============================================
// Point 4 — Écran de gestion des retours (reprises gratuites).
//
// Fonctionnalités :
//   - Liste de tous les retours avec filtre par recherche
//   - Création d'un nouveau retour (sélection commande + pièce)
//   - Passage du statut "Signalé" → "En reprise" → "Résolu"
//   - accessible au Boss et à la Secrétaire
// =============================================

using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class RetoursView : Page
    {
        private readonly IRetourService _retourService;
        private readonly ApplicationDbContext _context;
        private readonly Models.Employe _utilisateur;
        private List<Commande> _commandes;
        private int _retourSelectionneId;

        public RetoursView()
        {
            InitializeComponent();

            _retourService = App.Services.GetRequiredService<IRetourService>();
            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            var authService = App.Services.GetRequiredService<IAuthService>();
            _utilisateur = authService.UtilisateurConnecte!;

            // Charger les commandes avec pièces et client pour les ComboBox
            _commandes = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                    .ThenInclude(p => p.Couturier)
                .OrderByDescending(c => c.DateDebut)
                .ToList();

            ChargerRetours();
        }

        private void ChargerRetours()
        {
            GridRetours.ItemsSource = null;
            GridRetours.ItemsSource = _retourService.ObtenirTous();
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string motCle = TxtRecherche.Text.Trim();
            if (string.IsNullOrEmpty(motCle))
                ChargerRetours();
            else
                GridRetours.ItemsSource = _retourService.Rechercher(motCle);
        }

        private void GridRetours_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Pourrait afficher les détails dans un panneau latéral (futur)
        }

        private void BtnNouveauRetour_Click(object sender, RoutedEventArgs e)
        {
            OuvrirFenetreNouveauRetour();
        }

        private void GridRetours_MouseDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (GridRetours.SelectedItem is not Retour retour) return;

            if (retour.Statut == "Signale")
            {
                var r = MessageBox.Show(
                    $"Faire passer ce retour en 'En reprise' ?",
                    "Changement de statut", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;

                try
                {
                    _retourService.DemarrerReprise(retour.IdRetour, _utilisateur.IdEmploye,
                        _utilisateur.Prenom + " " + _utilisateur.Nom);
                    ChargerRetours();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else if (retour.Statut == "En reprise")
            {
                var r = MessageBox.Show(
                    "Marquer ce retour comme 'Résolu' ?",
                    "Changement de statut", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;

                try
                {
                    _retourService.Resoudre(retour.IdRetour, _utilisateur.IdEmploye,
                        _utilisateur.Prenom + " " + _utilisateur.Nom);
                    ChargerRetours();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur : " + ex.Message, "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            else
            {
                MessageBox.Show("Ce retour est déjà résolu.", "Information",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        // ------------------------------------------------------------------
        // Fenêtre de création de retour
        // ------------------------------------------------------------------
        public void OuvrirFenetreNouveauRetour()
        {
            // Filtrer les commandes qui ont au moins une pièce livrée
            var commandesEligibles = _commandes
                .Where(c => c.Pieces.Any(p => p.Statut == "Livree"))
                .ToList();

            if (commandesEligibles.Count == 0)
            {
                MessageBox.Show(
                    "Aucune commande livrée éligible pour un retour.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var fenetre = new Window
            {
                Title = "Nouveau Retour",
                Width = 520,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = SystemColors.WindowBrush,
                Owner = Window.GetWindow(this)
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            // --- Titre ---
            panel.Children.Add(new TextBlock
            {
                Text = "Enregistrer un retour",
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            // --- ComboBox Commande ---
            panel.Children.Add(new TextBlock
            {
                Text = "Commande *",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var cmbCommande = new ComboBox
            {
                Height = 36,
                FontSize = 13,
                DisplayMemberPath = "DisplayText",
                Margin = new Thickness(0, 0, 0, 12)
            };
            cmbCommande.ItemsSource = commandesEligibles.Select(c => new
            {
                c.IdCommande,
                DisplayText = $"CMD-{c.IdCommande} — {c.Client?.Nom} {c.Client?.Prenom} ({c.DateDebut:dd/MM/yyyy})"
            }).ToList();
            cmbCommande.SelectedValuePath = "IdCommande";
            panel.Children.Add(cmbCommande);

            // --- ComboBox Pièce ---
            panel.Children.Add(new TextBlock
            {
                Text = "Piece concernee *",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var cmbPiece = new ComboBox
            {
                Height = 36,
                FontSize = 13,
                DisplayMemberPath = "DisplayText",
                Margin = new Thickness(0, 0, 0, 12)
            };
            cmbPiece.SelectedValuePath = "IdPieceCommande";
            panel.Children.Add(cmbPiece);

            // Charger les pièces quand on choisit une commande
            cmbCommande.SelectionChanged += (s, ev) =>
            {
                cmbPiece.ItemsSource = null;
                if (cmbCommande.SelectedValue == null) return;
                int idCmd = (int)cmbCommande.SelectedValue;
                var cmd = commandesEligibles.First(c => c.IdCommande == idCmd);
                cmbPiece.ItemsSource = cmd.Pieces
                    .Where(p => p.Statut == "Livree")
                    .Select(p => new
                    {
                        p.IdPieceCommande,
                        DisplayText = $"{p.TypeVetement} — {p.Couturier?.Prenom} {p.Couturier?.Nom}"
                    }).ToList();
            };

            // --- Couturier (pré-rempli depuis la pièce) ---
            panel.Children.Add(new TextBlock
            {
                Text = "Couturier responsable",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var txtCouturier = new TextBox
            {
                IsReadOnly = true,
                Height = 36,
                FontSize = 13,
                Background = System.Windows.Media.Brushes.LightGray,
                Text = "(selectionnez une piece)",
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(txtCouturier);

            int? idCouturierPiece = null;

            cmbPiece.SelectionChanged += (s, ev) =>
            {
                txtCouturier.Text = "(selectionnez une piece)";
                idCouturierPiece = null;
                if (cmbPiece.SelectedValue == null) return;
                int idPiece = (int)cmbPiece.SelectedValue;
                var cmd = commandesEligibles.First(c => c.IdCommande == (int)cmbCommande.SelectedValue);
                var piece = cmd.Pieces.First(p => p.IdPieceCommande == idPiece);
                if (piece.Couturier != null)
                {
                    txtCouturier.Text = $"{piece.Couturier.Prenom} {piece.Couturier.Nom}";
                    idCouturierPiece = piece.IdCouturier;
                }
            };

            // --- Description du problème ---
            panel.Children.Add(new TextBlock
            {
                Text = "Description du probleme *",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var txtDescription = new TextBox
            {
                Height = 80,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                AcceptsReturn = true,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Margin = new Thickness(0, 0, 0, 16)
            };
            panel.Children.Add(txtDescription);

            // --- Boutons ---
            var panelBoutons = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var btnAnnuler = new Button
            {
                Content = "Annuler",
                Width = 100,
                Height = 38,
                FontSize = 13,
                Margin = new Thickness(0, 0, 10, 0)
            };
            btnAnnuler.Click += (s, ev) => fenetre.Close();

            var btnEnregistrer = new Button
            {
                Content = "Enregistrer",
                Width = 130,
                Height = 38,
                FontSize = 13,
                FontWeight = FontWeights.Bold,
                Foreground = System.Windows.Media.Brushes.White,
                Background = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x05, 0x96, 0x69)),
                BorderThickness = new Thickness(0)
            };
            btnEnregistrer.Click += (s, ev) =>
            {
                // Validations
                if (cmbCommande.SelectedValue == null)
                {
                    MessageBox.Show("Selectionnez une commande.", "Champ manquant",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (cmbPiece.SelectedValue == null)
                {
                    MessageBox.Show("Selectionnez une piece.", "Champ manquant",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(txtDescription.Text))
                {
                    MessageBox.Show("Decrivez le probleme.", "Champ manquant",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (idCouturierPiece == null)
                {
                    MessageBox.Show("La piece selectionnee n'a pas de couturier.",
                        "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var retour = new Retour
                    {
                        IdCommande = (int)cmbCommande.SelectedValue,
                        IdPieceCommande = (int)cmbPiece.SelectedValue,
                        IdCouturier = idCouturierPiece.Value,
                        DescriptionProbleme = txtDescription.Text.Trim(),
                        Statut = "Signale",
                        IdOperateurEnregistrement = _utilisateur.IdEmploye,
                        NomOperateurEnregistrement = _utilisateur.Prenom + " " + _utilisateur.Nom
                    };

                    _retourService.Ajouter(retour);
                    ChargerRetours();
                    fenetre.DialogResult = true;
                    fenetre.Close();
                    MessageBox.Show("Retour enregistre avec succes !", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur : " + ex.Message, "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            panelBoutons.Children.Add(btnAnnuler);
            panelBoutons.Children.Add(btnEnregistrer);
            panel.Children.Add(panelBoutons);

            fenetre.Content = panel;
            fenetre.ShowDialog();
        }
    }
}