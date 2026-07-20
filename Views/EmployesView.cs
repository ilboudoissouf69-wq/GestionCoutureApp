using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
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

            // Contexte propre à cet écran, cree via la factory et libere quand
            // on quitte l'ecran (voir Unloaded ci-dessous). Corrige l'ancien
            // DbContext partage "Singleton" sur toute l'application.
            var contextFactory = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _context = contextFactory.CreateDbContext();
            Unloaded += (s, e) => _context.Dispose();

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

            GridEmployes.ItemsSource = null;
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

            GridEmployes.ItemsSource = null;
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
                AfficherMessage("Tous les champs sont requis.", succes: false);
                return;
            }

            // CORRECTIF (securite) : aucune longueur minimale n'etait imposee
            // au mot de passe d'un employe, ce qui rend le hachage PBKDF2
            // (voir Helpers/PasswordHasher.cs) quasi inutile face a une
            // simple attaque par force brute sur un mot de passe trivial.
            if (TxtMotDePasse.Password.Length < 6)
            {
                AfficherMessage("Le mot de passe doit contenir au moins 6 caracteres.", succes: false);
                return;
            }

            // Verifier si l'identifiant existe deja
            if (_context.Employes.Any(emp => emp.Identifiant == TxtIdentifiant.Text.Trim()))
            {
                AfficherMessage("Cet identifiant existe déjà.", succes: false);
                return;
            }

            if (CmbRole.SelectedIndex < 0)
            {
                AfficherMessage("Sélectionnez un rôle.", succes: false);
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

            try
            {
                _context.Employes.Add(employe);
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                // Filet de securite : meme si la verification applicative ci-dessus
                // n'a rien trouve (course rare), l'index unique en base (voir
                // ApplicationDbContext) refusera un identifiant en double.
                AfficherMessage("Impossible d'enregistrer : " + (ex.InnerException?.Message ?? ex.Message), succes: false);
                return;
            }

            AfficherMessage("Employé ajouté avec succès.", succes: true);
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
                AfficherMessage("Sélectionnez un employé à modifier.", succes: false);
                return;
            }

            if (CmbRole.SelectedIndex < 0)
            {
                AfficherMessage("Sélectionnez un rôle.", succes: false);
                return;
            }

            if (string.IsNullOrWhiteSpace(TxtNom.Text) ||
                string.IsNullOrWhiteSpace(TxtPrenom.Text) ||
                string.IsNullOrWhiteSpace(TxtIdentifiant.Text))
            {
                AfficherMessage("Le nom, le prénom et l'identifiant sont requis.", succes: false);
                return;
            }

            // Un autre employe (different de celui en cours de modification)
            // ne doit pas deja utiliser cet identifiant.
            if (_context.Employes.Any(emp => emp.Identifiant == TxtIdentifiant.Text.Trim()
                                           && emp.IdEmploye != _employeSelectionne.IdEmploye))
            {
                AfficherMessage("Cet identifiant est déjà utilisé par un autre employé.", succes: false);
                return;
            }

            _employeSelectionne.Nom = TxtNom.Text.Trim();
            _employeSelectionne.Prenom = TxtPrenom.Text.Trim();
            _employeSelectionne.Identifiant = TxtIdentifiant.Text.Trim();
            _employeSelectionne.Role = ((ComboBoxItem)CmbRole.SelectedItem).Content?.ToString() ?? "";

            // Mettre a jour le mot de passe seulement si saisi
            if (!string.IsNullOrWhiteSpace(TxtMotDePasse.Password))
            {
                // CORRECTIF (securite) : meme controle de longueur minimale
                // qu'a la creation (voir BtnEnregistrer_Click).
                if (TxtMotDePasse.Password.Length < 6)
                {
                    AfficherMessage("Le mot de passe doit contenir au moins 6 caracteres.", succes: false);
                    return;
                }
                _employeSelectionne.MotDePasse = HashMotDePasse(TxtMotDePasse.Password);
            }

            try
            {
                _context.SaveChanges();
            }
            catch (DbUpdateException ex)
            {
                AfficherMessage("Impossible d'enregistrer : " + (ex.InnerException?.Message ?? ex.Message), succes: false);
                return;
            }

            AfficherMessage("Employé modifié avec succès.", succes: true);
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
                AfficherMessage("Sélectionnez un employé.", succes: false);
                return;
            }

            // Ne pas suspendre le Boss
            if (_employeSelectionne.Role == "Boss")
            {
                AfficherMessage("Impossible de suspendre le Boss.", succes: false);
                return;
            }

            string nomComplet = _employeSelectionne.Prenom + " " + _employeSelectionne.Nom;

            if (_employeSelectionne.Statut == "Actif")
            {
                _employeSelectionne.Statut = "Suspendu";
                _context.SaveChanges();
                AfficherMessage(nomComplet + " suspendu.", succes: true);
            }
            else
            {
                _employeSelectionne.Statut = "Actif";
                _context.SaveChanges();
                AfficherMessage(nomComplet + " réactivé.", succes: true);
            }

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
            GridEmployes.SelectedItem = null;
        }

        // ------------------------------------------------------------------
        // Affichage du message de retour — CORRECTIF DU BUG :
        // avant, seul le chemin de succes rendait BorderMessage visible ;
        // toutes les erreurs de validation restaient invisibles a l'ecran.
        // Desormais un seul point de sortie rend TOUJOURS le message visible,
        // qu'il s'agisse d'un succes ou d'une erreur.
        // ------------------------------------------------------------------
        private void AfficherMessage(string texte, bool succes)
        {
            TxtMessage.Text = texte;
            TxtMessage.Foreground = succes
                ? System.Windows.Media.Brushes.Green
                : System.Windows.Media.Brushes.Red;

            BorderMessage.Background = new System.Windows.Media.SolidColorBrush(
                succes
                    ? System.Windows.Media.Color.FromRgb(236, 253, 245)   // vert clair
                    : System.Windows.Media.Color.FromRgb(254, 242, 242)); // rouge clair

            BorderMessage.Visibility = System.Windows.Visibility.Visible;
        }

        // ------------------------------------------------------------------
        // Hachage sécurisé (PBKDF2 + sel) — logique centralisée dans Helpers/PasswordHasher.cs
        // ------------------------------------------------------------------
        private static string HashMotDePasse(string motDePasse)
            => GestionCoutureApp.Helpers.PasswordHasher.Hasher(motDePasse);
    }
}
