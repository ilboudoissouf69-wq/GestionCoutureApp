using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class ClientsView : Page
    {
        private readonly IClientService _clientService;
        private int _clientSelectionneId;

        public ClientsView()
        {
            InitializeComponent();
            _clientService = App.Services.GetRequiredService<IClientService>();
            ChargerClients();
        }

        private void ChargerClients()
        {
            GridClients.ItemsSource = _clientService.ObtenirTous();
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string motCle = TxtRecherche.Text.Trim();

            if (string.IsNullOrEmpty(motCle))
                ChargerClients();
            else
                GridClients.ItemsSource = _clientService.Rechercher(motCle);
        }

        private void GridClients_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridClients.SelectedItem is Client client)
            {
                _clientSelectionneId = client.IdClient;
                TxtNom.Text = client.Nom;
                TxtPrenom.Text = client.Prenom;
                TxtTelephone.Text = client.Telephone;
            }
        }

        private void BtnAjouter_Click(object sender, RoutedEventArgs e)
        {
            if (ChampsInvalides()) return;

            var client = new Client
            {
                Nom = TxtNom.Text.Trim(),
                Prenom = TxtPrenom.Text.Trim(),
                Telephone = TxtTelephone.Text.Trim()
            };

            _clientService.Ajouter(client);
            ChargerClients();
            ViderChamps();
            MessageBox.Show("Client ajoute avec succes !", "Succes",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnModifier_Click(object sender, RoutedEventArgs e)
        {
            if (_clientSelectionneId == 0)
            {
                MessageBox.Show("Selectionnez un client dans le tableau.",
                                "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (ChampsInvalides()) return;

            var client = new Client
            {
                IdClient = _clientSelectionneId,
                Nom = TxtNom.Text.Trim(),
                Prenom = TxtPrenom.Text.Trim(),
                Telephone = TxtTelephone.Text.Trim()
            };

            _clientService.Modifier(client);
            ChargerClients();
            ViderChamps();
            MessageBox.Show("Client modifie avec succes !", "Succes",
                            MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_clientSelectionneId == 0)
            {
                MessageBox.Show("Selectionnez un client dans le tableau.",
                                "Attention", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var resultat = MessageBox.Show(
                "Voulez-vous vraiment supprimer ce client ?",
                "Confirmation",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (resultat == MessageBoxResult.Yes)
            {
                try
                {
                    _clientService.Supprimer(_clientSelectionneId);
                    ChargerClients();
                    ViderChamps();
                    MessageBox.Show("Client supprime.", "Succes",
                                    MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (InvalidOperationException ex)
                {
                    MessageBox.Show(ex.Message, "Suppression impossible",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnVider_Click(object sender, RoutedEventArgs e)
        {
            ViderChamps();
        }

        private bool ChampsInvalides()
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text) ||
                string.IsNullOrWhiteSpace(TxtPrenom.Text))
            {
                MessageBox.Show("Le nom et le prenom sont obligatoires.",
                                "Champs manquants",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return true;
            }
            return false;
        }

        private void ViderChamps()
        {
            _clientSelectionneId = 0;
            TxtNom.Text = "";
            TxtPrenom.Text = "";
            TxtTelephone.Text = "";
            GridClients.SelectedItem = null;
        }
    }
}