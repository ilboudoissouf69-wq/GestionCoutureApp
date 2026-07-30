using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class AlerteService : IAlerteService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IParametresService _parametresService;

        public AlerteService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IParametresService parametresService)
        {
            _contextFactory = contextFactory;
            _parametresService = parametresService;
        }

        public async Task<List<AlerteRendezVous>> ObtenirAlertesActuelles()
        {
            var delaiHeures = await _parametresService.ObtenirDelaiAlerteRendezVousHeures();
            var maintenant = DateTime.Now;
            var limite = maintenant.AddHours(delaiHeures);

            var tous = await ObtenirTousRendezVousAVenir();

            return tous
                .Where(a => a.DateRendezVous <= limite)
                .OrderBy(a => a.DateRendezVous)
                .ToList();
        }

        public async Task<List<AlerteRendezVous>> ObtenirTousRendezVousAVenir()
        {
            var maintenant = DateTime.Now;

            await using var context = await _contextFactory.CreateDbContextAsync();

            var commandes = await context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Where(c => c.DateFin >= maintenant.Date)
                .Where(c => c.Pieces.Any(p => p.Statut != "Livree"))
                .OrderBy(c => c.DateFin)
                .ThenBy(c => c.HeureFin ?? TimeSpan.Zero)
                .AsNoTracking()
                .ToListAsync();

            return commandes
                .Select(c => CreerAlerte(c, maintenant))
                .Where(a => a != null)
                .OrderBy(a => a!.DateRendezVous)
                .ToList()!;
        }

        private static AlerteRendezVous? CreerAlerte(Commande c, DateTime maintenant)
        {
            // Heure de rendez-vous : HeureFin si renseignée, sinon 17h par défaut
            var heureFin = c.HeureFin ?? new TimeSpan(17, 0, 0);
            var dateRdv = c.DateFin.Date + heureFin;

            // On ne crée l'alerte que si le RDV est encore dans le futur
            if (dateRdv <= maintenant)
                return null;

            var tempsRestant = dateRdv - maintenant;
            var premierePiece = c.Pieces.FirstOrDefault();
            var couturier = premierePiece?.Couturier;

            return new AlerteRendezVous
            {
                IdCommande = c.IdCommande,
                NomClient = c.Client != null
                    ? $"{c.Client.Prenom} {c.Client.Nom}"
                    : "(client inconnu)",
                Telephone = c.Client?.Telephone ?? "",
                TypeVetement = c.TypeVetementAffiche,
                DateRendezVous = dateRdv,
                HeureRendezVous = dateRdv.ToString("HH:mm"),
                Statut = c.StatutGlobalAffiche,
                TempsRestant = FormaterTempsRestant(tempsRestant),
                NomCouturier = couturier != null
                    ? couturier.NomComplet
                    : "(non assigné)",
                EstUrgent = tempsRestant.TotalHours <= 1
            };
        }

        private static string FormaterTempsRestant(TimeSpan reste)
        {
            if (reste.TotalDays >= 1)
            {
                var jours = (int)reste.TotalDays;
                var heures = reste.Hours;
                return heures > 0 ? $"{jours}j {heures}h" : $"{jours}j";
            }

            return reste.TotalMinutes >= 60
                ? $"{(int)reste.TotalHours}h {reste.Minutes:00}min"
                : $"{reste.Minutes}min";
        }
    }
}