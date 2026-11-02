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
            var seuilJourSuivant = seuil.Date.AddDays(1);

            await using var ctx = await _factory.CreateDbContextAsync();

            // ── Suivis : Dictionary indexé par (TypeCible, IdCible) pour O(1) ──
            var suivisList = await ctx.SuivisAlerteRetard.AsNoTracking().ToListAsync();
            var suivis = suivisList.ToDictionary(s => (s.TypeCible, s.IdCible), s => s);
            var alertes = new List<AlerteRendezVous>();

            // ── Pièces : pré-filtre SQL de l'échéance + projection (pas d'Include) ──
            // Avec RendezVousException : échéance = RendezVousException → filtre <= seuil
            // Sans  RendezVousException : échéance = DateFin.Date + HeureFin → filtre DateFin < seuil.Date.AddDays(1)
            var piecesData = await ctx.PiecesCommande
                .Where(p => p.Statut != "Livree"
                         && p.Statut != "Terminee"
                         && p.Commande != null
                         && !p.Commande.EstSupprimee
                         && (p.RendezVousException.HasValue
                                ? p.RendezVousException <= seuil
                                : p.Commande!.DateFin < seuilJourSuivant))
                .Select(p => new
                {
                    p.IdPieceCommande,
                    p.IdCommande,
                    p.TypeVetement,
                    p.Statut,
                    p.RendezVousException,
                    CmdDateFin   = p.Commande!.DateFin,
                    CmdHeureDebut = p.Commande.HeureDebut,
                    CmdHeureFin  = p.Commande.HeureFin,
                    CmdIdClient  = p.Commande.IdClient,
                    ClientPrenom = p.Commande.Client != null ? p.Commande.Client.Prenom : null,
                    ClientNom    = p.Commande.Client != null ? p.Commande.Client.Nom : null,
                    ClientTel    = p.Commande.Client != null ? p.Commande.Client.Telephone : null,
                    CoutPrenom    = p.Couturier != null ? p.Couturier.Prenom : null,
                    CoutNom       = p.Couturier != null ? p.Couturier.Nom : null
                })
                .AsNoTracking()
                .ToListAsync();

            foreach (var p in piecesData)
            {
                // Échéance exacte (affinage mémoire)
                DateTime echeance;
                if (p.RendezVousException.HasValue)
                    echeance = p.RendezVousException.Value;
                else
                {
                    var heure = p.CmdHeureFin ?? p.CmdHeureDebut;
                    echeance = p.CmdDateFin.Date + heure;
                }
                if (echeance > seuil) continue;

                // Clé cible
                string type = p.RendezVousException.HasValue ? CiblePiece : CibleCommande;
                int id = p.RendezVousException.HasValue ? p.IdPieceCommande : p.IdCommande;

                // Suivi O(1) via Dictionary
                SuiviAlerteRetard? suivi = null;
                suivis.TryGetValue((type, id), out suivi);
                if (suivi != null && MemeRdv(suivi.DateRdvTraite, echeance) && suivi.NbPreventions > 0)
                    continue;

                string statutAffiche = p.Statut switch
                {
                    "A faire"   => "À faire",
                    "En cours"  => "En cours",
                    "Terminee"  => "Terminée",
                    "Livree"    => "Livrée",
                    _           => p.Statut
                };
                string nomCouturier = (!string.IsNullOrEmpty(p.CoutPrenom) || !string.IsNullOrEmpty(p.CoutNom))
                    ? $"{p.CoutPrenom} {p.CoutNom}".Trim()
                    : "(non assigné)";
                string nomClient = (!string.IsNullOrEmpty(p.ClientPrenom) || !string.IsNullOrEmpty(p.ClientNom))
                    ? $"{p.ClientPrenom} {p.ClientNom}".Trim()
                    : "(client inconnu)";

                var temps = echeance - now;
                bool depasse = echeance <= now;
                alertes.Add(new AlerteRendezVous
                {
                    IdCommande      = p.IdCommande,
                    IdPieceCommande = p.IdPieceCommande,
                    NomClient       = nomClient,
                    Telephone       = p.ClientTel ?? "",
                    TypeVetement    = p.TypeVetement,
                    DateRendezVous  = echeance,
                    HeureRendezVous = echeance.ToString("HH:mm"),
                    Statut          = statutAffiche,
                    TempsRestant    = FormaterTemps(temps),
                    NomCouturier    = nomCouturier,
                    EstUrgent       = true,
                    TypeAlerte      = depasse ? "Retard" : "RisqueRetard",
                    TypeCibleRetard = type,
                    IdCibleRetard   = id,
                    IdClientRetard  = p.CmdIdClient,
                    NbFoisPrevenu   = suivi?.NbPreventions ?? 0,
                    AlerteTraiteePourCeRdv = false
                });
            }

            // ── Retours : pré-filtre SQL + projection ──
            var retoursData = await ctx.Retours
                .Where(r => !r.EstAnnule
                         && r.Statut != "Rendu"
                         && r.Statut != "Termine"
                         && r.Statut != "Pret"
                         && r.DateRdvReprise.HasValue
                         && r.DateRdvReprise < seuilJourSuivant)
                .Select(r => new
                {
                    r.IdRetour,
                    r.IdCommande,
                    r.IdPieceCommande,
                    r.NumeroRetour,
                    r.Statut,
                    r.DateRdvReprise,
                    r.HeureDebutReprise,
                    r.HeureFinReprise,
                    r.DateSignalement,
                    TypeVetement = r.PieceCommande != null ? r.PieceCommande.TypeVetement : null,
                    CmdIdClient  = r.Commande != null ? r.Commande.IdClient : 0,
                    ClientPrenom = r.Commande != null && r.Commande.Client != null
                        ? r.Commande.Client.Prenom : null,
                    ClientNom    = r.Commande != null && r.Commande.Client != null
                        ? r.Commande.Client.Nom : null,
                    ClientTel    = r.Commande != null && r.Commande.Client != null
                        ? r.Commande.Client.Telephone : null,
                    CoutReprisePrenom = r.CouturierReprise != null ? r.CouturierReprise.Prenom : null,
                    CoutRepriseNom    = r.CouturierReprise != null ? r.CouturierReprise.Nom : null,
                    CoutPrenom         = r.Couturier != null ? r.Couturier.Prenom : null,
                    CoutNom            = r.Couturier != null ? r.Couturier.Nom : null
                })
                .AsNoTracking()
                .ToListAsync();

            foreach (var r in retoursData)
            {
                // Échéance exacte
                var dateBase = r.DateRdvReprise ?? r.DateSignalement;
                var heure    = r.HeureFinReprise ?? r.HeureDebutReprise;
                DateTime echeance = heure.HasValue ? dateBase.Date + heure.Value : dateBase;
                if (echeance > seuil) continue;

                SuiviAlerteRetard? suivi = null;
                suivis.TryGetValue((CibleRetour, r.IdRetour), out suivi);
                if (suivi != null && MemeRdv(suivi.DateRdvTraite, echeance) && suivi.NbPreventions > 0)
                    continue;

                string nomCouturier = !string.IsNullOrEmpty(r.CoutReprisePrenom) || !string.IsNullOrEmpty(r.CoutRepriseNom)
                    ? $"{r.CoutReprisePrenom} {r.CoutRepriseNom}".Trim()
                    : !string.IsNullOrEmpty(r.CoutPrenom) || !string.IsNullOrEmpty(r.CoutNom)
                        ? $"{r.CoutPrenom} {r.CoutNom}".Trim()
                        : "(non assigné)";
                string nomClient = !string.IsNullOrEmpty(r.ClientPrenom) || !string.IsNullOrEmpty(r.ClientNom)
                    ? $"{r.ClientPrenom} {r.ClientNom}".Trim()
                    : "(client inconnu)";

                var temps = echeance - now;
                bool depasse = echeance <= now;
                alertes.Add(new AlerteRendezVous
                {
                    IdCommande      = r.IdCommande,
                    IdPieceCommande = r.IdPieceCommande,
                    IdRetour        = r.IdRetour,
                    NumeroRetour    = r.NumeroRetour,
                    EstAlertRetour  = true,
                    NomClient       = nomClient,
                    Telephone       = r.ClientTel ?? "",
                    TypeVetement    = r.TypeVetement ?? "—",
                    DateRendezVous  = echeance,
                    HeureRendezVous = echeance.ToString("HH:mm"),
                    Statut          = r.Statut,
                    TempsRestant    = FormaterTemps(temps),
                    NomCouturier    = nomCouturier,
                    EstUrgent       = true,
                    TypeAlerte      = depasse ? "Retard" : "RisqueRetard",
                    TypeCibleRetard = CibleRetour,
                    IdCibleRetard   = r.IdRetour,
                    IdClientRetard  = r.CmdIdClient,
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
            var seuilJourSuivant = now.Date.AddDays(1);

            await using var ctx = await _factory.CreateDbContextAsync();

            // ── NbCommandesEnRetard : COUNT SQL DISTINCT IdCommande sur le pré-filtre ──
            // Même pré-filtre que ObtenirAlertesRisqueRetardAsync (échéance <= now),
            // puis exclusion des suivis (NbPreventions > 0 et MemeRdv) en mémoire sur le jeu réduit.
            var candidats = await ctx.PiecesCommande
                .Where(p => p.Statut != "Livree"
                         && p.Statut != "Terminee"
                         && p.Commande != null
                         && !p.Commande.EstSupprimee
                         && (p.RendezVousException.HasValue
                                ? p.RendezVousException <= now
                                : p.Commande!.DateFin < seuilJourSuivant))
                .Select(p => new
                {
                    p.IdCommande,
                    p.IdPieceCommande,
                    p.RendezVousException,
                    CmdDateFin = p.Commande!.DateFin,
                    CmdHeureDebut = p.Commande.HeureDebut,
                    CmdHeureFin = p.Commande.HeureFin
                })
                .AsNoTracking()
                .ToListAsync();

            // Chargé uniquement les suivis pertinents (ceux des cibles candidates)
            var suivisList = await ctx.SuivisAlerteRetard.AsNoTracking().ToListAsync();
            var suivis = suivisList.ToDictionary(s => (s.TypeCible, s.IdCible), s => s);

            var commandesEnRetard = new HashSet<int>();
            foreach (var p in candidats)
            {
                DateTime echeance;
                if (p.RendezVousException.HasValue)
                    echeance = p.RendezVousException.Value;
                else
                {
                    var heure = p.CmdHeureFin ?? p.CmdHeureDebut;
                    echeance = p.CmdDateFin.Date + heure;
                }
                if (echeance > now) continue;

                string type = p.RendezVousException.HasValue ? CiblePiece : CibleCommande;
                int id = p.RendezVousException.HasValue ? p.IdPieceCommande : p.IdCommande;

                if (suivis.TryGetValue((type, id), out var suivi)
                    && MemeRdv(suivi.DateRdvTraite, echeance) && suivi.NbPreventions > 0)
                    continue;

                commandesEnRetard.Add(p.IdCommande);
            }

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
                NbCommandesEnRetard    = commandesEnRetard.Count,
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
