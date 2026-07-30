using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class MaterielsView : Page
    {
        private readonly IMaterielService _materielService;
        private readonly ApplicationDbContext _context;
        private List<MaterielSupplement> _tousLesMateriels;
        private List<Commande> _commandes;

        public MaterielsView()
        {
            InitializeComponent();

            _materielService = App.Services.GetRequiredService<IMaterielService>();
            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

            _commandes = _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                .OrderByDescending(c => c.IdCommande)
                .ToList();

            CmbCommande.Items.Add("(Toutes les commandes)");
            foreach (var cmd in _commandes)
            {
                CmbCommande.Items.Add(new
                {
                    cmd.IdCommande,
                    DisplayText = $"CMD-{cmd.IdCommande} - {cmd.Client?.Nom} {cmd.Client?.Prenom}"
                });
            }
            CmbCommande.SelectedIndex = 0;

            ChargerMateriels();
        }

        private void ChargerMateriels()
        {
            _tousLesMateriels = _materielService.ObtenirTous();
            AppliquerFiltresLocaux();
        }

        private void AppliquerFiltresLocaux()
        {
            if (_tousLesMateriels == null) return;

            var filtres = _tousLesMateriels.AsEnumerable();

            if (CmbCommande.SelectedIndex > 0)
            {
                var selected = CmbCommande.SelectedItem;
                if (selected is not string)
                {
                    var id = (int)selected.GetType().GetProperty("IdCommande")!.GetValue(selected)!;
                    filtres = filtres.Where(m => m.IdCommande == id);

                    decimal total = _materielService.TotalParCommande(id);
                    TxtTotal.Text = $"Total materiaux : {total:N0} FCFA";
                }
                else
                {
                    TxtTotal.Text = "";
                }
            }
            else
            {
                decimal grandTotal = _tousLesMateriels.Sum(m => m.Montant);
                TxtTotal.Text = $"Total general : {grandTotal:N0} FCFA";
            }

            string motCle = TxtRecherche.Text.Trim().ToLower();
            if (!string.IsNullOrEmpty(motCle))
            {
                filtres = filtres.Where(m =>
                    m.Designation.ToLower().Contains(motCle) ||
                    (m.PieceCommande?.TypeVetement ?? "").ToLower().Contains(motCle) ||
                    (m.Commande?.Client?.Nom ?? "").ToLower().Contains(motCle));
            }

            GridMateriels.ItemsSource = filtres.ToList();
        }

        private void CmbCommande_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            AppliquerFiltresLocaux();
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            AppliquerFiltresLocaux();
        }

        private void BtnAjouter_Click(object sender, RoutedEventArgs e)
        {
            OuvrirFenetreMateriel(null);
        }

        private void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (GridMateriels.SelectedItem is not MaterielSupplement materiel)
            {
                MessageBox.Show("Selectionnez un materiel a modifier.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            OuvrirFenetreMateriel(materiel);
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (GridMateriels.SelectedItem is not MaterielSupplement materiel)
            {
                MessageBox.Show("Selectionnez un materiel a supprimer.",
                    "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var result = MessageBox.Show(
                $"Supprimer {materiel.Designation} ({materiel.MontantAffiche}) ?",
                "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Question);

            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    _materielService.Supprimer(materiel.IdMateriel);
                    ChargerMateriels();
                    MessageBox.Show("Materiel supprime.", "Succes",
                        MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Erreur : " + ex.Message, "Erreur",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OuvrirFenetreMateriel(MaterielSupplement? existant)
        {
            bool estModification = existant != null;
            string titre = estModification ? "Modifier le materiel" : "Nouveau materiel";

            var fenetre = new Window
            {
                Title = titre,
                Width = 520,
                Height = 480,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                Background = SystemColors.WindowBrush,
                Owner = Window.GetWindow(this)
            };

            var panel = new StackPanel { Margin = new Thickness(24, 20, 24, 20) };

            panel.Children.Add(new TextBlock
            {
                Text = titre,
                FontSize = 18,
                FontWeight = FontWeights.Bold,
                Foreground = new System.Windows.Media.SolidColorBrush(
                    System.Windows.Media.Color.FromRgb(0x0F, 0x17, 0x2A)),
                Margin = new Thickness(0, 0, 0, 16)
            });

            // ComboBox Commande
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

            var commandesDisponibles = _commandes.Select(c => new
            {
                c.IdCommande,
                DisplayText = $"CMD-{c.IdCommande} - {c.Client?.Nom} {c.Client?.Prenom}"
            }).ToList();

            cmbCommande.ItemsSource = commandesDisponibles;
            cmbCommande.SelectedValuePath = "IdCommande";
            panel.Children.Add(cmbCommande);

            // ComboBox Piece
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

            cmbCommande.SelectionChanged += (s, ev) =>
            {
                cmbPiece.ItemsSource = null;
                if (cmbCommande.SelectedValue == null) return;
                int idCmd = (int)cmbCommande.SelectedValue;
                var cmd = _commandes.First(c => c.IdCommande == idCmd);
                cmbPiece.ItemsSource = cmd.Pieces.Select(p => new
                {
                    p.IdPieceCommande,
                    DisplayText = p.TypeVetement
                }).ToList();

                if (cmd.Pieces.Count > 0)
                    cmbPiece.SelectedIndex = 0;
            };

            // Designation
            panel.Children.Add(new TextBlock
            {
                Text = "Designation du materiel *",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });

            var txtDesignation = new TextBox
            {
                Height = 36,
                FontSize = 13,
                Text = estModification ? existant.Designation : "",
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(txtDesignation);

            // Quantite + Prix
            var gridQuantite = new Grid { Margin = new Thickness(0, 0, 0, 16) };
            gridQuantite.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            gridQuantite.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(12) });
            gridQuantite.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var stackQte = new StackPanel();
            stackQte.Children.Add(new TextBlock
            {
                Text = "Quantite *",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var txtQuantite = new TextBox
            {
                Height = 36,
                FontSize = 13,
                Text = estModification ? existant.Quantite.ToString() : "1"
            };
            stackQte.Children.Add(txtQuantite);
            Grid.SetColumn(stackQte, 0);
            gridQuantite.Children.Add(stackQte);

            var stackPrix = new StackPanel();
            stackPrix.Children.Add(new TextBlock
            {
                Text = "Prix unitaire (FCFA) *",
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var txtPrixUnitaire = new TextBox
            {
                Height = 36,
                FontSize = 13,
                Text = estModification ? existant.PrixUnitaire.ToString() : ""
            };
            stackPrix.Children.Add(txtPrixUnitaire);
            Grid.SetColumn(stackPrix, 2);
            gridQuantite.Children.Add(stackPrix);

            panel.Children.Add(gridQuantite);

            if (estModification)
            {
                cmbCommande.SelectedValue = existant.IdCommande;
            }

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
                if (cmbCommande.SelectedValue == null)
                {
                    MessageBox.Show("Selectionnez une commande.",
                        "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (cmbPiece.SelectedValue == null)
                {
                    MessageBox.Show("Selectionnez une piece.",
                        "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (string.IsNullOrWhiteSpace(txtDesignation.Text))
                {
                    MessageBox.Show("Renseignez la designation.",
                        "Champ manquant", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!int.TryParse(txtQuantite.Text.Trim(), out int quantite) || quantite <= 0)
                {
                    MessageBox.Show("Quantite invalide.",
                        "Champ invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                if (!decimal.TryParse(txtPrixUnitaire.Text.Trim(), out decimal prix) || prix < 0)
                {
                    MessageBox.Show("Prix unitaire invalide.",
                        "Champ invalide", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    if (estModification)
                    {
                        existant.IdCommande = (int)cmbCommande.SelectedValue;
                        existant.IdPieceCommande = (int)cmbPiece.SelectedValue;
                        existant.Designation = txtDesignation.Text.Trim();
                        existant.Quantite = quantite;
                        existant.PrixUnitaire = prix;

                        _materielService.Modifier(existant);
                    }
                    else
                    {
                        var nouveau = new MaterielSupplement
                        {
                            IdCommande = (int)cmbCommande.SelectedValue,
                            IdPieceCommande = (int)cmbPiece.SelectedValue,
                            Designation = txtDesignation.Text.Trim(),
                            Quantite = quantite,
                            PrixUnitaire = prix
                        };

                        _materielService.Ajouter(nouveau);
                    }

                    ChargerMateriels();
                    fenetre.DialogResult = true;
                    fenetre.Close();
                    MessageBox.Show(
                        estModification ? "Materiel modifie." : "Materiel ajoute.",
                        "Succes", MessageBoxButton.OK, MessageBoxImage.Information);
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