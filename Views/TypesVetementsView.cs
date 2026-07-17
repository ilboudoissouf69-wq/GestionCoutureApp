using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class TypesVetementsView : Page
    {
        private readonly ApplicationDbContext _context;
        private TypeVetement? _typeSelectionne;
        private readonly List<string> _mesuresTemporaires = new List<string>();

        public TypesVetementsView()
        {
            var authService = App.Services.GetRequiredService<IAuthService>();
            if (authService.UtilisateurConnecte?.Role != "Boss")
                throw new UnauthorizedAccessException("Accès réservé au Boss.");

            InitializeComponent();
            _context = App.Services.GetRequiredService<ApplicationDbContext>();
            ChargerTypes();
        }

        private void ChargerTypes()
        {
            var types = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .OrderBy(t => t.Nom)
                .Select(t => new
                {
                    t.IdTypeVetement,
                    t.Nom,
                    t.PrixBase,
                    NbMesures = t.MesuresRequises.Count + " mesure(s)"
                })
                .ToList();

            GridTypes.ItemsSource = types;
        }

        private void TxtRecherche_TextChanged(object sender, TextChangedEventArgs e)
        {
            string terme = TxtRecherche.Text.Trim().ToLower();
            if (string.IsNullOrEmpty(terme))
            {
                ChargerTypes();
                return;
            }

            var types = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .Where(t => t.Nom.ToLower().Contains(terme))
                .Select(t => new
                {
                    t.IdTypeVetement,
                    t.Nom,
                    t.PrixBase,
                    NbMesures = t.MesuresRequises.Count + " mesure(s)"
                })
                .ToList();

            GridTypes.ItemsSource = types;
        }

        private void GridTypes_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (GridTypes.SelectedItem == null) return;

            dynamic item = GridTypes.SelectedItem;
            int id = (int)item.IdTypeVetement;

            _typeSelectionne = _context.TypesVetements
                .Include(t => t.MesuresRequises)
                .FirstOrDefault(t => t.IdTypeVetement == id);

            if (_typeSelectionne == null) return;

            TxtNom.Text = _typeSelectionne.Nom;
            TxtPrixBase.Text = _typeSelectionne.PrixBase.ToString();

            _mesuresTemporaires.Clear();
            foreach (var m in _typeSelectionne.MesuresRequises)
                _mesuresTemporaires.Add(m.NomMesure);

            RafraichirListeMesures();
        }

        private void BtnAjouterMesure_Click(object sender, RoutedEventArgs e)
        {
            string nom = TxtNomMesure.Text.Trim();
            if (string.IsNullOrWhiteSpace(nom))
            {
                TxtMessage.Text = "Saisissez un nom de mesure.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (_mesuresTemporaires.Any(m => m.Equals(nom, StringComparison.OrdinalIgnoreCase)))
            {
                TxtMessage.Text = "Cette mesure existe déjà.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            _mesuresTemporaires.Add(nom);
            RafraichirListeMesures();
            TxtNomMesure.Clear();
            TxtMessage.Text = string.Empty;
        }

        private void RafraichirListeMesures()
        {
            ListeMesures.ItemsSource = null;
            ListeMesures.ItemsSource = _mesuresTemporaires.ToList();
        }

        private void BtnEnregistrer_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtNom.Text))
            {
                TxtMessage.Text = "Saisissez le nom du type.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            if (!double.TryParse(TxtPrixBase.Text, out double prixBase) || prixBase <= 0)
            {
                TxtMessage.Text = "Saisissez un prix valide.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            try
            {
                if (_typeSelectionne == null)
                {
                    var nouveau = new TypeVetement
                    {
                        Nom = TxtNom.Text.Trim(),
                        PrixBase = prixBase,
                        MesuresRequises = _mesuresTemporaires.Select(nom => new MesureRequise
                        {
                            NomMesure = nom
                        }).ToList()
                    };

                    _context.TypesVetements.Add(nouveau);
                    _context.SaveChanges();
                    TxtMessage.Text = "Type ajouté.";
                    TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
                }
                else
                {
                    _context.MesuresRequises.RemoveRange(
                        _context.MesuresRequises.Where(m => m.IdTypeVetement == _typeSelectionne.IdTypeVetement));

                    _typeSelectionne.Nom = TxtNom.Text.Trim();
                    _typeSelectionne.PrixBase = prixBase;
                    _typeSelectionne.MesuresRequises = _mesuresTemporaires.Select(nom => new MesureRequise
                    {
                        NomMesure = nom
                    }).ToList();

                    _context.SaveChanges();
                    TxtMessage.Text = "Type modifié.";
                    TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
                }

                ChargerTypes();
                ViderFormulaire();
            }
            catch (Exception ex)
            {
                TxtMessage.Text = "Erreur : " + ex.Message;
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
            }
        }

        private void BtnSupprimer_Click(object sender, RoutedEventArgs e)
        {
            if (_typeSelectionne == null)
            {
                TxtMessage.Text = "Sélectionnez un type.";
                TxtMessage.Foreground = System.Windows.Media.Brushes.Red;
                return;
            }

            var r = MessageBox.Show(
                $"Supprimer le type \"{_typeSelectionne.Nom}\" ?",
                "Confirmation", MessageBoxButton.YesNo,
                MessageBoxImage.Warning);

            if (r != MessageBoxResult.Yes) return;

            _context.TypesVetements.Remove(_typeSelectionne);
            _context.SaveChanges();
            TxtMessage.Text = "Type supprimé.";
            TxtMessage.Foreground = System.Windows.Media.Brushes.Green;
            ChargerTypes();
            ViderFormulaire();
        }

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
            _mesuresTemporaires.Clear();
            RafraichirListeMesures();
            GridTypes.SelectedItem = null;
            TxtMessage.Text = string.Empty;
        }
    }
}