using System.Windows;
using System.Windows.Controls;

namespace GestionCoutureApp.Views
{
    public partial class MainWindow : Window
    {
        private readonly Models.Employe _employeConnecte;

        // Garde la référence du bouton actuellement actif
        private Button? _boutonActif;

        public MainWindow(Models.Employe employe)
        {
            InitializeComponent();
            _employeConnecte = employe;
            TxtUtilisateur.Text = $"{employe.Prenom} {employe.Nom} ({employe.Role})";

            string role = employe.Role;

            Loaded += (s, e) =>
            {
                if (role == "Boss")
                {
                    BtnTableauDeBord.Visibility = Visibility.Visible;
                    BtnClients.Visibility = Visibility.Visible;
                    BtnCommandes.Visibility = Visibility.Visible;
                    BtnPaiements.Visibility = Visibility.Visible;
                    BtnTypesVetements.Visibility = Visibility.Visible;
                    BtnEmployes.Visibility = Visibility.Visible;
                    BtnCommissions.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(new DashboardView());
                    SetBoutonActif(BtnTableauDeBord);
                }
                else if (role == "Secretaire")
                {
                    BtnTableauDeBord.Visibility = Visibility.Visible;
                    BtnClients.Visibility = Visibility.Visible;
                    BtnCommandes.Visibility = Visibility.Visible;
                    BtnPaiements.Visibility = Visibility.Visible;
                    BtnTypesVetements.Visibility = Visibility.Collapsed;
                    BtnEmployes.Visibility = Visibility.Collapsed;
                    BtnCommissions.Visibility = Visibility.Collapsed;
                    ContentFrame.Navigate(new DashboardView());
                    SetBoutonActif(BtnTableauDeBord);
                }
                else if (role == "Couturier")
                {
                    BtnTableauDeBord.Visibility = Visibility.Collapsed;
                    BtnClients.Visibility = Visibility.Collapsed;
                    BtnCommandes.Visibility = Visibility.Collapsed;
                    BtnPaiements.Visibility = Visibility.Collapsed;
                    BtnTypesVetements.Visibility = Visibility.Collapsed;
                    BtnEmployes.Visibility = Visibility.Collapsed;
                    BtnCommissions.Visibility = Visibility.Collapsed;
                    BtnDeconnexion.Visibility = Visibility.Visible;
                    ContentFrame.Navigate(new CouturierDashboardView(employe));
                }
            };
        }

        // ------------------------------------------------------------------
        // Active visuellement un bouton de nav et désactive l'ancien
        // ------------------------------------------------------------------
        private void SetBoutonActif(Button bouton)
        {
            // Remet l'ancien bouton au style normal
            if (_boutonActif != null)
                _boutonActif.Style = (Style)Resources["NavBtn"];

            // Applique le style actif au nouveau bouton
            bouton.Style = (Style)Resources["NavBtnActif"];
            _boutonActif = bouton;
        }

        // ------------------------------------------------------------------
        private bool RoleAutorise(params string[] rolesAutorises)
        {
            if (rolesAutorises.Contains(_employeConnecte.Role)) return true;

            MessageBox.Show("Vous n'avez pas les droits nécessaires pour accéder à cette section.",
                "Accès refusé", MessageBoxButton.OK, MessageBoxImage.Warning);
            return false;
        }

        private void BtnTableauDeBord_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss", "Secretaire")) return;
            ContentFrame.Navigate(new DashboardView());
            SetBoutonActif(BtnTableauDeBord);
        }

        private void BtnClients_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss", "Secretaire")) return;
            ContentFrame.Navigate(new ClientsView());
            SetBoutonActif(BtnClients);
        }

        private void BtnCommandes_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss", "Secretaire")) return;
            ContentFrame.Navigate(new CommandesView());
            SetBoutonActif(BtnCommandes);
        }

        private void BtnPaiements_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss", "Secretaire")) return;
            ContentFrame.Navigate(new PaiementsView());
            SetBoutonActif(BtnPaiements);
        }

        private void BtnTypesVetements_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss")) return;
            try
            {
                ContentFrame.Navigate(new TypesVetementsView());
                SetBoutonActif(BtnTypesVetements);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Accès refusé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnEmployes_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss")) return;
            try
            {
                ContentFrame.Navigate(new EmployesView());
                SetBoutonActif(BtnEmployes);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Accès refusé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDeconnexion_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }

        private void BtnCommissions_Click(object sender, RoutedEventArgs e)
        {
            if (!RoleAutorise("Boss")) return;
            try
            {
                ContentFrame.Navigate(new CommissionsView());
                SetBoutonActif(BtnCommissions);
            }
            catch (UnauthorizedAccessException)
            {
                MessageBox.Show("Accès refusé.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}