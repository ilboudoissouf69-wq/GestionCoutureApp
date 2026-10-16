using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using GestionCoutureApp.Data;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace GestionCoutureApp.Views
{
    public partial class CouturierDashboardView : Page
    {
        private readonly Models.Employe _couturier;
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IEventAggregator _eventAggregator;

        // ── Rafraîchissement automatique ─────────────────────────────────
        private readonly DispatcherTimer _timerPeriodique;
        private readonly DispatcherTimer _timerDebounce;
        private readonly Action<DomainChangedEvent> _handlerDomaine;
        private bool _refreshEnCours = false;

        public CouturierDashboardView(Models.Employe couturier)
        {
            InitializeComponent();
            _couturier = couturier;

            _contextFactory  = App.Services.GetRequiredService<IDbContextFactory<ApplicationDbContext>>();
            _eventAggregator = App.Services.GetRequiredService<IEventAggregator>();

            TxtNomCouturier.Text = $"Bienvenue, {couturier.Prenom} {couturier.Nom}";

            // Anti-rebond 500 ms
            _timerDebounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
            _timerDebounce.Tick += (_, _) => { _timerDebounce.Stop(); RafraichirAsync(); };

            // Périodique 60 s
            _timerPeriodique = new DispatcherTimer { Interval = TimeSpan.FromSeconds(60) };
            _timerPeriodique.Tick += (_, _) => RafraichirAsync();

            _handlerDomaine = _ => PlanifierRefresh();

            Loaded   += OnLoaded;
            Unloaded += OnUnloaded;
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            // Les couturiers n'ont besoin que des événements Commande et Retour
            _eventAggregator.Subscribe(DomainChangedType.Commande, _handlerDomaine);
            _eventAggregator.Subscribe(DomainChangedType.Retour,   _handlerDomaine);

            _timerPeriodique.Start();
            RafraichirAsync();
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            _timerDebounce.Stop();
            _timerPeriodique.Stop();

            _eventAggregator.Unsubscribe(DomainChangedType.Commande, _handlerDomaine);
            _eventAggregator.Unsubscribe(DomainChangedType.Retour,   _handlerDomaine);
        }

        private void PlanifierRefresh()
        {
            Dispatcher.BeginInvoke(() =>
            {
                _timerDebounce.Stop();
                _timerDebounce.Start();
            });
        }

        private async void BtnActualiserCouturier_Click(object sender, RoutedEventArgs e)
        {
            await Task.Run(RafraichirAsync);
        }

        private void RafraichirAsync()
        {
            if (_refreshEnCours) return;
            _refreshEnCours = true;

            try
            {
                Dispatcher.Invoke(ChargerDonnees);
                Dispatcher.Invoke(() =>
                {
                    if (TxtDerniereMAJCouturier != null)
                        TxtDerniereMAJCouturier.Text = $"Mise à jour : {DateTime.Now:HH:mm:ss}";
                });
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("[CouturierDashboard] " + ex.Message);
            }
            finally
            {
                _refreshEnCours = false;
            }
        }

        private void ChargerDonnees()
        {
            using var context = _contextFactory.CreateDbContext();

            // Filtrage via Pieces.IdCouturier (Commande.IdCouturier est déprécié)
            var commandes = context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces)
                .Where(c => !c.EstSupprimee && c.Pieces.Any(p => p.IdCouturier == _couturier.IdEmploye))
                .ToList();

            GridMesCommandes.ItemsSource = commandes;

            TxtEnCours.Text   = commandes.Count(c => c.StatutGlobal == "En cours").ToString();
            TxtTerminees.Text = commandes.Count(c => c.StatutGlobal == "Terminee").ToString();
            TxtLivrees.Text   = commandes.Count(c => c.StatutGlobal == "Livree").ToString();

            // Revenus : uniquement la part de couture des pièces de CE couturier
            TxtRevenus.Text = commandes
                .SelectMany(c => c.Pieces)
                .Where(p => p.IdCouturier == _couturier.IdEmploye)
                .Sum(p => p.MontantCouture)
                .ToString("N0");
        }
    }
}
