using System.Windows;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class MainWindow : Window
    {
        private readonly Models.Employe _employeConnecte;

        public MainWindow(Models.Employe employe)
        {
            InitializeComponent();
            _employeConnecte = employe;
            // Nom complet + role dans la sidebar
            TxtUtilisateur.Text = $"{employe.Prenom} {employe.Nom} ({employe.Role})";

            string role = employe.Role;

            Loaded += (s, e) =>
            {
                if (role == "Boss")
                {
                    BtnTableauDeBord.Visibility  = Visibility.Visible;
                    BtnClients.Visibility        = Visibility.Visible;
                    BtnCommandes.Visibility      = Visibility.Visible;
                    BtnPaiements.Visibility      = Visibility.Visible;
                    BtnTypesVetements.Visibility = Visibility.Visible;
                    BtnEmployes.Visibility       = Visibility.Visible;
                    BtnCommissions.Visibility    = Visibility.Visible;
                    ContentFrame.Navigate(new DashboardView());
                }
                else if (role == "Secretaire")
                {
                    BtnTableauDeBord.Visibility  = Visibility.Visible;
                    BtnClients.Visibility        = Visibility.Visible;
                    BtnCommandes.Visibility      = Visibility.Visible;
                    BtnPaiements.Visibility      = Visibility.Visible;
                    BtnTypesVetements.Visibility = Visibility.Collapsed;
                    BtnEmployes.Visibility       = Visibility.Collapsed;
                    BtnCommissions.Visibility    = Visibility.Collapsed;
                    ContentFrame.Navigate(new DashboardView());
                }
                else if (role == "Couturier")
                {
                    BtnTableauDeBord.Visibility  = Visibility.Collapsed;
                    BtnClients.Visibility        = Visibility.Collapsed;
                    BtnCommandes.Visibility      = Visibility.Collapsed;
                    BtnPaiements.Visibility      = Visibility.Collapsed;
                    BtnTypesVetements.Visibility = Visibility.Collapsed;
                    BtnEmployes.Visibility       = Visibility.Collapsed;
                    BtnCommissions.Visibility    = Visibility.Collapsed;
                    BtnDeconnexion.Visibility    = Visibility.Visible;
                    ContentFrame.Navigate(new CouturierDashboardView(employe));
                }
            };
        }

        private void BtnTableauDeBord_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new DashboardView());
        }

        private void BtnClients_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new ClientsView());
        }

        private void BtnCommandes_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new CommandesView());
        }

        private void BtnPaiements_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new PaiementsView());
        }

        private void BtnTypesVetements_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new TypesVetementsView());
        }

        private void BtnEmployes_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new EmployesView());
        }

        private void BtnDeconnexion_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
            var loginWindow = new LoginWindow();
            loginWindow.Show();
        }

        private void BtnCommissions_Click(object sender, RoutedEventArgs e)
        {
            ContentFrame.Navigate(new CommissionsView());
        }
    }
}