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

        // CORRECTIF (audit) : réécrit pour travailler PIÈCE PAR PIÈCE (et non
        // plus commande par commande) et pour générer les DEUX types d'alerte
        // prévus au Point 5 du cahier. L'ancienne version ne produisait que
        // l'alerte "rendez-vous proche" ; l'alerte "pas encore prise en
        // charge" (mi-délai entre dépôt et rendez-vous, statut toujours
        // "À faire") n'existait nulle part dans le code.
        public async Task<List<AlerteRendezVous>> ObtenirAlertesActuelles()
        {
            var delaiHeures = await _parametresService.ObtenirDelaiAlerteRendezVousHeures();
            var maintenant = DateTime.Now;

            await using var context = await _contextFactory.CreateDbContextAsync();

            var pieces = await context.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Client)
                .Where(p => p.Statut != "Livree" && p.Commande != null)
                .AsNoTracking()
                .ToListAsync();

            var alertes = new List<AlerteRendezVous>();

            foreach (var piece in pieces)
            {
                var commande = piece.Commande!;
                var dateRdv = ObtenirDateRendezVous(piece, commande);
                if (dateRdv <= maintenant) continue; // rendez-vous déjà passé : pas une alerte "à venir"

                // Alerte 1 — "pas encore prise en charge" : à la moitié du temps
                // entre le dépôt et le rendez-vous, si le statut est toujours
                // "A faire". Disparaît dès que le statut passe à "En cours"
                // (cette pièce ne rentre alors même plus dans cette boucle
                // puisque le calcul se refait à chaque appel — rien à stocker).
                if (piece.Statut == "A faire")
                {
                    var dateDepot = commande.DateDebut.Date + commande.HeureDebut;
                    var dureeTotal = dateRdv - dateDepot;
                    if (dureeTotal > TimeSpan.Zero)
                    {
                        var miTemps = dateDepot + TimeSpan.FromTicks(dureeTotal.Ticks / 2);
                        if (maintenant >= miTemps)
                        {
                            alertes.Add(ConstruireAlerte(
                                piece, commande, dateRdv, maintenant,
                                "PasEncorePriseEnCharge"));
                        }
                    }
                }

                // Alerte 2 — "rendez-vous proche" : dans les N heures réglées
                // par le Boss (Paramètres), tant que le statut n'est pas
                // encore "Terminee".
                if (piece.Statut != "Terminee" && dateRdv <= maintenant.AddHours(delaiHeures))
                {
                    alertes.Add(ConstruireAlerte(
                        piece, commande, dateRdv, maintenant,
                        "RendezVousProche"));
                }
            }

            return alertes
                .OrderBy(a => a.DateRendezVous)
                .ToList();
        }

        public async Task<List<AlerteRendezVous>> ObtenirTousRendezVousAVenir()
        {
            var maintenant = DateTime.Now;

            await using var context = await _contextFactory.CreateDbContextAsync();

            var pieces = await context.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Client)
                .Where(p => p.Statut != "Livree" && p.Commande != null)
                .AsNoTracking()
                .ToListAsync();

            var resultat = new List<AlerteRendezVous>();
            foreach (var piece in pieces)
            {
                var commande = piece.Commande!;
                var dateRdv = ObtenirDateRendezVous(piece, commande);
                if (dateRdv <= maintenant) continue;

                resultat.Add(ConstruireAlerte(piece, commande, dateRdv, maintenant, "RendezVousProche"));
            }

            return resultat.OrderBy(a => a.DateRendezVous).ToList();
        }

        // CORRECTIF (audit) : centralise la règle "rendez-vous de la pièce" —
        // honore désormais PieceCommande.RendezVousException (Point 5, cas
        // d'exception) au lieu d'utiliser systématiquement le rendez-vous
        // global de la commande, comme le faisait l'ancienne version.
        private static DateTime ObtenirDateRendezVous(PieceCommande piece, Commande commande)
        {
            if (piece.RendezVousException.HasValue)
                return piece.RendezVousException.Value;

            var heureFin = commande.HeureFin ?? new TimeSpan(17, 0, 0);
            return commande.DateFin.Date + heureFin;
        }

        private static AlerteRendezVous ConstruireAlerte(
            PieceCommande piece, Commande commande, DateTime dateRdv,
            DateTime maintenant, string typeAlerte)
        {
            var tempsRestant = dateRdv - maintenant;

            return new AlerteRendezVous
            {
                IdCommande = commande.IdCommande,
                IdPieceCommande = piece.IdPieceCommande,
                NomClient = commande.Client != null
                    ? $"{commande.Client.Prenom} {commande.Client.Nom}"
                    : "(client inconnu)",
                Telephone = commande.Client?.Telephone ?? "",
                TypeVetement = piece.TypeVetement,
                DateRendezVous = dateRdv,
                HeureRendezVous = dateRdv.ToString("HH:mm"),
                Statut = piece.StatutAffiche,
                TempsRestant = FormaterTempsRestant(tempsRestant),
                NomCouturier = piece.Couturier?.NomComplet ?? "(non assigné)",
                EstUrgent = tempsRestant.TotalHours <= 1,
                ProposerContactWhatsApp = piece.Statut == "Terminee" && dateRdv <= maintenant,
                TypeAlerte = typeAlerte
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
