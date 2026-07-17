using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class EmployesView : Page
    {
        private readonly ApplicationDbContext _context;
        private Employe? _employeSelectionne;

        public EmployesView()
        {
            // Défense en profondeur : cette vue ne doit être accessible qu'au Boss,
            // indépendamment du fait que MainWindow ait ou non déjà filtré l'accès.
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();
            _context = App.Services.GetRequiredService<ApplicationDbContext>();
            ChargerEmployes();
        }

        // ------------------------------------------------------------------
        // Charger la liste
        // ------------------------------------------------------------------
        private void ChargerEmployes()
        {
            var liste = _context.Employes
                .OrderBy(e => e.Nom)
                .Select(e => new
                {
                    e.IdEmploye,
                    NomComplet = e.Nom + " " + e.Prenom,
                    e.Identifiant,
                    e.Role,
                    e.Statut
                })
                .ToList();

            GridEmployes.ItemsSource = liste;
        }

        // ------------------------------------------------------------------
        // Recherche
        // ------------------------------------------------------------------
        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string terme = TxtRecherche.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(terme))
            {
                ChargerEmployes();
                return;
            }

            var liste = _context.Employes
                .Where(e => e.Nom.ToLower().Contains(terme) ||
                            e.Prenom.ToLower().Contains(terme) ||
                            e.Identifiant.ToLower().Contains(terme))
                .Select(e => new
                {
                    e.IdEmploye,
                    NomComplet = e.Nom + " " + e.Prenom,
                    e.Identifiant,
                    e.Role,
                    e.Statut
                })
                .ToList();

            GridEmployes.ItemsSource = liste;
        }

        // ------------------------------------------------------------------
        // Sélection dans la grille
        // ------------------------------------------------------------------
        private void GridEmployes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridEmployes.SelectedItem == null) return;

            dynamic item = GridEmployes.SelectedItem;
            int id = (int)item.IdEmploye;

            _employeSelectionne = _context.Employes.FirstOrDefault(emp => emp.IdEmploye == id);
            if (_employeSelectionne == null) return;

            TxtNom.Text = _employeSelectionne.Nom;
            TxtPrenom.Text = _employeSelectionne.Prenom;
            TxtIdentifiant.Text = _employeSelectionne.Identifiant;
            TxtMotDePasse.Password = "";

            // Sélectionner le rôle
            for (int i = 0; i < CmbRole.Items.Count; i++)
            {
                if (((ComboBoxItem)CmbRole.Items[i]).Content?.ToString() == _employeSelectionne.Role)
                {
                    CmbRole.SelectedIndex = i;
                    break;
                }
            }

            // Mettre à jour le texte du bouton Suspendre
            BtnSuspendre.Content = _employeSelectionne.Statut == "Actif"
                ? "Suspendre" : "Réactiver";
        }

        // ------------------------------------------------------------------
        // Enregistrer un nouvel employé
        // ------------------------------------------------------------------
        private void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text) ||
                string.IsNullOrWhiteSpace(TxtPrenom.Text) ||
                string.IsNullOrWhiteSpace(TxtIdentifiant.Text) ||
                string.IsNullOrWhiteSpace(TxtMotDePasse.Password))
            {
                TxtMessage.Text = "Tous les champs sont requis.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Vérifier si l'identifiant existe déjà
            if (_context.Employes.Any(emp => emp.Identifiant == TxtIdentifiant.Text.Trim()))
            {
                TxtMessage.Text = "Cet identifiant existe déjà.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (CmbRole.SelectedIndex < 0)
            {
                TxtMessage.Text = "Sélectionnez un rôle.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            var employe = new Employe
            {
                Nom = TxtNom.Text.Trim(),
                Prenom = TxtPrenom.Text.Trim(),
                Identifiant = TxtIdentifiant.Text.Trim(),
                MotDePasse = HashMotDePasse(TxtMotDePasse.Password),
                Role = ((ComboBoxItem)CmbRole.SelectedItem).Content?.ToString() ?? "",
                Statut = "Actif"
            };

            _context.Employes.Add(employe);
            _context.SaveChanges();

            TxtMessage.Text = "Employé ajouté avec succès.";
            TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
            BorderMessage.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(236, 253, 245));
            BorderMessage.Visibility = System.Windows.Visibility.Visible;
            ChargerEmployes();
            ViderFormulaire();
        }

        // ------------------------------------------------------------------
        // Modifier un employé
        // ------------------------------------------------------------------
        private void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (_employeSelectionne == null)
            {
                TxtMessage.Text = "Sélectionnez un employé à modifier.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (CmbRole.SelectedIndex < 0)
            {
                TxtMessage.Text = "Sélectionnez un rôle.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            _employeSelectionne.Nom = TxtNom.Text.Trim();
            _employeSelectionne.Prenom = TxtPrenom.Text.Trim();
            _employeSelectionne.Identifiant = TxtIdentifiant.Text.Trim();
            _employeSelectionne.Role = ((ComboBoxItem)CmbRole.SelectedItem).Content?.ToString() ?? "";

            // Mettre à jour le mot de passe seulement si saisi
            if (!string.IsNullOrWhiteSpace(TxtMotDePasse.Password))
            {
                _employeSelectionne.MotDePasse = HashMotDePasse(TxtMotDePasse.Password);
            }

            _context.SaveChanges();

            TxtMessage.Text = "Employé modifié avec succès.";
            TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
            BorderMessage.Background = new System.Windows.Media.SolidColorBrush(
                System.Windows.Media.Color.FromRgb(236, 253, 245));
            BorderMessage.Visibility = System.Windows.Visibility.Visible;
            ChargerEmployes();
            ViderFormulaire();
        }

        // ------------------------------------------------------------------
        // Suspendre ou Réactiver un employé
        // ------------------------------------------------------------------
        private void BtnSuspendre_Click(object sender, RoutedEventArgs e)
        {
            if (_employeSelectionne == null)
            {
                TxtMessage.Text = "Sélectionnez un employé.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            // Ne pas suspendre le Boss
            if (_employeSelectionne.Role == "Boss")
            {
                TxtMessage.Text = "Impossible de suspendre le Boss.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (_employeSelectionne.Statut == "Actif")
            {
                _employeSelectionne.Statut = "Suspendu";
                TxtMessage.Text = _employeSelectionne.Prenom + " " + _employeSelectionne.Nom + " suspendu.";
            }
            else
            {
                _employeSelectionne.Statut = "Actif";
                TxtMessage.Text = _employeSelectionne.Prenom + " " + _employeSelectionne.Nom + " réactivé.";
            }

            TxtMessage.Foreground = System.Windows.Media.Brushes.Orange;
            _context.SaveChanges();
            ChargerEmployes();
            ViderFormulaire();
        }

        // ------------------------------------------------------------------
        // Vider le formulaire
        // ------------------------------------------------------------------
        private void BtnVider_Click(object sender, RoutedEventArgs e)
        {
            ViderFormulaire();
        }

        private void ViderFormulaire()
        {
            _employeSelectionne = null;
            TxtNom.Clear();
            TxtPrenom.Clear();
            TxtIdentifiant.Clear();
            TxtMotDePasse.Clear();
            CmbRole.SelectedIndex = -1;
            BtnSuspendre.Content = "Suspendre";
            TxtMessage.Text = string.Empty;
            BorderMessage.Visibility = System.Windows.Visibility.Collapsed;
            GridEmployes.SelectedItem = null;
        }

        // ------------------------------------------------------------------
        // Hachage sécurisé (PBKDF2 + sel) — logique centralisée dans Helpers/PasswordHasher.cs
        // ------------------------------------------------------------------
        private static string HashMotDePasse(string motDePasse)
            => GestionCoutureApp.Helpers.PasswordHasher.Hasher(motDePasse);
    }
}