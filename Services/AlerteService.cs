using GestionCoutureApp.Helpers;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class AlerteService : IAlerteService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IParametresService _parametresService;
        private readonly Microsoft.Extensions.Logging.ILogger<AlerteService> _logger;

        public AlerteService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IParametresService parametresService,
            Microsoft.Extensions.Logging.ILogger<AlerteService> logger)
        {
            _contextFactory    = contextFactory;
            _parametresService = parametresService;
            _logger            = logger;
        }

        // ── Cache léger : évite de scanner les pièces 3× par rafraîchissement ──
        // On conserve le résultat du dernier chargement pendant 5 s.
        // Si AlertesView appelle ObtenirAlertesActuelles, ObtenirRendezVousSemaine
        // et ObtenirRetards dans la même seconde, EF Core n'interroge la base qu'une fois.
        private List<PieceCommande>? _cachePieces;
        private DateTime _cacheExpire = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuree = TimeSpan.FromSeconds(5);

        /// <summary>
        /// Charge les pièces actives (ni Livrée) depuis la base, ou retourne
        /// le cache si celui-ci a moins de 5 secondes.
        /// Appelé par toutes les méthodes publiques d'alerte — une seule requête SQL
        /// même si les 3 méthodes sont appelées en séquence depuis AlertesView.
        /// </summary>
        private async Task<List<PieceCommande>> ObtenirPiecesActivesAsync()
        {
            if (_cachePieces != null && DateTime.Now < _cacheExpire)
                return _cachePieces;

            await using var context = await _contextFactory.CreateDbContextAsync();

            var pieces = await context.PiecesCommande
                .Where(p => p.Statut != "Livree"
                         && p.Commande != null
                         && p.Commande.EstSupprimee == false)
                .Select(p => new PieceCommande
                {
                    IdPieceCommande     = p.IdPieceCommande,
                    IdCommande          = p.IdCommande,
                    TypeVetement        = p.TypeVetement,
                    Statut              = p.Statut,
                    MontantCouture      = p.MontantCouture,
                    IdCouturier         = p.IdCouturier,
                    RendezVousException = p.RendezVousException,
                    Commande = p.Commande == null ? null : new Commande
                    {
                        IdCommande   = p.Commande.IdCommande,
                        DateDebut    = p.Commande.DateDebut,
                        HeureDebut   = p.Commande.HeureDebut,
                        DateFin      = p.Commande.DateFin,
                        HeureFin     = p.Commande.HeureFin,
                        EstSupprimee = p.Commande.EstSupprimee,
                        Client = p.Commande.Client == null ? null : new Models.Client
                        {
                            IdClient  = p.Commande.Client.IdClient,
                            Prenom    = p.Commande.Client.Prenom,
                            Nom       = p.Commande.Client.Nom,
                            Telephone = p.Commande.Client.Telephone
                        }
                    },
                    Couturier = p.Couturier == null ? null : new Employe
                    {
                        IdEmploye = p.Couturier.IdEmploye,
                        Prenom    = p.Couturier.Prenom,
                        Nom       = p.Couturier.Nom,
                        Role      = p.Couturier.Role,
                        Statut    = p.Couturier.Statut,
                        Identifiant = p.Couturier.Identifiant,
                        MotDePasse  = p.Couturier.MotDePasse
                    }
                })
                .AsNoTracking()
                .ToListAsync();

            _cachePieces = pieces;
            _cacheExpire = DateTime.Now.Add(_cacheDuree);
            return pieces;
        }

        /// <summary>Invalide le cache (à appeler après toute modification de pièce).</summary>
        public void InvaliderCache() => _cachePieces = null;

        // Génère les deux types d'alerte par pièce : "rendez-vous proche" et
        // "pas encore prise en charge" (mi-délai entre dépôt et rendez-vous avec statut "À faire").
        public async Task<List<AlerteRendezVous>> ObtenirAlertesActuelles()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirAlertesActuelles", _logger);
            var delaiHeures = await _parametresService.ObtenirDelaiAlerteRendezVousHeures();
            var maintenant = DateTime.Now;

            var pieces = await ObtenirPiecesActivesAsync();
            var alertes = new List<AlerteRendezVous>();

            foreach (var piece in pieces)
            {
                var commande = piece.Commande!;
                var dateRdv = ObtenirDateRendezVous(piece, commande);
                if (dateRdv <= maintenant) continue; // rendez-vous déjà passé : pas une alerte "à venir"

                // Alerte 1 — "pas encore prise en charge" : à la moitié du temps
                // entre le dépôt et le rendez-vous, si le statut est toujours "A faire".
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

                // Alerte 2 — "rendez-vous proche" : dans les N heures réglées par le Boss.
                if (piece.Statut != "Terminee" && dateRdv <= maintenant.AddHours(delaiHeures))
                {
                    alertes.Add(ConstruireAlerte(
                        piece, commande, dateRdv, maintenant,
                        "RendezVousProche"));
                }
            }

            return alertes.OrderBy(a => a.DateRendezVous).ToList();
        }

        public async Task<List<AlerteRendezVous>> ObtenirTousRendezVousAVenir()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirTousRendezVousAVenir", _logger);
            var maintenant = DateTime.Now;
            var pieces = await ObtenirPiecesActivesAsync();

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
        /// dans les 7 prochains jours OU dont la pièce est terminée.
        /// </summary>
        public async Task<List<AlerteRendezVous>> ObtenirRendezVousSemaine()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirRendezVousSemaine", _logger);
            var maintenant = DateTime.Now;
            var finSemaine = maintenant.AddDays(7);

            var pieces = await ObtenirPiecesActivesAsync();
            var resultat = new List<AlerteRendezVous>();

            foreach (var piece in pieces)
            {
                var commande = piece.Commande!;
                var dateRdv  = ObtenirDateRendezVous(piece, commande);

                bool pieceTerminee = piece.Statut == "Terminee";
                bool rdvSemaine    = dateRdv >= maintenant && dateRdv <= finSemaine;

                if (!pieceTerminee && !rdvSemaine) continue;

                var alerte = ConstruireAlerte(piece, commande, dateRdv, maintenant, "RendezVousProche");
                alerte.ProposerContactWhatsApp = pieceTerminee && dateRdv.Date <= maintenant.Date;
                resultat.Add(alerte);
            }

            return resultat
                .OrderBy(a => a.ProposerContactWhatsApp ? 0 : 1)
                .ThenBy(a => a.DateRendezVous)
                .ToList();
        }

        /// <summary>
        /// Pièces non livrées dont le RDV est déjà dépassé.
        /// TypeAlerte = "Retard". Tri : RDV le plus ancien en premier.
        /// </summary>
        public async Task<List<AlerteRendezVous>> ObtenirRetards()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirRetards", _logger);
            var maintenant = DateTime.Now;

            // Exclut aussi "Terminee" ici : travail fini, client pas encore venu → pas retard prod
            var pieces = (await ObtenirPiecesActivesAsync())
                .Where(p => p.Statut != "Terminee")
                .ToList();

            var resultat = new List<AlerteRendezVous>();
            foreach (var piece in pieces)
            {
                var commande = piece.Commande!;
                var dateRdv  = ObtenirDateRendezVous(piece, commande);
                if (dateRdv >= maintenant) continue;

                var alerte = ConstruireAlerte(piece, commande, dateRdv, maintenant, "Retard");
                alerte.ProposerContactWhatsApp = piece.Statut == "Terminee";
                resultat.Add(alerte);
            }

            return resultat.OrderBy(a => a.DateRendezVous).ToList();
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

        // ══════════════════════════════════════════════════════════════════
        // ÉTAPE 6 — ALERTES RETOURS
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Retours actifs avec DateRdvReprise renseignée.
        /// Inclut : Signale, En reprise, Termine (pas encore récupéré).
        /// Exclut : Rendu, Annulé, sans DateRdvReprise.
        /// Les mêmes niveaux d'urgence (J-1 / Jour J / retard) s'appliquent.
        /// </summary>
        public async Task<List<AlerteRendezVous>> ObtenirAlertesRetours()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirAlertesRetours", _logger);
            await using var context = await _contextFactory.CreateDbContextAsync();

            var retours = await context.Retours
                .Include(r => r.Commande).ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .Include(r => r.CouturierReprise)
                .Where(r =>
                    !r.EstAnnule &&
                    r.Statut != "Rendu" &&
                    r.DateRdvReprise.HasValue)
                .AsNoTracking()
                .ToListAsync();

            var alertes = new List<AlerteRendezVous>();
            var maintenant = DateTime.Now;

            foreach (var ret in retours)
            {
                var dateRdv = ret.DateRdvReprise!.Value;
                if (ret.HeureDebutReprise.HasValue)
                    dateRdv = ret.DateRdvReprise.Value.Date + ret.HeureDebutReprise.Value;

                string nomCouturier = ret.CouturierReprise != null
                    ? ret.CouturierReprise.Prenom + " " + ret.CouturierReprise.Nom
                    : ret.Couturier != null
                        ? ret.Couturier.Prenom + " " + ret.Couturier.Nom
                        : "(non assigné)";

                string nomClient = ret.Commande?.Client != null
                    ? ret.Commande.Client.Prenom + " " + ret.Commande.Client.Nom
                    : "(client inconnu)";

                var tempsRestant = dateRdv - maintenant;

                alertes.Add(new AlerteRendezVous
                {
                    IdCommande       = ret.IdCommande,
                    IdPieceCommande  = ret.IdPieceCommande,
                    IdRetour         = ret.IdRetour,
                    NumeroRetour     = ret.NumeroRetour,
                    EstAlertRetour   = true,
                    NomClient        = nomClient,
                    Telephone        = ret.Commande?.Client?.Telephone ?? "",
                    TypeVetement     = ret.PieceCommande?.TypeVetement ?? "—",
                    DateRendezVous   = dateRdv,
                    HeureRendezVous  = dateRdv.ToString("HH:mm"),
                    Statut           = ret.Statut,
                    TempsRestant     = FormaterTempsRestant(tempsRestant),
                    NomCouturier     = nomCouturier,
                    EstUrgent        = tempsRestant.TotalHours <= 1,
                    ProposerContactWhatsApp = false,  // pas de WhatsApp auto sur retours
                    TypeAlerte       = "Retour"
                });
            }

            return alertes.OrderBy(a => a.DateRendezVous).ToList();
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
