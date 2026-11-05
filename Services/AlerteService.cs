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
                .Where(p => p.CommandeActive
                         && p.Statut != "Livree"
                         && p.Commande != null
                         && p.Commande.EstSupprimee == false)
                .OrderBy(p => p.DateRdvEffective)
                .Take(500) // API historique; l'écran utilise ObtenirSectionAsync (pagination SQL).
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

            return alertes.OrderBy(a => a.DateRendezVous).Take(50).ToList();
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
            return resultat.OrderBy(a => a.DateRendezVous).Take(50).ToList();
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
                .Take(50)
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

            return resultat.OrderBy(a => a.DateRendezVous).Take(50).ToList();
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

            dto.Production = dto.Production.OrderBy(a => a.DateRendezVous).Take(50).ToList();
            dto.Retrait = dto.Retrait.OrderBy(a => a.DateRendezVous).Take(50).ToList();
            dto.Retards = dto.Retards.OrderBy(a => a.DateRendezVous).Take(50).ToList();
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

        public async Task<List<AlerteRendezVous>> ObtenirAlertesRetours(int max = 50)
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirAlertesRetours", _logger);
            await using var context = await _contextFactory.CreateDbContextAsync();

            // Projection : pas d'Include, pas de MotDePasse
            var retoursData = await context.Retours
                .Where(r => !r.EstAnnule && r.Statut != "Rendu" && r.DateRdvReprise.HasValue
                         && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee)
                .OrderBy(r => r.DateRdvReprise)
                .Take(Math.Clamp(max, 1, 500))
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

        // ════════════════════════════════════════════════════════════════════
        // ÉTAPE 2 PERF — Nouvelle API SQL (COUNT + pagination bornée à 50)
        // ════════════════════════════════════════════════════════════════════
        // Règles d'affectation (conditions SQL exclusives sur DateRdvEffective) :
        //   Retards    : CommandeActive && Statut IN ('A faire','En cours') && DateRdvEffective < maintenant
        //   Prets      : CommandeActive && Statut = 'Terminee'
        //   Aujourdhui : CommandeActive && Statut NOT IN ('Terminee','Livree') && DateRdvEffective ∈ [debut_jour, debut_demain[
        //   Imminentes : CommandeActive && Statut NOT IN ('Terminee','Livree','A faire','En cours en retard')
        //                 && DateRdvEffective ∈ [maintenant, +7j]
        //   (Prêts remplace la notion de Retrait; Retards > Prêts > Aujourd'hui > Imminentes)

        public async Task<ResumeAlertesDto> ObtenirResumeAsync(CancellationToken cancellationToken = default)
        {
            using var t = PerfLogger.Start("AlerteService.ObtenirResumeAsync", _logger);
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var maintenant = DateTime.Now;
            var debutJour  = maintenant.Date;
            var debutDemain = debutJour.AddDays(1);
            var dans7j      = debutJour.AddDays(7);

            int delaiH = await _parametresService.ObtenirDelaiAlerteRendezVousHeures();
            var seuilImminent = maintenant.AddHours(delaiH);

            // ── Compteurs SQL (aucune entité chargée) ────────────────────────
            var nbRetards = await ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && (p.Statut == "A faire" || p.Statut == "En cours" || p.Statut == "Terminee")
                         && p.DateRdvEffective < maintenant)
                .Select(p => p.IdCommande)
                .Distinct()
                .CountAsync(cancellationToken);

            var nbPrets = await ctx.PiecesCommande
                .Where(p => p.CommandeActive && p.Statut == "Terminee" && p.DateRdvEffective >= maintenant)
                .Select(p => p.IdCommande)
                .Distinct()
                .CountAsync(cancellationToken);

            var nbAujourdhui = await ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && p.Statut != "Terminee" && p.Statut != "Livree"
                         && p.DateRdvEffective >= maintenant
                         && p.DateRdvEffective < debutDemain)
                .Select(p => p.IdCommande)
                .Distinct()
                .CountAsync(cancellationToken);

            var nbImminentes = await ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && p.Statut != "Terminee" && p.Statut != "Livree"
                         && ((p.DateRdvEffective >= debutDemain && p.DateRdvEffective <= dans7j)
                             || (p.Statut == "A faire" && p.DateMiParcours <= maintenant
                                 && p.DateRdvEffective > dans7j)))
                .Select(p => p.IdCommande)
                .Distinct()
                .CountAsync(cancellationToken);

            // Risque de retard : pièces non terminées avec DateRdvEffective <= seuilImminent,
            // excluant celles dont le suivi existe avec NbPreventions > 0 pour ce RDV.
            // NOT EXISTS via LEFT JOIN sur SuivisAlerteRetard
            var nbRisque = await ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && p.Statut != "Terminee" && p.Statut != "Livree"
                         && p.DateRdvEffective <= seuilImminent
                         && !ctx.SuivisAlerteRetard.Any(s =>
                             s.TypeCible == (p.RendezVousException.HasValue ? "Piece" : "Commande")
                             && s.IdCible == (p.RendezVousException.HasValue ? p.IdPieceCommande : p.IdCommande)
                             && s.NbPreventions > 0
                             && s.DateRdvTraite >= p.DateRdvEffective.AddMinutes(-1)
                             && s.DateRdvTraite < p.DateRdvEffective.AddMinutes(1)))
                .Select(p => new { p.IdCommande, p.IdPieceCommande, p.DateRdvEffective })
                .Distinct()
                .CountAsync(cancellationToken);
            int nbRisqueRetours = await ctx.Retours.CountAsync(r => !r.EstAnnule
                && r.Statut != "Rendu" && r.Statut != "Termine" && r.Statut != "Pret"
                && r.DateRdvReprise.HasValue && r.DateRdvReprise <= seuilImminent
                && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee
                && !ctx.SuivisAlerteRetard.Any(s => s.TypeCible == RetardAlerteService.CibleRetour
                    && s.IdCible == r.IdRetour && s.NbPreventions > 0
                    && s.DateRdvTraite >= r.DateRdvReprise!.Value.AddMinutes(-1)
                    && s.DateRdvTraite < r.DateRdvReprise.Value.AddMinutes(1)), cancellationToken);

            int nbRetardsRetours = await ctx.Retours.CountAsync(r => !r.EstAnnule
                && r.Statut != "Rendu" && r.Statut != "Termine" && r.Statut != "Pret"
                && r.DateRdvReprise.HasValue && r.DateRdvReprise < debutJour
                && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee, cancellationToken);
            int nbPretsRetours = await ctx.Retours.CountAsync(r => !r.EstAnnule
                && (r.Statut == "Termine" || r.Statut == "Pret") && r.DateRdvReprise.HasValue
                && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee, cancellationToken);
            int nbAujourdhuiRetours = await ctx.Retours.CountAsync(r => !r.EstAnnule
                && r.Statut != "Rendu" && r.Statut != "Termine" && r.Statut != "Pret"
                && r.DateRdvReprise >= debutJour && r.DateRdvReprise < debutDemain
                && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee, cancellationToken);
            int nbImminentesRetours = await ctx.Retours.CountAsync(r => !r.EstAnnule
                && r.Statut != "Rendu" && r.Statut != "Termine" && r.Statut != "Pret"
                && r.DateRdvReprise >= debutDemain && r.DateRdvReprise <= dans7j
                && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee, cancellationToken);

            return new ResumeAlertesDto
            {
                NbRetards     = nbRetards + nbRetardsRetours,
                NbPrets       = nbPrets + nbPretsRetours,
                NbAujourdhui  = nbAujourdhui + nbAujourdhuiRetours,
                NbImminentes  = nbImminentes + nbImminentesRetours,
                NbRisqueRetard = nbRisque + nbRisqueRetours,
                NbBadgeMenu   = nbRetards + nbAujourdhui
            };
        }

        public async Task<PagedResult<AlerteCommandeVm>> ObtenirSectionAsync(
            SectionAlerte section, int page, int taille = 50,
            CancellationToken cancellationToken = default)
        {
            using var t = PerfLogger.Start($"AlerteService.ObtenirSectionAsync({section})", _logger);
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var maintenant = DateTime.Now;
            var debutJour   = maintenant.Date;
            var debutDemain = debutJour.AddDays(1);
            var dans7j      = debutJour.AddDays(7);

            // ── Filtre SQL selon section (conditions exclusives) ──────────────
            IQueryable<PieceCommande> query;
            switch (section)
            {
                case SectionAlerte.Retards:
                    query = ctx.PiecesCommande
                        .Where(p => p.CommandeActive
                                 && (p.Statut == "A faire" || p.Statut == "En cours" || p.Statut == "Terminee")
                                 && p.DateRdvEffective < maintenant);
                    break;
                case SectionAlerte.Prets:
                    query = ctx.PiecesCommande
                        .Where(p => p.CommandeActive && p.Statut == "Terminee"
                                 && p.DateRdvEffective >= maintenant);
                    break;
                case SectionAlerte.Aujourdhui:
                    query = ctx.PiecesCommande
                        .Where(p => p.CommandeActive
                                 && p.Statut != "Terminee" && p.Statut != "Livree"
                                 && p.DateRdvEffective >= maintenant
                                 && p.DateRdvEffective < debutDemain);
                    break;
                case SectionAlerte.Imminentes:
                    query = ctx.PiecesCommande
                        .Where(p => p.CommandeActive
                                 && p.Statut != "Terminee" && p.Statut != "Livree"
                                 && ((p.DateRdvEffective >= debutDemain && p.DateRdvEffective <= dans7j)
                                     || (p.Statut == "A faire" && p.DateMiParcours <= maintenant
                                         && p.DateRdvEffective > dans7j)));
                    break;
                default: // RisqueRetard — géré par IRetardAlerteService
                    return new PagedResult<AlerteCommandeVm>
                    { Items = new(), TotalCount = 0, Page = page, PageSize = taille };
            }

            // ── COUNT SQL ────────────────────────────────────────────────────
            int totalCommandes = await query.AsNoTracking().Select(p => p.IdCommande)
                .Distinct().CountAsync(cancellationToken);

            var idsCommandesPage = await query.AsNoTracking()
                .GroupBy(p => p.IdCommande)
                .Select(g => new { IdCommande = g.Key, Date = g.Min(p => p.DateRdvEffective) })
                .OrderBy(x => x.Date)
                .Skip((page - 1) * taille)
                .Take(page * taille)
                .Select(x => x.IdCommande)
                .ToListAsync(cancellationToken);

            // ── Projection paginée (ORDER BY + LIMIT) ───────────────────────
            var pieces = await query
                .Where(p => idsCommandesPage.Contains(p.IdCommande))
                .AsNoTracking()
                .OrderBy(p => p.DateRdvEffective)
                .Select(p => new
                {
                    p.IdCommande,
                    p.IdPieceCommande,
                    p.TypeVetement,
                    p.Statut,
                    p.DateRdvEffective,
                    NomClient   = p.Commande != null && p.Commande.Client != null
                        ? p.Commande.Client.Prenom + " " + p.Commande.Client.Nom : "?",
                    Telephone   = p.Commande != null && p.Commande.Client != null
                        ? p.Commande.Client.Telephone : "",
                    NomCouturier = p.Couturier != null
                        ? p.Couturier.Prenom + " " + p.Couturier.Nom : "(non assigné)"
                })
                .ToListAsync(cancellationToken);

            // ── Regroupement par commande (sur la seule page ≤ 50) ──────────
            var libelleBouton = section switch
            {
                SectionAlerte.Prets      => "💬 Prévenir : commande prête",
                SectionAlerte.Retards    => "💬 Relancer le client",
                SectionAlerte.Aujourdhui => "💬 Rappel RDV",
                _                        => ""
            };

            var cartes = pieces
                .GroupBy(p => p.IdCommande)
                .Select(g =>
                {
                    var first = g.OrderBy(x => x.DateRdvEffective).First();
                    string statutAffiche = first.Statut switch
                    {
                        "A faire"  => "À faire",
                        "En cours" => "En cours",
                        "Terminee" => "Terminée",
                        "Livree"   => "Livrée",
                        _          => first.Statut
                    };
                    string detail = string.Join(" • ", g.Select(x =>
                    {
                        string s = x.Statut switch
                        { "A faire" => "À faire", "En cours" => "En cours",
                          "Terminee" => "Terminée", "Livree" => "Livrée", _ => x.Statut };
                        return $"{x.TypeVetement} ({s})";
                    }));
                    return new AlerteCommandeVm
                    {
                        IdCommande           = g.Key,
                        NomClient            = first.NomClient,
                        Telephone            = first.Telephone ?? "",
                        DateEcheance         = first.DateRdvEffective,
                        HeureRdv             = first.DateRdvEffective.ToString("HH:mm"),
                        DetailPieces         = detail,
                        LibelleBoutonWhatsApp = libelleBouton,
                        IdsPiecesCommande    = g.Select(x => x.IdPieceCommande).ToList()
                    };
                })
                .OrderBy(c => c.DateEcheance)
                .ToList();

            var retoursQuery = ctx.Retours.AsNoTracking()
                .Where(r => !r.EstAnnule && r.Statut != "Rendu" && r.DateRdvReprise.HasValue
                         && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee);
            retoursQuery = section switch
            {
                SectionAlerte.Retards => retoursQuery.Where(r => r.Statut != "Termine" && r.Statut != "Pret" && r.DateRdvReprise < maintenant),
                SectionAlerte.Prets => retoursQuery.Where(r => r.Statut == "Termine" || r.Statut == "Pret"),
                SectionAlerte.Aujourdhui => retoursQuery.Where(r => r.Statut != "Termine" && r.Statut != "Pret" && r.DateRdvReprise >= debutJour && r.DateRdvReprise < debutDemain),
                SectionAlerte.Imminentes => retoursQuery.Where(r => r.Statut != "Termine" && r.Statut != "Pret" && r.DateRdvReprise >= debutDemain && r.DateRdvReprise <= dans7j),
                _ => retoursQuery.Where(r => false)
            };
            int totalRetours = await retoursQuery.CountAsync(cancellationToken);
            var retourDtos = await retoursQuery.OrderBy(r => r.DateRdvReprise)
                .Take(page * taille)
                .Select(r => new
                {
                    r.IdRetour, r.IdCommande, r.IdPieceCommande, r.NumeroRetour, r.Statut,
                    r.DateRdvReprise, r.HeureDebutReprise,
                    TypeVetement = r.PieceCommande != null ? r.PieceCommande.TypeVetement : "Retour",
                    ClientNom = r.Commande!.Client != null ? r.Commande.Client.Nom : "",
                    ClientPrenom = r.Commande!.Client != null ? r.Commande.Client.Prenom : "",
                    Telephone = r.Commande!.Client != null ? r.Commande.Client.Telephone : "",
                    CouturierNom = r.CouturierReprise != null ? r.CouturierReprise.Nom : "",
                    CouturierPrenom = r.CouturierReprise != null ? r.CouturierReprise.Prenom : ""
                }).ToListAsync(cancellationToken);
            var cartesRetours = retourDtos.Select(r =>
            {
                var date = r.DateRdvReprise!.Value;
                if (r.HeureDebutReprise.HasValue) date = date.Date + r.HeureDebutReprise.Value;
                return new AlerteCommandeVm
                {
                    IdCommande = r.IdCommande,
                    NomClient = $"{r.ClientPrenom} {r.ClientNom}".Trim(),
                    Telephone = r.Telephone ?? "",
                    DateEcheance = date,
                    HeureRdv = date.ToString("HH:mm"),
                    DetailPieces = $"🔄 RETOUR {r.NumeroRetour} — {r.TypeVetement} ({r.Statut}) — {(string.IsNullOrWhiteSpace(r.CouturierNom) ? "(non assigné)" : $"{r.CouturierPrenom} {r.CouturierNom}".Trim())}",
                    EstAlertRetour = true,
                    IdRetour = r.IdRetour,
                    NumeroRetour = r.NumeroRetour
                };
            }).OrderBy(r => r.DateEcheance).ToList();
            cartes = cartes.Concat(cartesRetours).OrderBy(c => c.DateEcheance)
                .Skip((page - 1) * taille).Take(taille).ToList();

            // Borne sur le nombre de cartes affichées (une commande = 1 carte)
            return new PagedResult<AlerteCommandeVm>
            {
                Items      = cartes,
                TotalCount = totalCommandes + totalRetours,
                Page       = page,
                PageSize   = taille
            };
        }

        public async Task<List<AlerteCommandeVm>> ObtenirAlertesUrgentesAsync(
            CancellationToken cancellationToken = default)
        {
            var maintenant = DateTime.Now;
            var seuil = maintenant.AddHours(1);
            await using var ctx = await _contextFactory.CreateDbContextAsync(cancellationToken);
            var pieces = await ctx.PiecesCommande.AsNoTracking()
                .Where(p => p.CommandeActive && p.Statut != "Livree" && p.DateRdvEffective <= seuil)
                .OrderBy(p => p.DateRdvEffective).Take(20)
                .Select(p => new AlerteCommandeVm
                {
                    IdCommande = p.IdCommande,
                    NomClient = p.Commande != null && p.Commande.Client != null
                        ? p.Commande.Client.Prenom + " " + p.Commande.Client.Nom : "?",
                    Telephone = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Telephone : "",
                    DateEcheance = p.DateRdvEffective,
                    HeureRdv = "",
                    DetailPieces = $"{p.TypeVetement} ({p.Statut})"
                }).ToListAsync(cancellationToken);
            var retours = await ctx.Retours.AsNoTracking()
                .Where(r => !r.EstAnnule && r.Statut != "Rendu" && r.DateRdvReprise.HasValue
                         && r.DateRdvReprise <= seuil && r.Commande != null
                         && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee)
                .OrderBy(r => r.DateRdvReprise).Take(20)
                .Select(r => new
                {
                    r.IdRetour, r.IdCommande, r.NumeroRetour, r.Statut, r.DateRdvReprise, r.HeureDebutReprise,
                    Nom = r.Commande!.Client != null ? r.Commande.Client.Prenom + " " + r.Commande.Client.Nom : "?",
                    Tel = r.Commande!.Client != null ? r.Commande.Client.Telephone : "",
                    Vetement = r.PieceCommande != null ? r.PieceCommande.TypeVetement : "Retour"
                }).ToListAsync(cancellationToken);
            pieces.ForEach(p => p.HeureRdv = p.DateEcheance.ToString("HH:mm"));
            pieces.AddRange(retours.Select(r =>
            {
                var date = r.DateRdvReprise!.Value;
                if (r.HeureDebutReprise.HasValue) date = date.Date + r.HeureDebutReprise.Value;
                return new AlerteCommandeVm
                {
                    IdCommande = r.IdCommande, NomClient = r.Nom, Telephone = r.Tel ?? "",
                    DateEcheance = date, HeureRdv = date.ToString("HH:mm"),
                    DetailPieces = $"🔄 RETOUR {r.NumeroRetour} — {r.Vetement} ({r.Statut})",
                    EstAlertRetour = true, IdRetour = r.IdRetour, NumeroRetour = r.NumeroRetour
                };
            }).Where(r => r.DateEcheance <= seuil));
            return pieces.OrderBy(p => p.DateEcheance).Take(20).ToList();
        }
    }
}
