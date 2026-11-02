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

        // ── Cache léger : les 3-4 méthodes partagent UNE requête ──
        private List<PieceAlerteDto>? _cachePieces;
        private DateTime _cacheExpire = DateTime.MinValue;
        private static readonly TimeSpan _cacheDuree = TimeSpan.FromSeconds(5);

        // DTO projeté : colonnes utiles uniquement, pas de MotDePasse, pas de Include
        private sealed class PieceAlerteDto
        {
            public int IdPieceCommande { get; set; }
            public int IdCommande { get; set; }
            public string TypeVetement { get; set; } = "";
            public string Statut { get; set; } = "";
            public DateTime? RendezVousException { get; set; }
            // Commande
            public DateTime CmdDateDebut { get; set; }
            public TimeSpan CmdHeureDebut { get; set; }
            public DateTime CmdDateFin { get; set; }
            public TimeSpan? CmdHeureFin { get; set; }
            public int CmdIdClient { get; set; }
            // Client
            public string? ClientPrenom { get; set; }
            public string? ClientNom { get; set; }
            public string? ClientTelephone { get; set; }
            // Couturier (sans MotDePasse)
            public string? CoutPrenom { get; set; }
            public string? CoutNom { get; set; }

            public string StatutAffiche => Statut switch
            {
                "A faire"   => "À faire",
                "En cours"  => "En cours",
                "Terminee"  => "Terminée",
                "Livree"    => "Livrée",
                _           => Statut
            };
            public string NomCouturier =>
                (!string.IsNullOrEmpty(CoutPrenom) || !string.IsNullOrEmpty(CoutNom))
                    ? $"{CoutPrenom} {CoutNom}".Trim() : "(non assigné)";
            public string NomClient =>
                (!string.IsNullOrEmpty(ClientPrenom) || !string.IsNullOrEmpty(ClientNom))
                    ? $"{ClientPrenom} {ClientNom}".Trim() : "(client inconnen)";

            public DateTime DateRdv =>
                RendezVousException.HasValue
                    ? RendezVousException.Value
                    : CmdDateFin.Date + (CmdHeureFin ?? new TimeSpan(17, 0, 0));
        }

        public AlerteService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IParametresService parametresService,
            Microsoft.Extensions.Logging.ILogger<AlerteService> logger)
        {
            _contextFactory    = contextFactory;
            _parametresService = parametresService;
            _logger            = logger;
        }

        /// <summary>Charge les pièces actives (non livrées) avec projection — une seule requête SQL.</summary>
        private async Task<List<PieceAlerteDto>> ObtenirPiecesActivesAsync()
        {
            if (_cachePieces != null && DateTime.Now < _cacheExpire)
                return _cachePieces;

            await using var context = await _contextFactory.CreateDbContextAsync();

            _cachePieces = await context.PiecesCommande
                .Where(p => p.Statut != "Livree"
                         && p.Commande != null
                         && p.Commande.EstSupprimee == false)
                .Select(p => new PieceAlerteDto
                {
                    IdPieceCommande     = p.IdPieceCommande,
                    IdCommande          = p.IdCommande,
                    TypeVetement        = p.TypeVetement,
                    Statut              = p.Statut,
                    RendezVousException = p.RendezVousException,
                    CmdDateDebut         = p.Commande!.DateDebut,
                    CmdHeureDebut        = p.Commande.HeureDebut,
                    CmdDateFin           = p.Commande.DateFin,
                    CmdHeureFin          = p.Commande.HeureFin,
                    CmdIdClient          = p.Commande.IdClient,
                    ClientPrenom         = p.Commande.Client != null ? p.Commande.Client.Prenom : null,
                    ClientNom            = p.Commande.Client != null ? p.Commande.Client.Nom : null,
                    ClientTelephone      = p.Commande.Client != null ? p.Commande.Client.Telephone : null,
                    CoutPrenom           = p.Couturier != null ? p.Couturier.Prenom : null,
                    CoutNom              = p.Couturier != null ? p.Couturier.Nom : null
                })
                .AsNoTracking()
                .ToListAsync();

            _cacheExpire = DateTime.Now.Add(_cacheDuree);
            return _cachePieces;
        }

        public void InvaliderCache() => _cachePieces = null;

        public async Task<List<AlerteRendezVous>> ObtenirAlertesActuelles()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirAlertesActuelles", _logger);
            var delaiHeures = await _parametresService.ObtenirDelaiAlerteRendezVousHeures();
            var maintenant = DateTime.Now;

            var pieces = await ObtenirPiecesActivesAsync();
            var alertes = new List<AlerteRendezVous>();

            foreach (var piece in pieces)
            {
                var dateRdv = piece.DateRdv;
                if (dateRdv <= maintenant) continue;

                if (piece.Statut == "A faire")
                {
                    var dateDepot = piece.CmdDateDebut.Date + piece.CmdHeureDebut;
                    var dureeTotal = dateRdv - dateDepot;
                    if (dureeTotal > TimeSpan.Zero)
                    {
                        var miTemps = dateDepot + TimeSpan.FromTicks(dureeTotal.Ticks / 2);
                        if (maintenant >= miTemps)
                            alertes.Add(ConstruireAlerteFromDto(piece, dateRdv, maintenant, "PasEncorePriseEnCharge"));
                    }
                }

                if (piece.Statut != "Terminee" && dateRdv <= maintenant.AddHours(delaiHeures))
                    alertes.Add(ConstruireAlerteFromDto(piece, dateRdv, maintenant, "RendezVousProche"));
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
                var dateRdv = piece.DateRdv;
                if (dateRdv <= maintenant) continue;
                resultat.Add(ConstruireAlerteFromDto(piece, dateRdv, maintenant, "RendezVousProche"));
            }
            return resultat.OrderBy(a => a.DateRendezVous).ToList();
        }

        public async Task<List<AlerteRendezVous>> ObtenirRendezVousSemaine()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirRendezVousSemaine", _logger);
            var maintenant = DateTime.Now;
            var finSemaine = maintenant.AddDays(7);

            var pieces = await ObtenirPiecesActivesAsync();
            var resultat = new List<AlerteRendezVous>();

            foreach (var piece in pieces)
            {
                var dateRdv = piece.DateRdv;
                bool pieceTerminee = piece.Statut == "Terminee";
                bool rdvSemaine    = dateRdv >= maintenant && dateRdv <= finSemaine;
                if (!pieceTerminee && !rdvSemaine) continue;

                var alerte = ConstruireAlerteFromDto(piece, dateRdv, maintenant, "RendezVousProche");
                alerte.ProposerContactWhatsApp = pieceTerminee && dateRdv.Date <= maintenant.Date;
                resultat.Add(alerte);
            }

            return resultat
                .OrderBy(a => a.ProposerContactWhatsApp ? 0 : 1)
                .ThenBy(a => a.DateRendezVous)
                .ToList();
        }

        public async Task<List<AlerteRendezVous>> ObtenirRetards()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirRetards", _logger);
            var maintenant = DateTime.Now;

            // Retards : exclut Terminee (travail fini, pas un retard de production)
            var pieces = (await ObtenirPiecesActivesAsync())
                .Where(p => p.Statut != "Terminee")
                .ToList();

            var resultat = new List<AlerteRendezVous>();
            foreach (var piece in pieces)
            {
                var dateRdv = piece.DateRdv;
                if (dateRdv >= maintenant) continue;

                var alerte = ConstruireAlerteFromDto(piece, dateRdv, maintenant, "Retard");
                alerte.ProposerContactWhatsApp = piece.Statut == "Terminee";
                resultat.Add(alerte);
            }

            return resultat.OrderBy(a => a.DateRendezVous).ToList();
        }

        // ── UNE seule requête pour toutes les sections ──
        public async Task<ToutesAlertesDto> ObtenirToutesAlertesAsync()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirToutesAlertesAsync", _logger);
            var delaiHeures = await _parametresService.ObtenirDelaiAlerteRendezVousHeures();
            var maintenant = DateTime.Now;
            var finSemaine = maintenant.AddDays(7);

            var pieces = await ObtenirPiecesActivesAsync();
            var dto = new ToutesAlertesDto();

            foreach (var piece in pieces)
            {
                var dateRdv = piece.DateRdv;
                bool rdvPasse = dateRdv <= maintenant;

                // ── Production : RDV futur ──
                if (!rdvPasse)
                {
                    if (piece.Statut == "A faire")
                    {
                        var dateDepot = piece.CmdDateDebut.Date + piece.CmdHeureDebut;
                        var dureeTotal = dateRdv - dateDepot;
                        if (dureeTotal > TimeSpan.Zero)
                        {
                            var miTemps = dateDepot + TimeSpan.FromTicks(dureeTotal.Ticks / 2);
                            if (maintenant >= miTemps)
                                dto.Production.Add(ConstruireAlerteFromDto(piece, dateRdv, maintenant, "PasEncorePriseEnCharge"));
                        }
                    }
                    if (piece.Statut != "Terminee" && dateRdv <= maintenant.AddHours(delaiHeures))
                        dto.Production.Add(ConstruireAlerteFromDto(piece, dateRdv, maintenant, "RendezVousProche"));
                }

                // ── Retrait : pièce terminée OU RDV dans la semaine ──
                bool pieceTerminee = piece.Statut == "Terminee";
                bool rdvSemaine = dateRdv >= maintenant && dateRdv <= finSemaine;
                if (pieceTerminee || rdvSemaine)
                {
                    var alerte = ConstruireAlerteFromDto(piece, dateRdv, maintenant, "RendezVousProche");
                    alerte.ProposerContactWhatsApp = pieceTerminee && dateRdv.Date <= maintenant.Date;
                    dto.Retrait.Add(alerte);
                }

                // ── Retards : RDV dépassé et pas Terminée ──
                if (!pieceTerminee && rdvPasse)
                {
                    var alerte = ConstruireAlerteFromDto(piece, dateRdv, maintenant, "Retard");
                    alerte.ProposerContactWhatsApp = false;
                    dto.Retards.Add(alerte);
                }
            }

            // ── Retours (requête séparée, légère) ──
            dto.Retours = await ObtenirAlertesRetours();

            return dto;
        }

        private static AlerteRendezVous ConstruireAlerteFromDto(
            PieceAlerteDto piece, DateTime dateRdv, DateTime maintenant, string typeAlerte)
        {
            var tempsRestant = dateRdv - maintenant;
            return new AlerteRendezVous
            {
                IdCommande = piece.IdCommande,
                IdPieceCommande = piece.IdPieceCommande,
                NomClient = piece.NomClient,
                Telephone = piece.ClientTelephone ?? "",
                TypeVetement = piece.TypeVetement,
                DateRendezVous = dateRdv,
                HeureRendezVous = dateRdv.ToString("HH:mm"),
                Statut = piece.StatutAffiche,
                TempsRestant = FormaterTempsRestant(tempsRestant),
                NomCouturier = piece.NomCouturier,
                EstUrgent = tempsRestant.TotalHours <= 1,
                ProposerContactWhatsApp = piece.Statut == "Terminee" && dateRdv <= maintenant,
                TypeAlerte = typeAlerte
            };
        }

        public async Task<List<AlerteRendezVous>> ObtenirAlertesRetours()
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirAlertesRetours", _logger);
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Projection : pas d'Include, pas de MotDePasse
            var retoursData = await context.Retours
                .Where(r => !r.EstAnnule && r.Statut != "Rendu" && r.DateRdvReprise.HasValue)
                .Select(r => new
                {
                    r.IdRetour,
                    r.IdCommande,
                    r.IdPieceCommande,
                    r.NumeroRetour,
                    r.Statut,
                    r.DateRdvReprise,
                    r.HeureDebutReprise,
                    TypeVetement = r.PieceCommande != null ? r.PieceCommande.TypeVetement : null,
                    ClientPrenom = r.Commande != null && r.Commande.Client != null
                        ? r.Commande.Client.Prenom : null,
                    ClientNom = r.Commande != null && r.Commande.Client != null
                        ? r.Commande.Client.Nom : null,
                    ClientTel = r.Commande != null && r.Commande.Client != null
                        ? r.Commande.Client.Telephone : null,
                    CoutReprisePrenom = r.CouturierReprise != null ? r.CouturierReprise.Prenom : null,
                    CoutRepriseNom    = r.CouturierReprise != null ? r.CouturierReprise.Nom : null,
                    CoutPrenom         = r.Couturier != null ? r.Couturier.Prenom : null,
                    CoutNom            = r.Couturier != null ? r.Couturier.Nom : null
                })
                .AsNoTracking()
                .ToListAsync();

            var alertes = new List<AlerteRendezVous>();
            var maintenant = DateTime.Now;

            foreach (var r in retoursData)
            {
                var dateRdv = r.DateRdvReprise!.Value;
                if (r.HeureDebutReprise.HasValue)
                    dateRdv = r.DateRdvReprise.Value.Date + r.HeureDebutReprise.Value;

                string nomCouturier = !string.IsNullOrEmpty(r.CoutReprisePrenom) || !string.IsNullOrEmpty(r.CoutRepriseNom)
                    ? $"{r.CoutReprisePrenom} {r.CoutRepriseNom}".Trim()
                    : !string.IsNullOrEmpty(r.CoutPrenom) || !string.IsNullOrEmpty(r.CoutNom)
                        ? $"{r.CoutPrenom} {r.CoutNom}".Trim()
                        : "(non assigné)";
                string nomClient = !string.IsNullOrEmpty(r.ClientPrenom) || !string.IsNullOrEmpty(r.ClientNom)
                    ? $"{r.ClientPrenom} {r.ClientNom}".Trim()
                    : "(client inconnen)";

                var tempsRestant = dateRdv - maintenant;

                alertes.Add(new AlerteRendezVous
                {
                    IdCommande       = r.IdCommande,
                    IdPieceCommande  = r.IdPieceCommande,
                    IdRetour         = r.IdRetour,
                    NumeroRetour     = r.NumeroRetour,
                    EstAlertRetour   = true,
                    NomClient        = nomClient,
                    Telephone        = r.ClientTel ?? "",
                    TypeVetement     = r.TypeVetement ?? "—",
                    DateRendezVous   = dateRdv,
                    HeureRendezVous  = dateRdv.ToString("HH:mm"),
                    Statut           = r.Statut,
                    TempsRestant     = FormaterTempsRestant(tempsRestant),
                    NomCouturier     = nomCouturier,
                    EstUrgent        = tempsRestant.TotalHours <= 1,
                    ProposerContactWhatsApp = false,
                    TypeAlerte       = "Retour"
                });
            }

            return alertes.OrderBy(a => a.DateRendezVous).ToList();
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
