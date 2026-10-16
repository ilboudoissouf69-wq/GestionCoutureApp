using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class RetardAlerteService : IRetardAlerteService
    {
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
            if (piece.RendezVousException.HasValue)
                return piece.RendezVousException.Value;

            var heure = commande.HeureFin ?? commande.HeureDebut;
            return commande.DateFin.Date + heure;
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
            var now = maintenant ?? DateTime.Now;
            int minutes = await _parametres.ObtenirRetardAlerteAvantMinutes();
            if (minutes != 60 && minutes != 120) minutes = ParametresService.RetardAlerteAvantMinutesDefaut;
            var seuil = now.AddMinutes(minutes);

            await using var ctx = await _factory.CreateDbContextAsync();

            var suivis = await ctx.SuivisAlerteRetard.AsNoTracking().ToListAsync();
            var alertes = new List<AlerteRendezVous>();

            var pieces = await ctx.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Commande).ThenInclude(c => c!.Client)
                .Where(p => p.Statut != "Livree"
                         && p.Statut != "Terminee"
                         && p.Commande != null
                         && !p.Commande.EstSupprimee)
                .AsNoTracking()
                .ToListAsync();

            foreach (var piece in pieces)
            {
                var commande = piece.Commande!;
                var echeance = CalculerEcheancePiece(piece, commande);
                if (echeance > seuil) continue;

                var (type, id) = CleCiblePiece(piece);
                var suivi = TrouverSuivi(suivis, type, id);
                if (suivi != null && MemeRdv(suivi.DateRdvTraite, echeance) && suivi.NbPreventions > 0)
                    continue;

                var temps = echeance - now;
                bool depasse = echeance <= now;
                alertes.Add(new AlerteRendezVous
                {
                    IdCommande      = commande.IdCommande,
                    IdPieceCommande = piece.IdPieceCommande,
                    NomClient       = commande.Client != null
                        ? $"{commande.Client.Prenom} {commande.Client.Nom}"
                        : "(client inconnu)",
                    Telephone       = commande.Client?.Telephone ?? "",
                    TypeVetement    = piece.TypeVetement,
                    DateRendezVous  = echeance,
                    HeureRendezVous = echeance.ToString("HH:mm"),
                    Statut          = piece.StatutAffiche,
                    TempsRestant    = FormaterTemps(temps),
                    NomCouturier    = piece.Couturier?.NomComplet ?? "(non assigné)",
                    EstUrgent       = true,
                    TypeAlerte      = depasse ? "Retard" : "RisqueRetard",
                    TypeCibleRetard = type,
                    IdCibleRetard   = id,
                    IdClientRetard  = commande.IdClient,
                    NbFoisPrevenu   = suivi?.NbPreventions ?? 0,
                    AlerteTraiteePourCeRdv = false
                });
            }

            var retours = await ctx.Retours
                .Include(r => r.Commande).ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .Include(r => r.CouturierReprise)
                .Where(r => !r.EstAnnule
                         && r.Statut != "Rendu"
                         && r.Statut != "Termine"
                         && r.Statut != "Pret"
                         && r.DateRdvReprise.HasValue)
                .AsNoTracking()
                .ToListAsync();

            foreach (var ret in retours)
            {
                var echeance = CalculerEcheanceRetour(ret);
                if (echeance > seuil) continue;

                var suivi = TrouverSuivi(suivis, CibleRetour, ret.IdRetour);
                if (suivi != null && MemeRdv(suivi.DateRdvTraite, echeance) && suivi.NbPreventions > 0)
                    continue;

                string nomCouturier = ret.CouturierReprise != null
                    ? ret.CouturierReprise.Prenom + " " + ret.CouturierReprise.Nom
                    : ret.Couturier != null
                        ? ret.Couturier.Prenom + " " + ret.Couturier.Nom
                        : "(non assigné)";
                string nomClient = ret.Commande?.Client != null
                    ? ret.Commande.Client.Prenom + " " + ret.Commande.Client.Nom
                    : "(client inconnu)";

                var temps = echeance - now;
                bool depasse = echeance <= now;
                alertes.Add(new AlerteRendezVous
                {
                    IdCommande      = ret.IdCommande,
                    IdPieceCommande = ret.IdPieceCommande,
                    IdRetour        = ret.IdRetour,
                    NumeroRetour    = ret.NumeroRetour,
                    EstAlertRetour  = true,
                    NomClient       = nomClient,
                    Telephone       = ret.Commande?.Client?.Telephone ?? "",
                    TypeVetement    = ret.PieceCommande?.TypeVetement ?? "—",
                    DateRendezVous  = echeance,
                    HeureRendezVous = echeance.ToString("HH:mm"),
                    Statut          = ret.Statut,
                    TempsRestant    = FormaterTemps(temps),
                    NomCouturier    = nomCouturier,
                    EstUrgent       = true,
                    TypeAlerte      = depasse ? "Retard" : "RisqueRetard",
                    TypeCibleRetard = CibleRetour,
                    IdCibleRetard   = ret.IdRetour,
                    IdClientRetard  = ret.Commande?.IdClient ?? 0,
                    NbFoisPrevenu   = suivi?.NbPreventions ?? 0,
                    AlerteTraiteePourCeRdv = false
                });
            }

            return alertes.OrderBy(a => a.DateRendezVous).ToList();
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
                ancienne = cmd.DateFin.Date + (cmd.HeureFin ?? cmd.HeureDebut);
                nouvelle = ancienne.AddDays(jours);
                cmd.DateFin = cmd.DateFin.Date.AddDays(jours);
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
            var debutMois = new DateTime(now.Year, now.Month, 1);
            var debutMoisUtc = debutMois.ToUniversalTime();

            var alertes = await ObtenirAlertesRisqueRetardAsync(now);
            int nbRetards = alertes
                .Where(a => !a.EstAlertRetour && a.DateRendezVous <= now)
                .Select(a => a.IdCommande)
                .Distinct()
                .Count();

            await using var ctx = await _factory.CreateDbContextAsync();
            int reports = await ctx.JournalAudit.CountAsync(j =>
                j.TypeAction == ActionReport && j.DateHeureUtc >= debutMoisUtc);

            int clients = await ctx.SuivisAlerteRetard
                .Where(s => s.NbPreventions > 0
                         && s.DateDernierMessage != null
                         && s.DateDernierMessage >= debutMois
                         && s.IdClient > 0)
                .Select(s => s.IdClient)
                .Distinct()
                .CountAsync();

            return new StatsRetardDashboard
            {
                NbCommandesEnRetard    = nbRetards,
                NbReportsMois          = reports,
                NbClientsPrevenusMois  = clients
            };
        }

        private static SuiviAlerteRetard? TrouverSuivi(
            List<SuiviAlerteRetard> suivis, string type, int id) =>
            suivis.FirstOrDefault(s => s.TypeCible == type && s.IdCible == id);

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
