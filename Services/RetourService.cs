using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Étape 6 — Service Retours complet.
    /// Génération NumeroRetour en transaction, machine à états Signale→En reprise→Termine→Rendu,
    /// compteurs couturiers (imputés / reprises effectuées) sur passage à "Termine",
    /// retour arrière Boss only, taux de retour dans les statistiques.
    /// </summary>
    public class RetourService : IRetourService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IAuditService _auditService;

        public RetourService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IAuditService auditService)
        {
            _contextFactory = contextFactory;
            _auditService   = auditService;
        }

        // ── Includes systématiques ────────────────────────────────────────
        private static IQueryable<Retour> AvecIncludes(ApplicationDbContext ctx) =>
            ctx.Retours
               .Include(r => r.Commande).ThenInclude(c => c!.Client)
               .Include(r => r.PieceCommande)
               .Include(r => r.Couturier)
               .Include(r => r.CouturierReprise);

        // ═══════════════════════════════════════════════════════════════════
        // LECTURE
        // ═══════════════════════════════════════════════════════════════════

        public List<Retour> ObtenirTous()
        {
            using var ctx = _contextFactory.CreateDbContext();
            try
            {
                return AvecIncludes(ctx)
                    .OrderByDescending(r => r.DateSignalement)
                    .ToList();
            }
            catch
            {
                // Fallback si CouturierReprise pas encore en base
                return ctx.Retours
                    .Include(r => r.Commande).ThenInclude(c => c!.Client)
                    .Include(r => r.PieceCommande)
                    .Include(r => r.Couturier)
                    .OrderByDescending(r => r.DateSignalement)
                    .ToList();
            }
        }

        public Retour? ObtenirParId(int id)
        {
            using var ctx = _contextFactory.CreateDbContext();
            return AvecIncludes(ctx).FirstOrDefault(r => r.IdRetour == id);
        }

        public async Task<PagedResult<Retour>> ObtenirPageAsync(
            int page, int pageSize,
            string? motCle  = null,
            string? statut  = null,
            DateTime? dateDebut = null,
            DateTime? dateFin   = null)
        {
            using var ctx = _contextFactory.CreateDbContext();

            var query = ctx.Retours
                .Include(r => r.Commande).ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .AsQueryable();

            // Filtre texte : NumeroRetour, nom client, téléphone, numéro de commande
            if (!string.IsNullOrWhiteSpace(motCle))
            {
                string m = motCle.Trim().ToLower();
                query = query.Where(r =>
                    r.NumeroRetour.ToLower().Contains(m) ||
                    (r.Commande != null && r.Commande.Client != null &&
                     (r.Commande.Client.Nom.ToLower().Contains(m)    ||
                      r.Commande.Client.Prenom.ToLower().Contains(m) ||
                      (r.Commande.Client.Telephone != null && r.Commande.Client.Telephone.ToLower().Contains(m)))) ||
                    (r.PieceCommande != null && r.PieceCommande.TypeVetement.ToLower().Contains(m)) ||
                    r.DescriptionProbleme.ToLower().Contains(m) ||
                    (r.Couturier != null && (r.Couturier.Nom + " " + r.Couturier.Prenom).ToLower().Contains(m)));
            }

            // Filtre statut (mappe les libellés affichés → valeurs DB)
            if (!string.IsNullOrEmpty(statut) && statut != "Tous")
            {
                string statutDb = statut switch
                {
                    "Signalé"         => "Signale",
                    "Terminé ✓"       => "Termine",
                    "Rendu au client" => "Rendu",
                    _                 => statut
                };
                // Compat. ancienne valeur "Pret"
                if (statutDb == "Termine")
                    query = query.Where(r => (r.Statut == "Termine" || r.Statut == "Pret") && !r.EstAnnule);
                else
                    query = query.Where(r => r.Statut == statutDb && !r.EstAnnule);
            }

            if (dateDebut.HasValue)
                query = query.Where(r => r.DateSignalement.Date >= dateDebut.Value.Date);
            if (dateFin.HasValue)
                query = query.Where(r => r.DateSignalement.Date <= dateFin.Value.Date);

            var total = await query.CountAsync();
            var items = await query
                .OrderByDescending(r => r.DateSignalement)
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Retour>
            {
                Items      = items,
                TotalCount = total,
                Page       = page,
                PageSize   = pageSize
            };
        }

        public List<Retour> Rechercher(string motCle)
        {
            using var ctx = _contextFactory.CreateDbContext();
            string m = motCle.Trim().ToLower();
            return AvecIncludes(ctx)
                .Where(r =>
                    r.NumeroRetour.ToLower().Contains(m) ||
                    (r.Commande != null && r.Commande.Client != null &&
                     (r.Commande.Client.Nom.ToLower().Contains(m) ||
                      r.Commande.Client.Prenom.ToLower().Contains(m) ||
                      (r.Commande.Client.Telephone != null && r.Commande.Client.Telephone.Contains(m)))) ||
                    r.DescriptionProbleme.ToLower().Contains(m) ||
                    r.Statut.ToLower().Contains(m) ||
                    (r.Couturier != null && (r.Couturier.Nom.ToLower().Contains(m) || r.Couturier.Prenom.ToLower().Contains(m))) ||
                    (r.PieceCommande != null && r.PieceCommande.TypeVetement.ToLower().Contains(m)))
                .OrderByDescending(r => r.DateSignalement)
                .ToList();
        }

        public List<Retour> FiltrerParStatut(string statut)
        {
            using var ctx = _contextFactory.CreateDbContext();
            var q = AvecIncludes(ctx);
            if (statut != "Tous")
            {
                if (statut == "Termine")
                    q = q.Where(r => (r.Statut == "Termine" || r.Statut == "Pret") && !r.EstAnnule);
                else
                    q = q.Where(r => r.Statut == statut && !r.EstAnnule);
            }
            return q.OrderByDescending(r => r.DateSignalement).ToList();
        }

        // ═══════════════════════════════════════════════════════════════════
        // ÉCRITURE
        // ═══════════════════════════════════════════════════════════════════

        public void Ajouter(Retour retour)
        {
            using var ctx = _contextFactory.CreateDbContext();

            var piece = ctx.PiecesCommande.Find(retour.IdPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            if (piece.Statut != "Livree" && piece.Statut != "Terminee")
                throw new InvalidOperationException(
                    $"Impossible d'enregistrer un retour : la pièce n'est pas encore terminée (statut : {piece.StatutAffiche}).");

            retour.DateSignalement = DateTime.Now;
            retour.Statut          = "Signale";
            retour.NumeroRetour    = string.Empty; // sera généré dans la transaction

            // ── Transaction + génération du numéro ──────────────────────
            using var tx = ctx.Database.BeginTransaction();
            try
            {
                // Verrou via SELECT → on met à jour le compteur dans la même transaction
                int annee = retour.DateSignalement.Year;
                var compteur = ctx.RetourCompteurs.Find(annee);
                if (compteur == null)
                {
                    compteur = new RetourCompteur { Annee = annee, DernierNumero = 0 };
                    ctx.RetourCompteurs.Add(compteur);
                    ctx.SaveChanges(); // persiste pour pouvoir le modifier
                }

                compteur.DernierNumero++;
                retour.NumeroRetour = $"RET-{annee}-{compteur.DernierNumero:D5}";

                ctx.Retours.Add(retour);
                ctx.SaveChanges();
                tx.Commit();
            }
            catch
            {
                tx.Rollback();
                throw;
            }
        }

        public void Modifier(Retour retour)
        {
            using var ctx = _contextFactory.CreateDbContext();
            var existant = ctx.Retours.Find(retour.IdRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (existant.EstAnnule)
                throw new InvalidOperationException("Un retour annulé ne peut pas être modifié.");

            existant.DescriptionProbleme = retour.DescriptionProbleme;
            existant.IdCouturierReprise  = retour.IdCouturierReprise;
            existant.DateRdvReprise      = retour.DateRdvReprise;
            existant.HeureDebutReprise   = retour.HeureDebutReprise;
            existant.HeureFinReprise     = retour.HeureFinReprise;
            if (!string.IsNullOrEmpty(retour.CheminPhotoDefaut))
                existant.CheminPhotoDefaut = retour.CheminPhotoDefaut;

            ctx.SaveChanges();
        }

        // ═══════════════════════════════════════════════════════════════════
        // TRANSITIONS D'ÉTAT
        // ═══════════════════════════════════════════════════════════════════

        public void DemarrerReprise(int idRetour, int idOperateur, string nomOperateur)
        {
            using var ctx = _contextFactory.CreateDbContext();
            var retour = ctx.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.Statut != "Signale")
                throw new InvalidOperationException("Seul un retour 'Signalé' peut passer en reprise.");

            retour.Statut = "En reprise";
            ctx.SaveChanges();

            _ = _auditService.EnregistrerActionAsync(
                idOperateur, nomOperateur, "Operateur",
                "RETOUR_STATUT_MODIFIE", "Retour", idRetour,
                new { StatutAvant = "Signale" },
                new { StatutApres = "En reprise" });
        }

        /// <summary>
        /// Signale → Termine.
        /// Les compteurs couturiers sont mis à jour ici :
        ///   - IdCouturier (couturier responsable)  : RetoursImputes++
        ///   - IdCouturierReprise ou IdCouturier   : ReprisesEffectuees++
        /// Les deux sont tracés avec la DateResolution.
        /// </summary>
        public void Resoudre(int idRetour, int idOperateur, string nomOperateur)
        {
            using var ctx = _contextFactory.CreateDbContext();
            var retour = ctx.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.Statut != "En reprise")
                throw new InvalidOperationException("Seul un retour 'En reprise' peut être marqué Terminé.");

            string statutAvant      = retour.Statut;
            retour.Statut           = "Termine";
            retour.DateResolution   = DateTime.Now;
            retour.IdOperateurResolution  = idOperateur;
            retour.NomOperateurResolution = nomOperateur;
            ctx.SaveChanges();

            _ = _auditService.EnregistrerActionAsync(
                idOperateur, nomOperateur, "Operateur",
                "RETOUR_TERMINE", "Retour", idRetour,
                new { StatutAvant = statutAvant },
                new { StatutApres = "Termine", DateResolution = retour.DateResolution });
        }

        /// <summary>
        /// Termine → Rendu.
        /// Interdit si la reprise est payante et qu'il reste un solde (via PieceCommande).
        /// </summary>
        public void MarquerRendu(int idRetour, int idOperateur, string nomOperateur)
        {
            using var ctx = _contextFactory.CreateDbContext();
            var retour = ctx.Retours
                .Include(r => r.PieceCommande)
                    .ThenInclude(p => p!.Commande)
                        .ThenInclude(c => c!.Paiements)
                .FirstOrDefault(r => r.IdRetour == idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (!retour.EstTermine)
                throw new InvalidOperationException("Seul un retour 'Terminé' peut être marqué 'Rendu au client'.");

            // ── Garde : reste à payer sur la commande associée ──────────
            // Si la reprise a généré un supplément (prix couture > 0 sur la pièce),
            // on bloque le passage à Rendu jusqu'au solde intégral.
            if (retour.PieceCommande?.Commande != null)
            {
                var commande = retour.PieceCommande.Commande;
                decimal totalPaye    = commande.Paiements
                    .Where(p => !p.EstAnnule)
                    .Sum(p => p.MontantPaye);
                decimal totalFacture = commande.Pieces != null
                    ? commande.Pieces.Sum(p => p.MontantCouture)
                    : commande.MontantTotal;
                decimal resteAPayer = totalFacture - totalPaye;

                if (resteAPayer > 0.01m)
                    throw new InvalidOperationException(
                        $"Impossible de marquer ce retour 'Rendu' : il reste {resteAPayer:N0} FCFA à régler sur la commande.");
            }

            retour.Statut = "Rendu";
            ctx.SaveChanges();

            _ = _auditService.EnregistrerActionAsync(
                idOperateur, nomOperateur, "Operateur",
                "RETOUR_RENDU", "Retour", idRetour,
                new { StatutAvant = "Termine" },
                new { StatutApres = "Rendu" });
        }

        /// <summary>
        /// Retour arrière de statut — Boss uniquement, motif obligatoire.
        /// Si le statut était "Termine" (ou "Pret"), les compteurs sont décrémentés.
        /// Transitions autorisées : Rendu→Termine, Termine→En reprise, En reprise→Signale.
        /// </summary>
        public void ReculerStatut(int idRetour, string motif, int idOperateur, string nomOperateur, string roleOperateur)
        {
            if (roleOperateur != "Boss")
                throw new UnauthorizedAccessException("Seul le Boss peut effectuer un retour arrière de statut.");

            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif du retour arrière est obligatoire.");

            using var ctx = _contextFactory.CreateDbContext();
            var retour = ctx.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.EstAnnule)
                throw new InvalidOperationException("Un retour annulé ne peut pas être reculé.");

            string statutAvant = retour.Statut;
            string statutApres = statutAvant switch
            {
                "Rendu"       => "Termine",
                "Termine"     => "En reprise",
                "Pret"        => "En reprise",   // compat. ancienne valeur
                "En reprise"  => "Signale",
                _ => throw new InvalidOperationException($"Retour arrière impossible depuis le statut '{statutAvant}'.")
            };

            // Si on recule depuis Termine (ou Pret), le compteur doit être décrémenté
            bool etaitTermine = statutAvant is "Termine" or "Pret";

            retour.Statut                    = statutApres;
            retour.MotifRetourStatut         = motif.Trim();
            retour.DateRetourStatut          = DateTime.Now;
            retour.NomOperateurRetourStatut  = nomOperateur;

            // Effacer DateResolution si on recule depuis Termine
            if (etaitTermine)
            {
                retour.DateResolution          = null;
                retour.IdOperateurResolution   = null;
                retour.NomOperateurResolution  = null;
            }

            ctx.SaveChanges();

            _ = _auditService.EnregistrerActionAsync(
                idOperateur, nomOperateur, roleOperateur,
                "RETOUR_STATUT_RECULE", "Retour", idRetour,
                new { StatutAvant = statutAvant },
                new { StatutApres = statutApres, Motif = motif });
        }

        public void Annuler(int idRetour, string motif, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var ctx = _contextFactory.CreateDbContext();
            var retour = ctx.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.EstAnnule)
                throw new InvalidOperationException("Ce retour est déjà annulé.");

            bool etaitTermine = retour.EstTermine;

            retour.EstAnnule       = true;
            retour.MotifAnnulation = motif.Trim();
            retour.DateAnnulation  = DateTime.Now;
            retour.NomAnnulateur   = nomAnnulateur;

            // Si annulé après Termine, effacer DateResolution pour cohérence compteurs
            if (etaitTermine)
            {
                retour.DateResolution         = null;
                retour.IdOperateurResolution  = null;
                retour.NomOperateurResolution = null;
            }

            ctx.SaveChanges();

            // L'audit est géré par l'appelant (RetoursView) qui a le rôle et l'id
        }

        // ═══════════════════════════════════════════════════════════════════
        // STATISTIQUES
        // ═══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Statistiques couturiers sur une période.
        /// Un retour compte dans "RetoursImputes" quand il a atteint "Termine" (DateResolution non null).
        /// Un retour annulé après Termine ne compte pas (DateResolution = null après annulation).
        /// Taux de retour = RetoursImputes / PiecesLivrees * 100.
        /// </summary>
        public List<StatistiqueRetourCouturier> StatistiquesParCouturier(DateTime dateDebut, DateTime dateFin)
        {
            using var ctx = _contextFactory.CreateDbContext();

            // Retours terminés sur la période (DateResolution dans la fenêtre)
            // Non annulés uniquement
            var retours = ctx.Retours
                .Include(r => r.Couturier)
                .Where(r => !r.EstAnnule)
                .ToList();

            // Retours imputés = ceux qui sont terminés dans la période (DateResolution non null et dans la fenêtre)
            var retoursTerminesPeriode = retours
                .Where(r => r.DateResolution.HasValue &&
                            r.DateResolution.Value.Date >= dateDebut.Date &&
                            r.DateResolution.Value.Date <= dateFin.Date)
                .ToList();

            // Retours en cours (non terminés, signalement dans la période)
            var retoursEnCours = retours
                .Where(r => !r.EstTermine &&
                            r.DateSignalement.Date >= dateDebut.Date &&
                            r.DateSignalement.Date <= dateFin.Date)
                .ToList();

            // Pièces livrées/terminées sur la période (pour le taux)
            var pieces = ctx.PiecesCommande
                .Include(p => p.Commande)
                .Where(p => (p.Statut == "Terminee" || p.Statut == "Livree") &&
                             p.IdCouturier.HasValue)
                .ToList()
                .Where(p => {
                    var dt = p.DateTerminee ?? p.Commande?.DateFin;
                    return dt.HasValue &&
                           dt.Value.Date >= dateDebut.Date &&
                           dt.Value.Date <= dateFin.Date;
                })
                .ToList();

            // Union des couturiers impliqués
            var idsCouturiers = retoursTerminesPeriode.Select(r => r.IdCouturier)
                .Concat(retoursTerminesPeriode.Select(r => r.IdCouturierRepriseEffectif))
                .Concat(retoursEnCours.Select(r => r.IdCouturier))
                .Concat(pieces.Where(p => p.IdCouturier.HasValue).Select(p => p.IdCouturier!.Value))
                .Distinct()
                .ToList();

            var employes = ctx.Employes
                .Where(e => idsCouturiers.Contains(e.IdEmploye))
                .ToDictionary(e => e.IdEmploye);

            return idsCouturiers
                .Select(idC =>
                {
                    employes.TryGetValue(idC, out var emp);
                    string nom = emp != null ? emp.Prenom + " " + emp.Nom : $"#{idC}";

                    int imputes  = retoursTerminesPeriode.Count(r => r.IdCouturier == idC);
                    int reprises = retoursTerminesPeriode.Count(r => r.IdCouturierRepriseEffectif == idC);
                    int enCours  = retoursEnCours.Count(r => r.IdCouturier == idC);
                    int resolus  = retoursTerminesPeriode.Count(r =>
                        r.IdCouturier == idC && (r.Statut == "Termine" || r.Statut == "Rendu"));
                    int livrees  = pieces.Count(p => p.IdCouturier == idC);
                    var derniere = retoursTerminesPeriode
                        .Where(r => r.IdCouturier == idC && r.DateResolution.HasValue)
                        .Select(r => r.DateResolution)
                        .Max();

                    return new StatistiqueRetourCouturier
                    {
                        IdCouturier             = idC,
                        NomCouturier            = nom,
                        RetoursImputes          = imputes,
                        ReprisesEffectuees      = reprises,
                        NombreResolus           = resolus,
                        NombreEnCours           = enCours,
                        PiecesLivrees           = livrees,
                        DateDerniereResolution  = derniere
                    };
                })
                .OrderByDescending(s => s.RetoursImputes)
                .ThenByDescending(s => s.ReprisesEffectuees)
                .ToList();
        }

        // ═══════════════════════════════════════════════════════════════════
        // IMPRESSION (AUDIT)
        // ═══════════════════════════════════════════════════════════════════

        public async Task EnregistrerImpressionAsync(
            int idRetour, string typeTicket,
            int idOperateur, string nomOperateur, string roleOperateur)
        {
            await _auditService.EnregistrerActionAsync(
                idOperateur, nomOperateur, roleOperateur,
                "RETOUR_IMPRESSION", "Retour", idRetour,
                valeursAvant: null,
                valeursApres: new { TypeTicket = typeTicket, Date = DateTime.Now.ToString("dd/MM/yyyy HH:mm") });
        }
    }
}
