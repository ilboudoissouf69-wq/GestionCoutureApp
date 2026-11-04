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
        private readonly IEventAggregator? _eventAggregator;

        public RetourService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IAuditService auditService,
            IEventAggregator? eventAggregator = null)
        {
            _contextFactory = contextFactory;
            _auditService   = auditService;
            _eventAggregator = eventAggregator;
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
            await using var ctx = await _contextFactory.CreateDbContextAsync();

            var query = ConstruireQueryRetours(ctx, motCle, statut, dateDebut, dateFin);

            var total = await query.CountAsync();
            var items = await query
                .Include(r => r.Commande).ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
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

        public async Task<Dictionary<string, int>> CompterParStatutAsync(
            string? motCle = null, string? statut = null,
            DateTime? dateDebut = null, DateTime? dateFin = null)
        {
            await using var ctx = await _contextFactory.CreateDbContextAsync();
            return await ConstruireQueryRetours(ctx, motCle, statut, dateDebut, dateFin)
                .GroupBy(r => r.Statut)
                .Select(g => new { Statut = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Statut, x => x.Count);
        }

        private static IQueryable<Retour> ConstruireQueryRetours(
            ApplicationDbContext ctx, string? motCle, string? statut,
            DateTime? dateDebut, DateTime? dateFin)
        {
            var query = ctx.Retours.AsNoTracking().AsQueryable();
            if (!string.IsNullOrWhiteSpace(motCle))
            {
                string cle = GestionCoutureApp.Helpers.TexteHelper.NormaliserPourRecherche(motCle.Trim());
                query = query.Where(r => r.NumeroRetour.Contains(motCle.Trim())
                    || (r.Commande != null && r.Commande.Client != null
                        && (r.Commande.Client.NomRecherche.StartsWith(cle)
                            || r.Commande.Client.PrenomNomRecherche.StartsWith(cle)
                            || r.Commande.Client.Telephone.StartsWith(motCle.Trim())))
                    || (r.PieceCommande != null && r.PieceCommande.TypeVetement.StartsWith(motCle.Trim()))
                    || r.DescriptionProbleme.StartsWith(motCle.Trim())
                    || (r.Couturier != null && (r.Couturier.Nom.StartsWith(motCle.Trim())
                                                || r.Couturier.Prenom.StartsWith(motCle.Trim()))));
            }
            if (!string.IsNullOrEmpty(statut) && statut != "Tous")
            {
                string db = statut switch
                {
                    "Signalé" => "Signale", "Terminé ✓" => "Termine",
                    "Rendu au client" => "Rendu", _ => statut
                };
                query = db == "Termine"
                    ? query.Where(r => !r.EstAnnule && (r.Statut == "Termine" || r.Statut == "Pret"))
                    : query.Where(r => !r.EstAnnule && r.Statut == db);
            }
            if (dateDebut.HasValue) query = query.Where(r => r.DateSignalement >= dateDebut.Value.Date);
            if (dateFin.HasValue) query = query.Where(r => r.DateSignalement < dateFin.Value.Date.AddDays(1));
            return query;
        }

        public List<Retour> Rechercher(string motCle)
        {
            using var ctx = _contextFactory.CreateDbContext();
            string m = GestionCoutureApp.Helpers.TexteHelper.NormaliserPourRecherche(motCle.Trim());
            return AvecIncludes(ctx).AsNoTracking()
                .Where(r =>
                    r.NumeroRetour.Contains(m) ||
                    (r.Commande != null && r.Commande.Client != null &&
                     (r.Commande.Client.NomRecherche.StartsWith(m) ||
                      r.Commande.Client.PrenomNomRecherche.StartsWith(m) ||
                      (r.Commande.Client.Telephone != null && r.Commande.Client.Telephone.StartsWith(m)))) ||
                    r.DescriptionProbleme.StartsWith(m) ||
                    r.Statut.StartsWith(m) ||
                    (r.Couturier != null && (r.Couturier.Nom.StartsWith(m) || r.Couturier.Prenom.StartsWith(m))) ||
                    (r.PieceCommande != null && r.PieceCommande.TypeVetement.StartsWith(m)))
                .OrderByDescending(r => r.DateSignalement)
                .Take(50)
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

            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });
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
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });
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
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });

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
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });

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
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });

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
        /// Phase 1.6 : le rôle est relu en base via l'identifiant — le paramètre roleOperateur
        /// est conservé pour compatibilité de signature mais ignoré au profit de la valeur DB.
        /// </summary>
        public void ReculerStatut(int idRetour, string motif, int idOperateur, string nomOperateur, string roleOperateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif du retour arrière est obligatoire.");

            // Phase 1.6 : vérification du rôle en base, pas sur le paramètre string
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(_contextFactory, idOperateur, RoleEmploye.Boss);

            using var ctx = _contextFactory.CreateDbContext();

            // Relire le nom réel en base pour l'audit
            var operateur = ctx.Employes.Find(idOperateur);
            string nomOperateurRelu = operateur != null
                ? operateur.Prenom + " " + operateur.Nom
                : nomOperateur;

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
            retour.NomOperateurRetourStatut  = nomOperateurRelu;

            // Effacer DateResolution si on recule depuis Termine
            if (etaitTermine)
            {
                retour.DateResolution          = null;
                retour.IdOperateurResolution   = null;
                retour.NomOperateurResolution  = null;
            }

            ctx.SaveChanges();

            _ = _auditService.EnregistrerActionAsync(
                idOperateur, nomOperateurRelu, "Boss",
                "RETOUR_STATUT_RECULE", "Retour", idRetour,
                new { StatutAvant = statutAvant },
                new { StatutApres = statutApres, Motif = motif });

            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });
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
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Retour });
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
            var debut = dateDebut.Date;
            var finExclusive = dateFin.Date.AddDays(1);
            var retoursTermines = ctx.Retours.AsNoTracking()
                .Where(r => !r.EstAnnule && r.DateResolution >= debut && r.DateResolution < finExclusive);

            var imputes = retoursTermines.GroupBy(r => r.IdCouturier)
                .Select(g => new
                {
                    Id = g.Key,
                    Count = g.Count(),
                    Resolus = g.Count(r => r.Statut == "Termine" || r.Statut == "Rendu"),
                    Derniere = g.Max(r => r.DateResolution)
                }).ToDictionary(x => x.Id);
            var reprises = retoursTermines.GroupBy(r => r.IdCouturierReprise ?? r.IdCouturier)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Id, x => x.Count);
            var enCours = ctx.Retours.AsNoTracking()
                .Where(r => !r.EstAnnule && r.Statut != "Termine" && r.Statut != "Pret" && r.Statut != "Rendu"
                         && r.DateSignalement >= debut && r.DateSignalement < finExclusive)
                .GroupBy(r => r.IdCouturier)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Id, x => x.Count);
            var piecesLivrees = ctx.PiecesCommande.AsNoTracking()
                .Where(p => p.IdCouturier.HasValue && (p.Statut == "Terminee" || p.Statut == "Livree")
                         && ((p.DateTerminee >= debut && p.DateTerminee < finExclusive)
                             || (!p.DateTerminee.HasValue && p.Commande != null
                                 && p.Commande.DateFin >= debut && p.Commande.DateFin < finExclusive)))
                .GroupBy(p => p.IdCouturier!.Value)
                .Select(g => new { Id = g.Key, Count = g.Count() })
                .ToDictionary(x => x.Id, x => x.Count);

            var ids = imputes.Keys.Concat(reprises.Keys).Concat(enCours.Keys).Concat(piecesLivrees.Keys)
                .Distinct().ToArray();
            var employes = ctx.Employes.AsNoTracking().Where(e => ids.Contains(e.IdEmploye))
                .ToDictionary(e => e.IdEmploye);

            return ids.Select(id =>
                {
                    employes.TryGetValue(id, out var employe);
                    imputes.TryGetValue(id, out var retour);
                    return new StatistiqueRetourCouturier
                    {
                        IdCouturier = id,
                        NomCouturier = employe != null ? employe.Prenom + " " + employe.Nom : $"#{id}",
                        RetoursImputes = retour?.Count ?? 0,
                        ReprisesEffectuees = reprises.GetValueOrDefault(id),
                        NombreResolus = retour?.Resolus ?? 0,
                        NombreEnCours = enCours.GetValueOrDefault(id),
                        PiecesLivrees = piecesLivrees.GetValueOrDefault(id),
                        DateDerniereResolution = retour?.Derniere
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
