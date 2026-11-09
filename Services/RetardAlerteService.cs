using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class RetardAlerteService : IRetardAlerteService
    {
        private sealed class RisquePieceDto
        {
            public int IdPieceCommande { get; set; }
            public int IdCommande { get; set; }
            public string TypeVetement { get; set; } = "";
            public string Statut { get; set; } = "";
            public DateTime? RendezVousException { get; set; }
            public DateTime DateRdvEffective { get; set; }
            public int CmdIdClient { get; set; }
            public string? ClientPrenom { get; set; }
            public string? ClientNom { get; set; }
            public string? ClientTel { get; set; }
            public string? CoutPrenom { get; set; }
            public string? CoutNom { get; set; }
        }

        private sealed class RisqueRetourDto
        {
            public int IdRetour { get; set; }
            public int IdCommande { get; set; }
            public int IdPieceCommande { get; set; }
            public string? NumeroRetour { get; set; }
            public string Statut { get; set; } = "";
            public DateTime? DateRdvReprise { get; set; }
            public TimeSpan? HeureDebutReprise { get; set; }
            public TimeSpan? HeureFinReprise { get; set; }
            public int IdClient { get; set; }
            public string? Nom { get; set; }
            public string? Telephone { get; set; }
            public string? Vetement { get; set; }
            public string? Couturier { get; set; }
        }

        public const string CibleCommande = "Commande";
        public const string CiblePiece    = "Piece";
        public const string CibleRetour   = "Retour";

        public const string ActionPrevention = "CLIENT_PREVENU_RETARD";
        public const string ActionReport     = "RDV_REPORTE";

        private readonly IDbContextFactory<ApplicationDbContext> _factory;
        private readonly IParametresService _parametres;
        private readonly IAuditService _audit;

        public RetardAlerteService(
            IDbContextFactory<ApplicationDbContext> factory,
            IParametresService parametres,
            IAuditService audit)
        {
            _factory    = factory;
            _parametres = parametres;
            _audit      = audit;
        }

        public static DateTime TronquerMinute(DateTime dt) =>
            new(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);

        public static bool MemeRdv(DateTime a, DateTime b) =>
            Math.Abs((TronquerMinute(a) - TronquerMinute(b)).TotalMinutes) < 1;

        public static DateTime CalculerEcheancePiece(PieceCommande piece, Commande commande)
        {
            // ÉTAPE 1 PERF — formule unifiée : HeureFin ?? 17:00 (pas HeureDebut)
            return DateRdvHelper.CalculerDateRdvEffective(
                piece.RendezVousException, commande.DateFin, commande.HeureFin);
        }

        public static DateTime CalculerEcheanceRetour(Retour ret)
        {
            var date = ret.DateRdvReprise ?? ret.DateSignalement;
            var heure = ret.HeureFinReprise ?? ret.HeureDebutReprise;
            return heure.HasValue ? date.Date + heure.Value : date;
        }

        public static (string type, int id) CleCiblePiece(PieceCommande piece) =>
            piece.RendezVousException.HasValue
                ? (CiblePiece, piece.IdPieceCommande)
                : (CibleCommande, piece.IdCommande);

        /// <summary>
        /// Exclut les pièces dont le client a déjà été prévenu pour le RDV courant.
        /// Les deux branches distinctes évitent un CASE corrélé sur les colonnes de
        /// clé et la comparaison exacte est possible car DateRdvEffective et
        /// DateRdvTraite sont normalisées à la minute.
        /// </summary>
        public static IQueryable<PieceCommande> ExclurePiecesDejaPrevenues(
            IQueryable<PieceCommande> pieces,
            IQueryable<SuiviAlerteRetard> suivis)
        {
            var commandes = pieces.Where(p => !p.RendezVousException.HasValue
                && !suivis.Any(s => s.TypeCible == CibleCommande
                    && s.IdCible == p.IdCommande
                    && s.NbPreventions > 0
                    && s.DateRdvTraite == p.DateRdvEffective));

            var piecesException = pieces.Where(p => p.RendezVousException.HasValue
                && !suivis.Any(s => s.TypeCible == CiblePiece
                    && s.IdCible == p.IdPieceCommande
                    && s.NbPreventions > 0
                    && s.DateRdvTraite == p.DateRdvEffective));

            return commandes.Concat(piecesException);
        }

        public string GenererMessageExcuse(
            string modele,
            string client,
            string numeroCommande,
            string typeVetement,
            DateTime nouvelleDate,
            string atelier)
        {
            if (string.IsNullOrWhiteSpace(modele))
                modele = ParametresService.ModeleExcuseRetardDefaut;

            return modele
                .Replace("{client}", client ?? "")
                .Replace("{numero_commande}", numeroCommande ?? "")
                .Replace("{type_vetement}", typeVetement ?? "")
                .Replace("{nouvelle_date}", nouvelleDate.ToString("dd/MM/yyyy"))
                .Replace("{atelier}", atelier ?? "");
        }

        public bool NumeroClientValide(string? telephone)
        {
            if (string.IsNullOrWhiteSpace(telephone)) return false;
            try
            {
                string n = System.Text.RegularExpressions.Regex.Replace(telephone.Trim(), @"[^\d+]", "");
                n = n.TrimStart('+');
                if (n.StartsWith("00")) n = n[2..];
                return n.Length >= 8 && n.All(char.IsDigit);
            }
            catch { return false; }
        }

        public async Task<List<AlerteRendezVous>> ObtenirAlertesRisqueRetardAsync(DateTime? maintenant = null)
        {
            var page = await ObtenirAlertesRisqueRetardPageCoreAsync(1, 50, maintenant, default, false);
            return page.Items;
        }
        public async Task<BrouillonMessageExcuse> PreparerMessageExcuseAsync(
            string typeCible,
            int idCible,
            DateTime dateRdvActuelle,
            DateTime? maintenant = null)
        {
            int jours = await _parametres.ObtenirRetardReportJours();
            if (jours < 1) jours = ParametresService.RetardReportJoursDefaut;
            string modele  = await _parametres.ObtenirRetardMessageModele();
            string atelier = await _parametres.ObtenirNomAtelier();

            await using var ctx = await _factory.CreateDbContextAsync();
            var nouvelle = dateRdvActuelle.AddDays(jours);

            string client = "";
            string numero = "";
            string typeVet = "";
            string tel = "";
            int idClient = 0;
            int nbPrevenu = 0;

            var suivi = await ctx.SuivisAlerteRetard
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.TypeCible == typeCible && s.IdCible == idCible);
            nbPrevenu = suivi?.NbPreventions ?? 0;

            if (typeCible == CibleRetour)
            {
                var ret = await ctx.Retours
                    .Include(r => r.Commande).ThenInclude(c => c!.Client)
                    .Include(r => r.PieceCommande)
                    .FirstOrDefaultAsync(r => r.IdRetour == idCible)
                    ?? throw new InvalidOperationException("Retour introuvable.");
                client  = ret.Commande?.Client != null
                    ? $"{ret.Commande.Client.Prenom} {ret.Commande.Client.Nom}".Trim()
                    : "";
                numero  = string.IsNullOrWhiteSpace(ret.NumeroRetour)
                    ? ret.IdRetour.ToString()
                    : ret.NumeroRetour;
                typeVet = ret.PieceCommande?.TypeVetement ?? "vêtement";
                tel     = ret.Commande?.Client?.Telephone ?? "";
                idClient = ret.Commande?.IdClient ?? 0;
            }
            else if (typeCible == CiblePiece)
            {
                var piece = await ctx.PiecesCommande
                    .Include(p => p.Commande).ThenInclude(c => c!.Client)
                    .FirstOrDefaultAsync(p => p.IdPieceCommande == idCible)
                    ?? throw new InvalidOperationException("Pièce introuvable.");
                client   = piece.Commande?.Client != null
                    ? $"{piece.Commande.Client.Prenom} {piece.Commande.Client.Nom}".Trim()
                    : "";
                numero   = piece.IdCommande.ToString();
                typeVet  = piece.TypeVetement;
                tel      = piece.Commande?.Client?.Telephone ?? "";
                idClient = piece.Commande?.IdClient ?? 0;
            }
            else
            {
                var cmd = await ctx.Commandes
                    .Include(c => c.Client)
                    .Include(c => c.Pieces)
                    .FirstOrDefaultAsync(c => c.IdCommande == idCible)
                    ?? throw new InvalidOperationException("Commande introuvable.");
                client   = cmd.Client != null ? $"{cmd.Client.Prenom} {cmd.Client.Nom}".Trim() : "";
                numero   = cmd.IdCommande.ToString();
                typeVet  = cmd.Pieces.Count == 1
                    ? cmd.Pieces[0].TypeVetement
                    : string.Join(", ", cmd.Pieces.Select(p => p.TypeVetement));
                tel      = cmd.Client?.Telephone ?? "";
                idClient = cmd.IdClient;
            }

            return new BrouillonMessageExcuse
            {
                Message              = GenererMessageExcuse(modele, client, numero, typeVet, nouvelle, atelier),
                DateRdvActuelle      = dateRdvActuelle,
                NouvelleDateProposee = nouvelle,
                TypeCible            = typeCible,
                IdCible              = idCible,
                IdClient             = idClient,
                Telephone            = tel,
                NbFoisPrevenu        = nbPrevenu
            };
        }

        public async Task EnregistrerPreventionAsync(
            string typeCible,
            int idCible,
            DateTime dateRdvTraite,
            int idClient,
            int idOperateur,
            string nomOperateur)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var operateur = ctx.Employes.Find(idOperateur);
            AuthorizationHelper.RequireRoleEnum(operateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            var suivi = await ctx.SuivisAlerteRetard
                .FirstOrDefaultAsync(s => s.TypeCible == typeCible && s.IdCible == idCible);

            var rdv = TronquerMinute(dateRdvTraite);
            if (suivi == null)
            {
                suivi = new SuiviAlerteRetard
                {
                    TypeCible = typeCible,
                    IdCible   = idCible,
                    IdClient  = idClient
                };
                ctx.SuivisAlerteRetard.Add(suivi);
            }

            suivi.IdClient = idClient;
            suivi.DateRdvTraite = rdv;
            suivi.NbPreventions++;
            suivi.DateDernierMessage = DateTime.Now;
            suivi.IdOperateurDernierMessage = idOperateur;
            suivi.NomOperateurDernierMessage = nomOperateur;

            await ctx.SaveChangesAsync();

            await _audit.EnregistrerActionAsync(
                idOperateur, nomOperateur, operateur!.Role,
                ActionPrevention, typeCible, idCible,
                valeursAvant: new { DateRdv = rdv, NbPreventions = suivi.NbPreventions - 1 },
                valeursApres: new { DateRdv = rdv, NbPreventions = suivi.NbPreventions },
                motif: "Message d'excuse retard envoyé au client");
        }

        public async Task ReporterRendezVousAsync(
            string typeCible,
            int idCible,
            DateTime dateRdvActuelle,
            int idOperateur,
            string nomOperateur,
            DateTime? maintenant = null)
        {
            await using var ctx = await _factory.CreateDbContextAsync();
            var operateur = ctx.Employes.Find(idOperateur);
            AuthorizationHelper.RequireRoleEnum(operateur, RoleEmploye.Boss, RoleEmploye.Secretaire);

            int jours = await _parametres.ObtenirRetardReportJours();
            if (jours < 1) jours = ParametresService.RetardReportJoursDefaut;

            DateTime ancienne;
            DateTime nouvelle;
            object? snapshotStatut = null;
            int idClient = 0;
            int idEntiteAudit = idCible;
            string entiteAudit = typeCible;

            if (typeCible == CibleRetour)
            {
                var ret = await ctx.Retours.Include(r => r.Commande)
                    .FirstOrDefaultAsync(r => r.IdRetour == idCible)
                    ?? throw new InvalidOperationException("Retour introuvable.");
                snapshotStatut = new { ret.Statut };
                ancienne = CalculerEcheanceRetour(ret);
                nouvelle = ancienne.AddDays(jours);
                if (ret.DateRdvReprise.HasValue)
                    ret.DateRdvReprise = ret.DateRdvReprise.Value.Date.AddDays(jours)
                                         + ret.DateRdvReprise.Value.TimeOfDay;
                else
                    ret.DateRdvReprise = nouvelle.Date;
                idClient = ret.Commande?.IdClient ?? 0;
                idEntiteAudit = ret.IdRetour;
                entiteAudit = "Retour";
            }
            else if (typeCible == CiblePiece)
            {
                var piece = await ctx.PiecesCommande
                    .Include(p => p.Commande)
                    .FirstOrDefaultAsync(p => p.IdPieceCommande == idCible)
                    ?? throw new InvalidOperationException("Pièce introuvable.");
                snapshotStatut = new { piece.Statut, piece.IdCommission, Montant = piece.MontantCouture };
                ancienne = piece.RendezVousException ?? CalculerEcheancePiece(piece, piece.Commande!);
                nouvelle = ancienne.AddDays(jours);
                piece.RendezVousException = nouvelle;
                // ÉTAPE 1 PERF — maintenir DateRdvEffective (RendezVousException = prioritaire)
                piece.DateRdvEffective = nouvelle;
                idClient = piece.Commande?.IdClient ?? 0;
                idEntiteAudit = piece.IdCommande;
                entiteAudit = "Commande";
            }
            else
            {
                var cmd = await ctx.Commandes
                    .Include(c => c.Pieces)
                    .FirstOrDefaultAsync(c => c.IdCommande == idCible)
                    ?? throw new InvalidOperationException("Commande introuvable.");
                snapshotStatut = new
                {
                    Statuts = cmd.Pieces.Select(p => p.Statut).ToList(),
                    Commissions = cmd.Pieces.Select(p => p.IdCommission).ToList()
                };
                ancienne = DateRdvHelper.CalculerDateRdvEffective(null, cmd.DateFin, cmd.HeureFin);
                nouvelle = ancienne.AddDays(jours);
                cmd.DateFin = cmd.DateFin.Date.AddDays(jours);
                // ÉTAPE 1 PERF — recalculer DateRdvEffective pour toutes les pièces
                foreach (var p in cmd.Pieces)
                {
                    p.DateRdvEffective = DateRdvHelper.CalculerDateRdvEffective(
                        p.RendezVousException, cmd.DateFin, cmd.HeureFin);
                }
                idClient = cmd.IdClient;
                idEntiteAudit = cmd.IdCommande;
                entiteAudit = "Commande";
            }

            var suivi = await ctx.SuivisAlerteRetard
                .FirstOrDefaultAsync(s => s.TypeCible == typeCible && s.IdCible == idCible);
            if (suivi == null)
            {
                suivi = new SuiviAlerteRetard
                {
                    TypeCible = typeCible,
                    IdCible   = idCible,
                    IdClient  = idClient,
                    DateRdvTraite = TronquerMinute(dateRdvActuelle),
                    NbPreventions = 1,
                    DateDernierMessage = DateTime.Now,
                    IdOperateurDernierMessage = idOperateur,
                    NomOperateurDernierMessage = nomOperateur
                };
                ctx.SuivisAlerteRetard.Add(suivi);
            }

            suivi.IdClient = idClient;
            suivi.NbReports++;
            suivi.AncienneDateDernierReport = ancienne;
            suivi.NouvelleDateDernierReport = nouvelle;
            suivi.DateDernierReport = DateTime.Now;
            // L'alerte traitée reste liée à l'ANCIEN RDV : le nouveau pourra re-alerter.
            suivi.DateRdvTraite = TronquerMinute(dateRdvActuelle);
            if (suivi.NbPreventions == 0)
            {
                suivi.NbPreventions = 1;
                suivi.DateDernierMessage = DateTime.Now;
                suivi.IdOperateurDernierMessage = idOperateur;
                suivi.NomOperateurDernierMessage = nomOperateur;
            }

            await ctx.SaveChangesAsync();

            await _audit.EnregistrerActionAsync(
                idOperateur, nomOperateur, operateur!.Role,
                ActionReport, entiteAudit, idEntiteAudit,
                valeursAvant: new { DateRdv = ancienne, Snapshot = snapshotStatut },
                valeursApres: new { DateRdv = nouvelle, Jours = jours, NbReports = suivi.NbReports },
                motif: $"Report de RDV de {jours} jour(s) après prévention client");
        }

        public async Task<StatsRetardDashboard> ObtenirStatsDashboardAsync(DateTime? maintenant = null)
        {
            var now = maintenant ?? DateTime.Now;
            var debutMois    = new DateTime(now.Year, now.Month, 1);
            var debutMoisUtc = debutMois.ToUniversalTime();

            await using var ctx = await _factory.CreateDbContextAsync();
            // Les tests métier utilisent EF InMemory, qui ne fournit pas de connexion
            // SQL. Garder le même calcul et les mêmes limites sans appeler l'API relationnelle.
            if (!ctx.Database.IsRelational())
            {
                var candidats = ctx.PiecesCommande.AsNoTracking()
                    .Where(p => p.CommandeActive
                        && (p.Statut == "A faire" || p.Statut == "En cours")
                        && p.DateRdvEffective <= now);
                var idsRetard = await ExclurePiecesDejaPrevenues(candidats, ctx.SuivisAlerteRetard)
                    .Select(p => p.IdCommande).Distinct().Take(1000).ToListAsync();
                var reportsInMemory = await ctx.JournalAudit.AsNoTracking()
                    .CountAsync(j => j.TypeAction == ActionReport && j.DateHeureUtc >= debutMoisUtc);
                var clientsInMemory = await ctx.SuivisAlerteRetard.AsNoTracking()
                    .Where(s => s.NbPreventions > 0 && s.DateDernierMessage >= debutMois && s.IdClient > 0)
                    .Select(s => s.IdClient).Distinct().CountAsync();
                return new StatsRetardDashboard
                {
                    NbCommandesEnRetard = idsRetard.Count,
                    NbReportsMois = reportsInMemory,
                    NbClientsPrevenusMois = clientsInMemory
                };
            }

            // Un seul aller-retour SQL pour les trois compteurs. Les deux branches
            // de retard gardent une clé de suivi constante, indexable (sans CASE).
            await ctx.Database.OpenConnectionAsync();
            await using var cmd = ctx.Database.GetDbConnection().CreateCommand();
            cmd.CommandText = @"
WITH retards AS (
  SELECT p.IdCommande FROM PiecesCommande p
  WHERE p.CommandeActive=1 AND p.Statut IN ('A faire','En cours')
    AND p.DateRdvEffective <= @maintenant AND p.RendezVousException IS NULL
    AND NOT EXISTS (SELECT 1 FROM SuivisAlerteRetard s
      WHERE s.TypeCible='Commande' AND s.IdCible=p.IdCommande
        AND s.NbPreventions>0 AND s.DateRdvTraite=p.DateRdvEffective)
  UNION ALL
  SELECT p.IdCommande FROM PiecesCommande p
  WHERE p.CommandeActive=1 AND p.Statut IN ('A faire','En cours')
    AND p.DateRdvEffective <= @maintenant AND p.RendezVousException IS NOT NULL
    AND NOT EXISTS (SELECT 1 FROM SuivisAlerteRetard s
      WHERE s.TypeCible='Piece' AND s.IdCible=p.IdPieceCommande
        AND s.NbPreventions>0 AND s.DateRdvTraite=p.DateRdvEffective)
)
SELECT
  (SELECT COUNT(*) FROM (SELECT DISTINCT IdCommande FROM retards LIMIT 1000)),
  (SELECT COUNT(*) FROM JournalAudit WHERE TypeAction=@actionReport AND DateHeureUtc>=@debutUtc),
  (SELECT COUNT(DISTINCT IdClient) FROM SuivisAlerteRetard
    WHERE NbPreventions>0 AND DateDernierMessage>=@debutMois AND IdClient>0);";
            AjouterParam(cmd, "@maintenant", now.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            AjouterParam(cmd, "@actionReport", ActionReport);
            AjouterParam(cmd, "@debutUtc", debutMoisUtc.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            AjouterParam(cmd, "@debutMois", debutMois.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
            await using var reader = await cmd.ExecuteReaderAsync();
            await reader.ReadAsync();
            int nbCommandesEnRetard = reader.GetInt32(0);
            int reports = reader.GetInt32(1);
            int clients = reader.GetInt32(2);

            return new StatsRetardDashboard
            {
                NbCommandesEnRetard   = nbCommandesEnRetard,
                NbReportsMois         = reports,
                NbClientsPrevenusMois = clients
            };
        }

        private static void AjouterParam(System.Data.Common.DbCommand command, string nom, object valeur)
        {
            var param = command.CreateParameter();
            param.ParameterName = nom;
            param.Value = valeur;
            command.Parameters.Add(param);
        }

        // ════════════════════════════════════════════════════════════════════
        // ÉTAPE 2 PERF — Version paginée de ObtenirAlertesRisqueRetardAsync
        // ════════════════════════════════════════════════════════════════════
        public async Task<PagedResult<AlerteRendezVous>> ObtenirAlertesRisqueRetardPageAsync(
            int page = 1, int taille = 50, DateTime? maintenant = null,
            CancellationToken cancellationToken = default)
            => await ObtenirAlertesRisqueRetardPageCoreAsync(page, taille, maintenant, cancellationToken, true);

        private async Task<PagedResult<AlerteRendezVous>> ObtenirAlertesRisqueRetardPageCoreAsync(
            int page, int taille, DateTime? maintenant, CancellationToken cancellationToken, bool compterTotal)
        {
            page = Math.Max(1, page);
            taille = Math.Clamp(taille, 1, 50);
            var now = maintenant ?? DateTime.Now;
            int minutes = await _parametres.ObtenirRetardAlerteAvantMinutes();
            if (minutes != 60 && minutes != 120) minutes = ParametresService.RetardAlerteAvantMinutesDefaut;
            var seuil = now.AddMinutes(minutes);

            await using var ctx = await _factory.CreateDbContextAsync(cancellationToken);

            // ── COUNT SQL avec NOT EXISTS ─────────────────────────────────
            var candidats = ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && p.Statut != "Terminee" && p.Statut != "Livree"
                         && p.DateRdvEffective <= seuil);
            var baseQuery = ExclurePiecesDejaPrevenues(candidats, ctx.SuivisAlerteRetard);

            int total = compterTotal ? await baseQuery.AsNoTracking().CountAsync(cancellationToken) : 0;

            // Sélectionner d'abord uniquement les identifiants en SQL brut : EF ajoutait
            // une projection d'entités complète au UNION et SQLite relisait toute la
            // section avant de rendre une page bornée.
            var idsPage = new List<int>(taille);
            List<RisquePieceDto> piecesData;
            if (ctx.Database.IsRelational())
            {
                await ctx.Database.OpenConnectionAsync(cancellationToken);
                await using (var command = ctx.Database.GetDbConnection().CreateCommand())
                {
                command.CommandText = @"
SELECT x.IdPieceCommande FROM (
  SELECT p.IdPieceCommande, p.DateRdvEffective FROM (
    SELECT p.IdPieceCommande, p.DateRdvEffective FROM PiecesCommande p
    WHERE p.CommandeActive=1 AND p.Statut NOT IN ('Terminee','Livree')
      AND p.DateRdvEffective <= @seuil AND p.RendezVousException IS NULL
      AND NOT EXISTS (SELECT 1 FROM SuivisAlerteRetard s
        WHERE s.TypeCible='Commande' AND s.IdCible=p.IdCommande
          AND s.NbPreventions>0 AND s.DateRdvTraite=p.DateRdvEffective)
    ORDER BY p.DateRdvEffective, p.IdPieceCommande LIMIT @limiteBranche
  ) p
  UNION ALL
  SELECT p.IdPieceCommande, p.DateRdvEffective FROM (
    SELECT p.IdPieceCommande, p.DateRdvEffective FROM PiecesCommande p
    WHERE p.CommandeActive=1 AND p.Statut NOT IN ('Terminee','Livree')
      AND p.DateRdvEffective <= @seuil AND p.RendezVousException IS NOT NULL
      AND NOT EXISTS (SELECT 1 FROM SuivisAlerteRetard s
        WHERE s.TypeCible='Piece' AND s.IdCible=p.IdPieceCommande
          AND s.NbPreventions>0 AND s.DateRdvTraite=p.DateRdvEffective)
    ORDER BY p.DateRdvEffective, p.IdPieceCommande LIMIT @limiteBranche
  ) p
) x ORDER BY x.DateRdvEffective, x.IdPieceCommande LIMIT @taille OFFSET @offset;";
                var seuilParameter = command.CreateParameter();
                seuilParameter.ParameterName = "@seuil";
                seuilParameter.Value = seuil.ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture);
                command.Parameters.Add(seuilParameter);
                var brancheParameter = command.CreateParameter();
                brancheParameter.ParameterName = "@limiteBranche";
                brancheParameter.Value = page * taille;
                command.Parameters.Add(brancheParameter);
                var tailleParameter = command.CreateParameter();
                tailleParameter.ParameterName = "@taille";
                tailleParameter.Value = taille;
                command.Parameters.Add(tailleParameter);
                var offsetParameter = command.CreateParameter();
                offsetParameter.ParameterName = "@offset";
                offsetParameter.Value = (page - 1) * taille;
                command.Parameters.Add(offsetParameter);
                    await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                    while (await reader.ReadAsync(cancellationToken))
                        idsPage.Add(reader.GetInt32(0));
                }

                piecesData = new List<RisquePieceDto>(idsPage.Count);
                if (idsPage.Count > 0)
                {
                    await using var detailsCommand = ctx.Database.GetDbConnection().CreateCommand();
                    var placeholders = new string[idsPage.Count];
                    for (int i = 0; i < idsPage.Count; i++)
                    {
                        placeholders[i] = $"@id{i}";
                        var idParam = detailsCommand.CreateParameter();
                        idParam.ParameterName = placeholders[i];
                        idParam.Value = idsPage[i];
                        detailsCommand.Parameters.Add(idParam);
                    }
                    detailsCommand.CommandText = $@"
SELECT p.IdPieceCommande, p.IdCommande, p.TypeVetement, p.Statut, p.RendezVousException,
       p.DateRdvEffective, c.IdClient, cl.Prenom, cl.Nom, cl.Telephone, e.Prenom, e.Nom
FROM PiecesCommande p
LEFT JOIN Commandes c ON c.IdCommande=p.IdCommande
LEFT JOIN Clients cl ON cl.IdClient=c.IdClient
LEFT JOIN Employes e ON e.IdEmploye=p.IdCouturier
WHERE p.IdPieceCommande IN ({string.Join(",", placeholders)})
ORDER BY p.DateRdvEffective, p.IdPieceCommande;";
                    await using var detailsReader = await detailsCommand.ExecuteReaderAsync(cancellationToken);
                    while (await detailsReader.ReadAsync(cancellationToken))
                    {
                        piecesData.Add(new RisquePieceDto
                        {
                            IdPieceCommande = detailsReader.GetInt32(0), IdCommande = detailsReader.GetInt32(1),
                            TypeVetement = detailsReader.GetString(2), Statut = detailsReader.GetString(3),
                            RendezVousException = detailsReader.IsDBNull(4) ? null : DateTime.Parse(detailsReader.GetString(4), System.Globalization.CultureInfo.InvariantCulture),
                            DateRdvEffective = DateTime.Parse(detailsReader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                            CmdIdClient = detailsReader.IsDBNull(6) ? 0 : detailsReader.GetInt32(6),
                            ClientPrenom = detailsReader.IsDBNull(7) ? null : detailsReader.GetString(7),
                            ClientNom = detailsReader.IsDBNull(8) ? null : detailsReader.GetString(8),
                            ClientTel = detailsReader.IsDBNull(9) ? null : detailsReader.GetString(9),
                            CoutPrenom = detailsReader.IsDBNull(10) ? null : detailsReader.GetString(10),
                            CoutNom = detailsReader.IsDBNull(11) ? null : detailsReader.GetString(11)
                        });
                    }
                }
            }
            else
            {
                piecesData = await baseQuery
                    .AsNoTracking()
                    .OrderBy(p => p.DateRdvEffective)
                    .Skip((page - 1) * taille)
                    .Take(taille)
                    .Select(p => new RisquePieceDto
                {
                    IdPieceCommande = p.IdPieceCommande, IdCommande = p.IdCommande,
                    TypeVetement = p.TypeVetement, Statut = p.Statut, RendezVousException = p.RendezVousException,
                    DateRdvEffective = p.DateRdvEffective,
                    CmdIdClient = p.Commande != null ? p.Commande.IdClient : 0,
                    ClientPrenom = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Prenom : null,
                    ClientNom = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Nom : null,
                    ClientTel = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Telephone : null,
                    CoutPrenom = p.Couturier != null ? p.Couturier.Prenom : null,
                    CoutNom = p.Couturier != null ? p.Couturier.Nom : null
                })
                    .ToListAsync(cancellationToken);
            }

            var idsCommandes = piecesData.Where(p => !p.RendezVousException.HasValue)
                .Select(p => p.IdCommande).Distinct().ToArray();
            var idsPiecesException = piecesData.Where(p => p.RendezVousException.HasValue)
                .Select(p => p.IdPieceCommande).Distinct().ToArray();
            var suivisPage = new List<SuiviAlerteRetard>(idsCommandes.Length + idsPiecesException.Length);
            if (idsCommandes.Length > 0)
                suivisPage.AddRange(await ctx.SuivisAlerteRetard.AsNoTracking()
                    .Where(s => s.TypeCible == CibleCommande && idsCommandes.Contains(s.IdCible))
                    .ToListAsync(cancellationToken));
            if (idsPiecesException.Length > 0)
                suivisPage.AddRange(await ctx.SuivisAlerteRetard.AsNoTracking()
                    .Where(s => s.TypeCible == CiblePiece && idsPiecesException.Contains(s.IdCible))
                    .ToListAsync(cancellationToken));
            var suivisParCle = suivisPage.ToDictionary(s => (s.TypeCible, s.IdCible));

            var alertes = new List<AlerteRendezVous>();
            foreach (var p in piecesData)
            {
                var cible = p.RendezVousException.HasValue
                    ? (CiblePiece, p.IdPieceCommande) : (CibleCommande, p.IdCommande);
                suivisParCle.TryGetValue(cible, out var suivi);
                var echeance = p.DateRdvEffective;
                string nomCouturier = !string.IsNullOrEmpty(p.CoutPrenom) || !string.IsNullOrEmpty(p.CoutNom)
                    ? $"{p.CoutPrenom} {p.CoutNom}".Trim() : "(non assigné)";
                string nomClient = !string.IsNullOrEmpty(p.ClientPrenom) || !string.IsNullOrEmpty(p.ClientNom)
                    ? $"{p.ClientPrenom} {p.ClientNom}".Trim() : "(client inconnu)";
                string statutAffiche = p.Statut switch
                {
                    "A faire" => "À faire", "En cours" => "En cours",
                    "Terminee" => "Terminée", "Livree" => "Livrée", _ => p.Statut
                };

                var temps = echeance - now;
                alertes.Add(new AlerteRendezVous
                {
                    IdCommande = p.IdCommande, IdPieceCommande = p.IdPieceCommande,
                    NomClient = nomClient, Telephone = p.ClientTel ?? "",
                    TypeVetement = p.TypeVetement, DateRendezVous = echeance,
                    HeureRendezVous = echeance.ToString("HH:mm"), Statut = statutAffiche,
                    TempsRestant = FormaterTemps(temps), NomCouturier = nomCouturier,
                    EstUrgent = true, TypeAlerte = echeance <= now ? "Retard" : "RisqueRetard",
                    TypeCibleRetard = p.RendezVousException.HasValue ? CiblePiece : CibleCommande,
                    IdCibleRetard = p.RendezVousException.HasValue ? p.IdPieceCommande : p.IdCommande,
                    IdClientRetard = p.CmdIdClient,
                    NbFoisPrevenu = suivi?.NbPreventions ?? 0, AlerteTraiteePourCeRdv = false
                });
            }

            var retoursQuery = ctx.Retours.AsNoTracking()
                .Where(r => r.EstAnnule == false && r.Statut != "Rendu" && r.Statut != "Termine" && r.Statut != "Pret"
                         && r.DateRdvReprise.HasValue && r.DateRdvReprise < seuil.Date.AddDays(1)
                         && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee
                         && !ctx.SuivisAlerteRetard.Any(s => s.TypeCible == CibleRetour && s.IdCible == r.IdRetour
                             && s.NbPreventions > 0
                             && s.DateRdvTraite >= r.DateRdvReprise!.Value.AddMinutes(-1)
                             && s.DateRdvTraite < r.DateRdvReprise.Value.AddMinutes(1)));
            int totalRetours = compterTotal ? await retoursQuery.CountAsync(cancellationToken) : 0;
            List<RisqueRetourDto> retoursData;
            if (ctx.Database.IsRelational())
            {
                await using var retourCommand = ctx.Database.GetDbConnection().CreateCommand();
                retourCommand.CommandText = @"
SELECT r.IdRetour, r.IdCommande, r.IdPieceCommande, r.NumeroRetour, r.Statut,
       r.DateRdvReprise, r.HeureDebutReprise, r.HeureFinReprise, c.IdClient,
       CASE WHEN cl.IdClient IS NULL THEN '(client inconnu)' ELSE cl.Prenom || ' ' || cl.Nom END,
       COALESCE(cl.Telephone, ''), COALESCE(pc.TypeVetement, 'Retour'),
       CASE WHEN er.IdEmploye IS NOT NULL THEN er.Prenom || ' ' || er.Nom
            WHEN ei.IdEmploye IS NOT NULL THEN ei.Prenom || ' ' || ei.Nom ELSE '(non assigné)' END
FROM Retours r
JOIN Commandes c ON c.IdCommande=r.IdCommande
LEFT JOIN Clients cl ON cl.IdClient=c.IdClient
LEFT JOIN PiecesCommande pc ON pc.IdPieceCommande=r.IdPieceCommande
LEFT JOIN Employes er ON er.IdEmploye=r.IdCouturierReprise
LEFT JOIN Employes ei ON ei.IdEmploye=r.IdCouturier
WHERE r.EstAnnule=0 AND r.Statut NOT IN ('Rendu','Termine','Pret')
  AND r.DateRdvReprise IS NOT NULL AND r.DateRdvReprise < @finJour
  AND c.EstSupprimee=0 AND c.EstAnnulee=0
  AND NOT EXISTS (SELECT 1 FROM SuivisAlerteRetard s
    WHERE s.TypeCible='Retour' AND s.IdCible=r.IdRetour AND s.NbPreventions>0
      AND s.DateRdvTraite >= rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f',r.DateRdvReprise,'-1 minutes'),'0'),'.')
      AND s.DateRdvTraite < rtrim(rtrim(strftime('%Y-%m-%d %H:%M:%f',r.DateRdvReprise,'+1 minutes'),'0'),'.'))
ORDER BY r.DateRdvReprise LIMIT @limite;";
                AjouterParam(retourCommand, "@finJour", seuil.Date.AddDays(1).ToString("yyyy-MM-dd HH:mm:ss", System.Globalization.CultureInfo.InvariantCulture));
                AjouterParam(retourCommand, "@limite", page * taille);
                retoursData = new List<RisqueRetourDto>(page * taille);
                await using var retourReader = await retourCommand.ExecuteReaderAsync(cancellationToken);
                while (await retourReader.ReadAsync(cancellationToken))
                {
                    retoursData.Add(new RisqueRetourDto
                    {
                        IdRetour = retourReader.GetInt32(0), IdCommande = retourReader.GetInt32(1),
                        IdPieceCommande = retourReader.GetInt32(2), NumeroRetour = retourReader.IsDBNull(3) ? null : retourReader.GetString(3),
                        Statut = retourReader.GetString(4),
                        DateRdvReprise = retourReader.IsDBNull(5) ? null : DateTime.Parse(retourReader.GetString(5), System.Globalization.CultureInfo.InvariantCulture),
                        HeureDebutReprise = retourReader.IsDBNull(6) ? null : TimeSpan.Parse(retourReader.GetString(6), System.Globalization.CultureInfo.InvariantCulture),
                        HeureFinReprise = retourReader.IsDBNull(7) ? null : TimeSpan.Parse(retourReader.GetString(7), System.Globalization.CultureInfo.InvariantCulture),
                        IdClient = retourReader.GetInt32(8), Nom = retourReader.GetString(9),
                        Telephone = retourReader.GetString(10), Vetement = retourReader.GetString(11),
                        Couturier = retourReader.GetString(12)
                    });
                }
            }
            else
            {
                retoursData = await retoursQuery.OrderBy(r => r.DateRdvReprise).Take(page * taille)
                    .Select(r => new RisqueRetourDto
                    {
                        IdRetour = r.IdRetour, IdCommande = r.IdCommande, IdPieceCommande = r.IdPieceCommande,
                        NumeroRetour = r.NumeroRetour, Statut = r.Statut, DateRdvReprise = r.DateRdvReprise,
                        HeureDebutReprise = r.HeureDebutReprise, HeureFinReprise = r.HeureFinReprise,
                        IdClient = r.Commande!.IdClient,
                        Nom = r.Commande!.Client != null ? r.Commande.Client.Prenom + " " + r.Commande.Client.Nom : "(client inconnu)",
                        Telephone = r.Commande!.Client != null ? r.Commande.Client.Telephone : "",
                        Vetement = r.PieceCommande != null ? r.PieceCommande.TypeVetement : "Retour",
                        Couturier = r.CouturierReprise != null ? r.CouturierReprise.Prenom + " " + r.CouturierReprise.Nom
                            : r.Couturier != null ? r.Couturier.Prenom + " " + r.Couturier.Nom : "(non assigné)"
                    }).ToListAsync(cancellationToken);
            }
            foreach (var r in retoursData)
            {
                var dateBase = r.DateRdvReprise!.Value;
                var heure = r.HeureFinReprise ?? r.HeureDebutReprise;
                var echeance = heure.HasValue ? dateBase.Date + heure.Value : dateBase;
                if (echeance > seuil) continue;
                alertes.Add(new AlerteRendezVous
                {
                    IdCommande = r.IdCommande, IdPieceCommande = r.IdPieceCommande,
                    IdRetour = r.IdRetour, NumeroRetour = r.NumeroRetour, EstAlertRetour = true,
                    NomClient = r.Nom, Telephone = r.Telephone ?? "", TypeVetement = r.Vetement ?? "Retour",
                    DateRendezVous = echeance, HeureRendezVous = echeance.ToString("HH:mm"),
                    Statut = r.Statut, TempsRestant = FormaterTemps(echeance - now), NomCouturier = r.Couturier,
                    EstUrgent = true, TypeAlerte = echeance <= now ? "Retard" : "RisqueRetard",
                    TypeCibleRetard = CibleRetour, IdCibleRetard = r.IdRetour,
                    IdClientRetard = r.IdClient, AlerteTraiteePourCeRdv = false
                });
            }

            alertes = alertes.OrderBy(a => a.DateRendezVous)
                .Skip((page - 1) * taille).Take(taille).ToList();

            return new PagedResult<AlerteRendezVous>
            {
                Items = alertes, TotalCount = total + totalRetours, Page = page, PageSize = taille
            };
        }

        private static string FormaterTemps(TimeSpan reste)
        {
            if (reste.TotalMinutes < 0)
            {
                var r = reste.Negate();
                if (r.TotalDays >= 1) return $"-{(int)r.TotalDays}j";
                if (r.TotalHours >= 1) return $"-{(int)r.TotalHours}h";
                return $"-{r.Minutes}min";
            }
            if (reste.TotalDays >= 1)
            {
                var jours = (int)reste.TotalDays;
                return reste.Hours > 0 ? $"{jours}j {reste.Hours}h" : $"{jours}j";
            }
            return reste.TotalMinutes >= 60
                ? $"{(int)reste.TotalHours}h {reste.Minutes:00}min"
                : $"{reste.Minutes}min";
        }
    }
}
