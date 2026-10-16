using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Data;
using System.Linq;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System;
using System.Text;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Étape 2-ter : Journal d'audit filtré — lecture seule, Boss uniquement.
    /// Fenêtre construite entièrement en C# (pas de XAML).
    /// Filtres : période, utilisateur, type d'action, numéro de commande, recherche texte.
    /// Tri par date décroissante, pagination 50 lignes, affichage des valeurs avant/après
    /// et du motif. Export CSV simple.
    /// </summary>
    public class JournalAuditWindow : Window
    {
        private readonly IAuditService _auditService;
        private readonly DataGrid _grid;
        private readonly TextBox _txtRecherche;
        private readonly ComboBox _cmbUtilisateur;
        private readonly ComboBox _cmbTypeAction;
        private readonly TextBox _txtNumeroCommande;
        private readonly DatePicker _dpDebut;
        private readonly DatePicker _dpFin;
        private readonly TextBlock _txtPagination;
        private readonly Button _btnPrecedent;
        private readonly Button _btnSuivant;
        private readonly Button _btnExportCsv;

        private int _page = 1;
        private const int PAGE_SIZE = 50;
        private List<JournalAudit> _toutesLesEntrees = new();
        private List<JournalAudit> _entreesFiltrées = new();

        // Constructeur avec utilisateur pré-filtré (depuis Dashboard)
        public JournalAuditWindow(int idOperateur, string nomOperateur)
            : this()
        {
            // Pré-sélectionner l'utilisateur
            _cmbUtilisateur.SelectedValue = idOperateur;
            Title = $"Journal d'audit — {nomOperateur}";
        }

        // Constructeur principal
        public JournalAuditWindow()
        {
            // Vérifier que l'utilisateur est Boss
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            _auditService = App.Services.GetRequiredService<IAuditService>();

            Title = "Journal d'audit — Contrôle des actions";
            Width = 1200;
            Height = 700;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(0xF9, 0xFA, 0xFB));

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // En-tête
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Filtres
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // DataGrid
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });  // Pagination

            // ── EN-TÊTE ────────────────────────────────────────────────────
            var headerBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 16, 24, 16)
            };
            var headerStack = new StackPanel();
            var titlePanel = new StackPanel { Orientation = Orientation.Horizontal };
            titlePanel.Children.Add(new TextBlock
            {
                Text = "📜",
                FontSize = 22,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 10, 0)
            });
            titlePanel.Children.Add(new TextBlock
            {
                Text = "Journal d'audit",
                FontSize = 20,
                FontWeight = FontWeights.Bold,
                Foreground = new SolidColorBrush(Color.FromRgb(0x0F, 0x17, 0x2A)),
                VerticalAlignment = VerticalAlignment.Center
            });
            headerStack.Children.Add(titlePanel);
            headerStack.Children.Add(new TextBlock
            {
                Text = "Historique immuable des actions sensibles sur la période sélectionnée",
                FontSize = 12,
                Foreground = new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)),
                Margin = new Thickness(0, 4, 0, 0)
            });
            headerBorder.Child = headerStack;
            Grid.SetRow(headerBorder, 0);
            grid.Children.Add(headerBorder);

            // ── FILTRES ────────────────────────────────────────────────────
            var filtresBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0xF8, 0xFA, 0xFC)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(24, 16, 24, 16)
            };
            var filtresGrid = new Grid();
            filtresGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            filtresGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            filtresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filtresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filtresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            filtresGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Ligne 1 : Recherche, Utilisateur, Type action, N° commande
            _txtRecherche = CreerTextBox("Recherche texte...");
            _txtRecherche.TextChanged += Filtrer;
            var sp1 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            sp1.Children.Add(new TextBlock { Text = "Recherche", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            sp1.Children.Add(_txtRecherche);
            Grid.SetRow(sp1, 0);
            Grid.SetColumn(sp1, 0);
            filtresGrid.Children.Add(sp1);

            _cmbUtilisateur = CreerComboBox();
            _cmbUtilisateur.SelectionChanged += Filtrer;
            var sp2 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            sp2.Children.Add(new TextBlock { Text = "Utilisateur", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            sp2.Children.Add(_cmbUtilisateur);
            Grid.SetRow(sp2, 0);
            Grid.SetColumn(sp2, 1);
            filtresGrid.Children.Add(sp2);

            _cmbTypeAction = CreerComboBox();
            _cmbTypeAction.SelectionChanged += Filtrer;
            var sp3 = new StackPanel { Margin = new Thickness(0, 0, 8, 0) };
            sp3.Children.Add(new TextBlock { Text = "Type d'action", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            sp3.Children.Add(_cmbTypeAction);
            Grid.SetRow(sp3, 0);
            Grid.SetColumn(sp3, 2);
            filtresGrid.Children.Add(sp3);

            _txtNumeroCommande = CreerTextBox("N° commande");
            _txtNumeroCommande.TextChanged += Filtrer;
            var sp4 = new StackPanel { Margin = new Thickness(0, 0, 0, 0) };
            sp4.Children.Add(new TextBlock { Text = "N° commande", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            sp4.Children.Add(_txtNumeroCommande);
            Grid.SetRow(sp4, 0);
            Grid.SetColumn(sp4, 3);
            filtresGrid.Children.Add(sp4);

            // Ligne 2 : Période + Bouton Export
            _dpDebut = CreerDatePicker();
            _dpDebut.SelectedDate = DateTime.Today.AddDays(-30);
            _dpDebut.SelectedDateChanged += Filtrer;
            var sp5 = new StackPanel { Margin = new Thickness(0, 8, 8, 0) };
            sp5.Children.Add(new TextBlock { Text = "Date début", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            sp5.Children.Add(_dpDebut);
            Grid.SetRow(sp5, 1);
            Grid.SetColumn(sp5, 0);
            filtresGrid.Children.Add(sp5);

            _dpFin = CreerDatePicker();
            _dpFin.SelectedDate = DateTime.Today;
            _dpFin.SelectedDateChanged += Filtrer;
            var sp6 = new StackPanel { Margin = new Thickness(0, 8, 8, 0) };
            sp6.Children.Add(new TextBlock { Text = "Date fin", FontSize = 11, Foreground = Brushes.Gray, Margin = new Thickness(0, 0, 0, 4) });
            sp6.Children.Add(_dpFin);
            Grid.SetRow(sp6, 1);
            Grid.SetColumn(sp6, 1);
            filtresGrid.Children.Add(sp6);

            _btnExportCsv = new Button
            {
                Content = "📥  Exporter CSV",
                Height = 34,
                Padding = new Thickness(16, 0, 16, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x05, 0x96, 0x69)),
                Foreground = Brushes.White,
                BorderThickness = new Thickness(0),
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 8, 0, 0)
            };
            _btnExportCsv.Click += BtnExportCsv_Click;
            Grid.SetRow(_btnExportCsv, 1);
            Grid.SetColumn(_btnExportCsv, 3);
            filtresGrid.Children.Add(_btnExportCsv);

            filtresBorder.Child = filtresGrid;
            Grid.SetRow(filtresBorder, 1);
            grid.Children.Add(filtresBorder);

            // ── DATAGRID ───────────────────────────────────────────────────
            _grid = new DataGrid
            {
                AutoGenerateColumns = false,
                IsReadOnly = true,
                CanUserAddRows = false,
                CanUserDeleteRows = false,
                CanUserReorderColumns = false,
                CanUserResizeRows = false,
                HeadersVisibility = DataGridHeadersVisibility.Column,
                GridLinesVisibility = DataGridGridLinesVisibility.None,
                SelectionMode = DataGridSelectionMode.Single,
                RowHeight = 36,
                FontSize = 12,
                Background = Brushes.White,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(24, 0, 24, 0)
            };

            // Colonnes
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Date/Heure",
                Binding = new Binding("DateHeureLocale") { StringFormat = "dd/MM/yyyy HH:mm:ss" },
                Width = 140
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Opérateur",
                Binding = new Binding("NomOperateur"),
                Width = 150
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Rôle",
                Binding = new Binding("RoleOperateur"),
                Width = 80
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Action",
                Binding = new Binding("ActionAffichee"),
                Width = 180
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Entité",
                Binding = new Binding("Entite"),
                Width = 100
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "ID",
                Binding = new Binding("IdEntite"),
                Width = 60
            });
            _grid.Columns.Add(new DataGridTextColumn
            {
                Header = "Détails",
                Binding = new Binding("DetailsAffichage"),
                Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                ElementStyle = new Style(typeof(TextBlock))
                {
                    Setters =
                    {
                        new Setter(TextBlock.TextWrappingProperty, TextWrapping.Wrap),
                        new Setter(TextBlock.ForegroundProperty, new SolidColorBrush(Color.FromRgb(0x64, 0x74, 0x8B)))
                    }
                }
            });

            var scrollBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Child = _grid
            };
            Grid.SetRow(scrollBorder, 2);
            grid.Children.Add(scrollBorder);

            // ── PAGINATION ─────────────────────────────────────────────────
            var paginationBorder = new Border
            {
                Background = Brushes.White,
                BorderBrush = new SolidColorBrush(Color.FromRgb(0xE5, 0xE7, 0xEB)),
                BorderThickness = new Thickness(0, 1, 0, 0),
                Padding = new Thickness(24, 12, 24, 12)
            };
            var paginationStack = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
            _btnPrecedent = new Button { Content = "← Précédent", Width = 100, Height = 32, Margin = new Thickness(0, 0, 12, 0) };
            _btnPrecedent.Click += BtnPrecedent_Click;
            _txtPagination = new TextBlock { VerticalAlignment = VerticalAlignment.Center, FontSize = 13, Margin = new Thickness(0, 0, 12, 0) };
            _btnSuivant = new Button { Content = "Suivant →", Width = 100, Height = 32 };
            _btnSuivant.Click += BtnSuivant_Click;
            paginationStack.Children.Add(_btnPrecedent);
            paginationStack.Children.Add(_txtPagination);
            paginationStack.Children.Add(_btnSuivant);
            paginationBorder.Child = paginationStack;
            Grid.SetRow(paginationBorder, 3);
            grid.Children.Add(paginationBorder);

            Content = grid;

            Loaded += (s, e) => ChargerDonnees();
        }

        private void ChargerDonnees()
        {
            // Charger toutes les entrées d'audit
            _toutesLesEntrees = _auditService.ObtenirToutesLesEntrees();

            // Peupler les combos
            var utilisateurs = _toutesLesEntrees
                .Select(entry => new { entry.IdOperateur, entry.NomOperateur })
                .Distinct()
                .OrderBy(u => u.NomOperateur)
                .ToList();
            _cmbUtilisateur.Items.Add(new ComboBoxItem { Content = "Tous", Tag = 0 });
            foreach (var u in utilisateurs)
                _cmbUtilisateur.Items.Add(new ComboBoxItem { Content = u.NomOperateur, Tag = u.IdOperateur });
            _cmbUtilisateur.SelectedIndex = 0;

            var actions = _toutesLesEntrees
                .Select(entry => entry.TypeAction)
                .Distinct()
                .OrderBy(a => a)
                .ToList();
            _cmbTypeAction.Items.Add(new ComboBoxItem { Content = "Toutes", Tag = null });
            foreach (var a in actions)
                _cmbTypeAction.Items.Add(new ComboBoxItem { Content = a, Tag = a });
            _cmbTypeAction.SelectedIndex = 0;

            Filtrer(null, null);
        }

        private void Filtrer(object? sender, EventArgs? eventArgs)
        {
            var debut = _dpDebut.SelectedDate ?? DateTime.Today.AddYears(-1);
            var fin = _dpFin.SelectedDate ?? DateTime.Today;
            var recherche = _txtRecherche.Text.Trim().ToLowerInvariant();
            var idUtilisateur = _cmbUtilisateur.SelectedItem is ComboBoxItem u ? (int)u.Tag : 0;
            var typeAction = _cmbTypeAction.SelectedItem is ComboBoxItem t ? (string?)t.Tag : null;
            var numCommande = _txtNumeroCommande.Text.Trim();

            _entreesFiltrées = _toutesLesEntrees
                .Where(e => e.DateHeureUtc.ToLocalTime().Date >= debut.Date && e.DateHeureUtc.ToLocalTime().Date <= fin.Date)
                .Where(e => idUtilisateur == 0 || e.IdOperateur == idUtilisateur)
                .Where(e => typeAction == null || e.TypeAction == typeAction)
                .Where(e => string.IsNullOrEmpty(numCommande) ||
                           (e.Entite == "Commande" && e.IdEntite.ToString() == numCommande))
                .Where(e => string.IsNullOrEmpty(recherche) ||
                           e.NomOperateur.ToLowerInvariant().Contains(recherche) ||
                           e.TypeAction.ToLowerInvariant().Contains(recherche) ||
                           (e.Motif ?? "").ToLowerInvariant().Contains(recherche))
                .OrderByDescending(e => e.DateHeureUtc)
                .ToList();

            _page = 1;
            AfficherPage();
        }

        private void AfficherPage()
        {
            var page = _entreesFiltrées
                .Skip((_page - 1) * PAGE_SIZE)
                .Take(PAGE_SIZE)
                .Select(e => new JournalAuditVm
                {
                    DateHeureLocale = e.DateHeureUtc.ToLocalTime(),
                    NomOperateur = e.NomOperateur,
                    RoleOperateur = e.RoleOperateur,
                    ActionAffichee = e.ActionAffichee,
                    Entite = e.Entite,
                    IdEntite = e.IdEntite,
                    DetailsAffichage = ConstruireDetailsAffichage(e)
                })
                .ToList();

            _grid.ItemsSource = page;

            int totalPages = (int)Math.Ceiling((double)_entreesFiltrées.Count / PAGE_SIZE);
            _txtPagination.Text = $"Page {_page} / {Math.Max(1, totalPages)}  ({_entreesFiltrées.Count} entrées)";
            _btnPrecedent.IsEnabled = _page > 1;
            _btnSuivant.IsEnabled = _page < totalPages;
        }

        private string ConstruireDetailsAffichage(JournalAudit e)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(e.Motif))
                sb.Append($"Motif : {e.Motif}  ·  ");
            if (!string.IsNullOrWhiteSpace(e.ValeursAvant))
                sb.Append($"Avant : {e.ValeursAvant}  ·  ");
            if (!string.IsNullOrWhiteSpace(e.ValeursApres))
                sb.Append($"Après : {e.ValeursApres}");
            return sb.ToString().TrimEnd(' ', '·');
        }

        private void BtnPrecedent_Click(object sender, RoutedEventArgs e)
        {
            if (_page > 1) { _page--; AfficherPage(); }
        }

        private void BtnSuivant_Click(object sender, RoutedEventArgs e)
        {
            int totalPages = (int)Math.Ceiling((double)_entreesFiltrées.Count / PAGE_SIZE);
            if (_page < totalPages) { _page++; AfficherPage(); }
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new Microsoft.Win32.SaveFileDialog
                {
                    FileName = $"audit_{DateTime.Now:yyyyMMdd_HHmmss}.csv",
                    DefaultExt = ".csv",
                    Filter = "CSV files (*.csv)|*.csv"
                };

                if (dialog.ShowDialog() != true) return;

                var sb = new StringBuilder();
                sb.AppendLine("Date/Heure;Opérateur;Rôle;Action;Entité;ID;Motif;Avant;Après");
                foreach (var entry in _entreesFiltrées)
                {
                    sb.AppendLine($"{entry.DateHeureUtc.ToLocalTime():dd/MM/yyyy HH:mm:ss};" +
                                  $"{entry.NomOperateur};{entry.RoleOperateur};{entry.ActionAffichee};" +
                                  $"{entry.Entite};{entry.IdEntite};" +
                                  $"\"{entry.Motif ?? ""}\";\"{ entry.ValeursAvant ?? ""}\";\"{ entry.ValeursApres ?? ""}\"");
                }

                System.IO.File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);
                MessageBox.Show($"Export réussi : {dialog.FileName}", "Export CSV", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur export : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private TextBox CreerTextBox(string placeholder)
        {
            return new TextBox
            {
                Height = 34,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center,
                Tag = placeholder
            };
        }

        private ComboBox CreerComboBox()
        {
            return new ComboBox
            {
                Height = 34,
                Padding = new Thickness(10, 0, 10, 0),
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }

        private DatePicker CreerDatePicker()
        {
            return new DatePicker
            {
                Height = 34,
                FontSize = 13,
                VerticalContentAlignment = VerticalAlignment.Center
            };
        }
    }

    // ViewModel pour l'affichage dans le DataGrid
    public class JournalAuditVm
    {
        public DateTime DateHeureLocale { get; set; }
        public string NomOperateur { get; set; } = string.Empty;
        public string RoleOperateur { get; set; } = string.Empty;
        public string ActionAffichee { get; set; } = string.Empty;
        public string Entite { get; set; } = string.Empty;
        public int IdEntite { get; set; }
        public string DetailsAffichage { get; set; } = string.Empty;
    }
}
