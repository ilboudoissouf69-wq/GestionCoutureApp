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

            // ── ÉTAPE 2 PERF : COUNT SQL avec NOT EXISTS (plus de chargement en mémoire) ──
            // NbCommandesEnRetard = nombre distinct de commandes qui ont au moins une pièce
            // en retard (DateRdvEffective <= maintenant, CommandeActive, non terminée/livrée)
            // ET qui n'ont pas de suivi valide (NbPreventions > 0 pour le même RDV).
            //
            // Note : MemeRdv compare à la minute près. En SQL on compare à la seconde :
            // si DateRdvTraite est dans le même créneau d'1 minute que DateRdvEffective,
            // la pièce est exclue. C'est suffisamment proche pour la sémantique métier.
            var nbCommandesEnRetard = await ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && (p.Statut == "A faire" || p.Statut == "En cours")
                         && p.DateRdvEffective <= now
                         && !ctx.SuivisAlerteRetard.Any(s =>
                             s.IdCible == p.IdCommande
                             && s.TypeCible == CibleCommande
                             && s.NbPreventions > 0
                             && s.DateRdvTraite >= p.DateRdvEffective.AddMinutes(-1)
                             && s.DateRdvTraite <  p.DateRdvEffective.AddMinutes(1)))
                .Select(p => p.IdCommande)
                .Distinct()
                .CountAsync();

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
                NbCommandesEnRetard   = nbCommandesEnRetard,
                NbReportsMois         = reports,
                NbClientsPrevenusMois = clients
            };
        }

        private static SuiviAlerteRetard? TrouverSuivi(
            List<SuiviAlerteRetard> suivis, string type, int id) =>
            suivis.FirstOrDefault(s => s.TypeCible == type && s.IdCible == id);

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
            var baseQuery = ctx.PiecesCommande
                .Where(p => p.CommandeActive
                         && p.Statut != "Terminee" && p.Statut != "Livree"
                         && p.DateRdvEffective <= seuil
                         && !ctx.SuivisAlerteRetard.Any(s =>
                             s.TypeCible == (p.RendezVousException.HasValue ? CiblePiece : CibleCommande)
                             && s.IdCible == (p.RendezVousException.HasValue ? p.IdPieceCommande : p.IdCommande)
                             && s.NbPreventions > 0
                             && s.DateRdvTraite >= p.DateRdvEffective.AddMinutes(-1)
                             && s.DateRdvTraite <  p.DateRdvEffective.AddMinutes(1)));

            int total = compterTotal ? await baseQuery.AsNoTracking().CountAsync(cancellationToken) : 0;

            var piecesData = await baseQuery
                .AsNoTracking()
                .OrderBy(p => p.DateRdvEffective)
                .Take(page * taille)
                .Select(p => new
                {
                    p.IdPieceCommande, p.IdCommande,
                    p.TypeVetement, p.Statut, p.RendezVousException,
                    p.DateRdvEffective,
                    CmdIdClient  = p.Commande != null ? p.Commande.IdClient : 0,
                    ClientPrenom = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Prenom : null,
                    ClientNom    = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Nom : null,
                    ClientTel    = p.Commande != null && p.Commande.Client != null ? p.Commande.Client.Telephone : null,
                    CoutPrenom   = p.Couturier != null ? p.Couturier.Prenom : null,
                    CoutNom      = p.Couturier != null ? p.Couturier.Nom : null
                })
                .ToListAsync(cancellationToken);

            var idsCibles = piecesData.Select(p => p.RendezVousException.HasValue
                    ? p.IdPieceCommande : p.IdCommande)
                .Distinct().ToArray();
            var suivisPage = idsCibles.Length == 0
                ? new List<SuiviAlerteRetard>()
                : await ctx.SuivisAlerteRetard.AsNoTracking()
                    .Where(s => idsCibles.Contains(s.IdCible)
                             && (s.TypeCible == CiblePiece || s.TypeCible == CibleCommande))
                    .ToListAsync(cancellationToken);
            var suivisParCle = suivisPage.ToDictionary(s => (s.TypeCible, s.IdCible));

            var alertes = new List<AlerteRendezVous>();
            foreach (var p in piecesData)
            {
                var echeance = p.DateRdvEffective;
                string type = p.RendezVousException.HasValue ? CiblePiece : CibleCommande;
                int id = p.RendezVousException.HasValue ? p.IdPieceCommande : p.IdCommande;

                suivisParCle.TryGetValue((type, id), out var suivi);

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
                    TypeCibleRetard = type, IdCibleRetard = id, IdClientRetard = p.CmdIdClient,
                    NbFoisPrevenu = suivi?.NbPreventions ?? 0, AlerteTraiteePourCeRdv = false
                });
            }

            var retoursQuery = ctx.Retours.AsNoTracking()
                .Where(r => !r.EstAnnule && r.Statut != "Rendu" && r.Statut != "Termine" && r.Statut != "Pret"
                         && r.DateRdvReprise.HasValue && r.DateRdvReprise < seuil.Date.AddDays(1)
                         && r.Commande != null && !r.Commande.EstSupprimee && !r.Commande.EstAnnulee
                         && !ctx.SuivisAlerteRetard.Any(s => s.TypeCible == CibleRetour && s.IdCible == r.IdRetour
                             && s.NbPreventions > 0
                             && s.DateRdvTraite >= r.DateRdvReprise!.Value.AddMinutes(-1)
                             && s.DateRdvTraite < r.DateRdvReprise.Value.AddMinutes(1)));
            int totalRetours = compterTotal ? await retoursQuery.CountAsync(cancellationToken) : 0;
            var retoursData = await retoursQuery.OrderBy(r => r.DateRdvReprise).Take(page * taille)
                .Select(r => new
                {
                    r.IdRetour, r.IdCommande, r.IdPieceCommande, r.NumeroRetour, r.Statut,
                    r.DateRdvReprise, r.HeureDebutReprise, r.HeureFinReprise,
                    IdClient = r.Commande!.IdClient,
                    Nom = r.Commande!.Client != null ? r.Commande.Client.Prenom + " " + r.Commande.Client.Nom : "(client inconnu)",
                    Telephone = r.Commande!.Client != null ? r.Commande.Client.Telephone : "",
                    Vetement = r.PieceCommande != null ? r.PieceCommande.TypeVetement : "Retour",
                    Couturier = r.CouturierReprise != null ? r.CouturierReprise.Prenom + " " + r.CouturierReprise.Nom
                        : r.Couturier != null ? r.Couturier.Prenom + " " + r.Couturier.Nom : "(non assigné)"
                }).ToListAsync(cancellationToken);
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
