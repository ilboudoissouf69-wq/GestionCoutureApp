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

        // Génère les deux types d'alerte par pièce : "rendez-vous proche" et
        // "pas encore prise en charge" (mi-délai entre dépôt et rendez-vous avec statut "À faire").
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

        /// <summary>
        /// Section Retrait : toutes les pièces non livrées dont le RDV est
        /// dans les 7 prochains jours OU dont la pièce est terminée
        /// (y compris si le RDV est passé — le client n'est pas encore venu).
        /// </summary>
        public async Task<List<AlerteRendezVous>> ObtenirRendezVousSemaine()
        {
            var maintenant = DateTime.Now;
            var finSemaine = maintenant.AddDays(7);

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
                var dateRdv  = ObtenirDateRendezVous(piece, commande);

                // Inclure : pièce terminée (peu importe le RDV) OU RDV dans la semaine
                bool pieceTerminee = piece.Statut == "Terminee";
                bool rdvSemaine    = dateRdv >= maintenant && dateRdv <= finSemaine;

                if (!pieceTerminee && !rdvSemaine) continue;

                var alerte = ConstruireAlerte(piece, commande, dateRdv, maintenant, "RendezVousProche");
                // ProposerContactWhatsApp : pièce terminée ET (RDV passé ou aujourd'hui)
                alerte.ProposerContactWhatsApp = pieceTerminee && dateRdv.Date <= maintenant.Date;
                resultat.Add(alerte);
            }

            return resultat
                .OrderBy(a => a.ProposerContactWhatsApp ? 0 : 1) // Prêts en premier
                .ThenBy(a => a.DateRendezVous)
                .ToList();
        }

        // Retourne le rendez-vous de la pièce : honore PieceCommande.RendezVousException
        // (cas d'exception de pièce individuelle) en priorité sur le RDV global de la commande.
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
