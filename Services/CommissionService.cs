using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    public class CommissionService : ICommissionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<CommissionService> _logger;
        private readonly IParametresService _parametresService;

        private static readonly object _verrou = new();

        public CommissionService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CommissionService> logger,
            IParametresService parametresService)
        {
            _contextFactory = contextFactory;
            _logger = logger;
            _parametresService = parametresService;
        }

        // Le moteur de calcul de commission opère sur PieceCommande, et non sur Commande
        // directement. Les champs Commande.IdCouturier/Statut/MontantTotal sont dépréciés
        // depuis la migration multi-pièces et ne sont plus jamais renseignés par CommandeService.
        public List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, decimal pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre)
        {
            using var context = _contextFactory.CreateDbContext();

            var query = context.PiecesCommande
                .Include(p => p.MaterielSupplements)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Pieces)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.MaterielSupplements)
                // Filtre sur DateTerminee (date réelle de terminaison), pas sur Commande.DateFin
                // (date de RDV). Une pièce sans DateTerminee n'est pas encore terminée.
                .Where(p => (p.Statut == "Terminee" || p.Statut == "Livree") &&
                            p.DateTerminee.HasValue &&
                            p.DateTerminee.Value.Date >= dateDebut.Date &&
                            p.DateTerminee.Value.Date <= dateFin.Date &&
                            p.Commande != null &&
                            p.IdCouturier.HasValue &&
                            p.IdCommission == null);

            if (idCouturierFiltre.HasValue)
                query = query.Where(p => p.IdCouturier == idCouturierFiltre.Value);

            var pieces = query.ToList();

        // Charger les retours non résolus pour exclure les pièces défectueuses.
        // Toute pièce avec un retour actif (Signalé ou En reprise) est exclue
        // de la commission jusqu'à résolution.
        var retoursNonResolus = context.Retours
                .Where(r => !r.EstAnnule &&
                            (r.Statut == "Signale" || r.Statut == "En reprise"))
                .Select(r => r.IdPieceCommande)
                .ToHashSet();

            // Exclure les pièces avec retours non résolus
            if (retoursNonResolus.Any())
            {
                pieces = pieces.Where(p => !retoursNonResolus.Contains(p.IdPieceCommande)).ToList();
                _logger.LogInformation(
                    "Aperçu commission — {NbExclus} pièce(s) exclue(s) (retours non résolus)",
                    retoursNonResolus.Count);
            }

            var couturiers = context.Employes
                .Where(e => e.Statut == "Actif" && (e.Role == "Couturier" || e.Role == "Boss"))
                .ToList();

            var resultat = new List<ApercuCommission>();

            foreach (var couturier in couturiers)
            {
                var piecesCouturier = pieces
                    .Where(p => p.IdCouturier == couturier.IdEmploye)
                    .ToList();

                if (piecesCouturier.Count == 0) continue;

                // CA couture uniquement — les matériaux sont exclus de la commission
                decimal caTotal = piecesCouturier.Sum(p => p.MontantCouture);

                // L'encaissé couture est la part des paiements attribuable à la couture
                // seule, répartie proportionnellement entre les pièces de la commande.
                decimal caEncaisse = piecesCouturier.Sum(p => PartEncaisseeDeLaPiece(p));

                decimal base_ = surMontantEncaisse ? caEncaisse : caTotal;
                decimal commission = Math.Round(base_ * (pourcentage / 100m), 0, MidpointRounding.AwayFromZero);

                // Matériaux rattachés aux pièces de ce couturier (exclus de la commission)
                decimal totalMateriaux = piecesCouturier
                    .SelectMany(p => p.MaterielSupplements ?? new List<MaterielSupplement>())
                    .Sum(m => m.Quantite * m.PrixUnitaire);

                // TotalEncaisse représente l'encaissé couture uniquement (hors matériaux).
                // Les matériaux sont un flux distinct refacturé au client et n'entrent
                // jamais dans la base de calcul des commissions ni dans le bénéfice atelier.
        decimal totalEncaisse = caEncaisse;

                resultat.Add(new ApercuCommission
                {
                    IdEmploye = couturier.IdEmploye,
                    Nom = couturier.Prenom + " " + couturier.Nom,
                    NbCommandes = piecesCouturier.Select(p => p.IdCommande).Distinct().Count(),
                    CaTotal = caTotal,
                    CaEncaisse = caEncaisse,
                    BaseCalcul = base_,
                    Commission = commission,
                    TotalMateriaux = totalMateriaux,
                    TotalEncaisse  = totalEncaisse,
                    IdsCommandes = piecesCouturier.Select(p => p.IdCommande).Distinct().ToList(),
                    IdsPieces = piecesCouturier.Select(p => p.IdPieceCommande).ToList()
                });
            }

            _logger.LogInformation(
                "Aperçu commission calculé — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy} " +
                "— {Pct}% — {NbCouturiers} couturier(s)",
                dateDebut, dateFin, pourcentage, resultat.Count);

            // Phase 2 : calcul de la prime qualité dans le service (plus dans la vue).
            // Les paramètres sont lus de façon synchrone via GetAwaiter().GetResult()
            // car CalculerApercu() est synchrone (SQLite local, pas de réseau).
            // Ce pattern est acceptable ici car le contexte d'exécution est un thread
            // de pool sans SynchronizationContext (pas de deadlock possible).
            int seuilPieces    = _parametresService.ObtenirSeuilPrimeNbCommandes().ConfigureAwait(false).GetAwaiter().GetResult();
            decimal montantPrime = _parametresService.ObtenirMontantPrimeQualite().ConfigureAwait(false).GetAwaiter().GetResult();

            // Charger les retours non résolus par couturier pour le calcul de prime
            var retoursParCouturier = context.Retours
                .Where(r => !r.EstAnnule)
                .GroupBy(r => r.IdCouturier)
                .Select(g => new { IdCouturier = g.Key, NbRetours = g.Count() })
                .ToDictionary(x => x.IdCouturier, x => x.NbRetours);

            foreach (var ap in resultat)
            {
                int nbRetours = retoursParCouturier.TryGetValue(ap.IdEmploye, out var nb) ? nb : 0;
                ap.NbRetours   = nbRetours;
                ap.TauxQualite = ap.NbCommandes > 0
                    ? Math.Max(0.0, 100.0 - (nbRetours * 100.0 / ap.NbCommandes))
                    : 100.0;
                ap.PrimeQualite = (ap.NbCommandes >= seuilPieces && nbRetours == 0)
                    ? montantPrime
                    : 0m;
            }

            return resultat;
        }

        // Calcule la part d'encaissé COUTURE qui revient à UNE pièce.
        //
        // Règle métier (cahier des charges Point 2) :
        //   La commission du couturier est calculée UNIQUEMENT sur les frais de
        //   couture — jamais sur les matériaux achetés pour le client.
        //   Les paiements du client couvrent couture + matériaux, mais seule la
        //   part couture entre dans la base de calcul.
        //
        // Méthode :
        //   1. Encaissé couture = min(encaissé total, total couture commande)
        //      → on plafonne à la valeur couture pour isoler la part matériaux
        //   2. Part de la pièce = encaissé couture × (couture pièce / total couture)
        private static decimal PartEncaisseeDeLaPiece(PieceCommande piece)
        {
            var commande = piece.Commande;
            if (commande == null) return 0m;

            // Total couture de la commande (hors matériaux)
            decimal totalCouture = commande.Pieces.Sum(p => p.MontantCouture);
            if (totalCouture <= 0m) return 0m;

            // Encaissé total (couture + matériaux potentiellement)
            decimal encaisseTotal = commande.MontantEncaisse;

            // On ne garde que la part couture de l'encaissé
            // (les matériaux ne rentrent JAMAIS dans la commission)
            decimal totalMateriaux = commande.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire);
            decimal encaisseCouture = Math.Max(0m, encaisseTotal - totalMateriaux);
            // Plafond : on ne peut pas dépasser le total couture
            encaisseCouture = Math.Min(encaisseCouture, totalCouture);

            // Part proportionnelle de cette pièce
            decimal proportion = piece.MontantCouture / totalCouture;
            return Math.Round(encaisseCouture * proportion, 0, MidpointRounding.AwayFromZero);
        }

        // Version multi-pièces qui garantit que la somme des parts == encaissé couture
        // en reportant le reliquat d'arrondi sur la dernière pièce de la commande.
        // À appeler uniquement quand on traite TOUTES les pièces d'une commande ensemble.
        internal static List<decimal> RepartirEncaisseSurPieces(
            IReadOnlyList<PieceCommande> pieces, decimal encaisseCouture)
        {
            if (pieces.Count == 0) return new List<decimal>();
            decimal totalCouture = pieces.Sum(p => p.MontantCouture);
            if (totalCouture <= 0m) return pieces.Select(_ => 0m).ToList();

            var parts = new List<decimal>(pieces.Count);
            decimal somme = 0m;
            for (int i = 0; i < pieces.Count - 1; i++)
            {
                decimal part = Math.Round(
                    encaisseCouture * (pieces[i].MontantCouture / totalCouture),
                    0, MidpointRounding.AwayFromZero);
                parts.Add(part);
                somme += part;
            }
            // Dernière pièce reçoit le reliquat pour que la somme soit exacte
            parts.Add(encaisseCouture - somme);
            return parts;
        }

        public void EnregistrerCommissions(
            List<ApercuCommission> apercu, DateTime dateDebut, DateTime dateFin,
            decimal pourcentage, bool surMontantEncaisse, int idOperateur, string nomOperateur)
        {
            if (apercu == null || apercu.Count == 0)
                throw new InvalidOperationException("Aucune commission à enregistrer pour cette période.");

            // Contrôle d'accès côté service : seul le Boss peut enregistrer des commissions.
            Helpers.AuthorizationHelper.RequireRoleById(_contextFactory, idOperateur, "Boss");

            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();
                using var transaction = context.Database.BeginTransaction();

                // Vérifie qu'aucune pièce n'a été verrouillée entre le calcul de l'aperçu
                // et l'enregistrement (race condition possible dans un atelier multi-poste futur).
                var conflits = new List<string>();

                foreach (var ligne in apercu)
                {
                    if (ligne.IdsPieces.Count == 0) continue;

                    var piecesVerrouillees = context.PiecesCommande
                        .Where(p => ligne.IdsPieces.Contains(p.IdPieceCommande) 
                                 && p.IdCommission != null)
                        .Select(p => new { p.IdPieceCommande, p.IdCommission })
                        .ToList();

                    if (piecesVerrouillees.Any())
                    {
                        var employe = context.Employes.Find(ligne.IdEmploye);
                        string nomCouturier = employe != null 
                            ? $"{employe.Prenom} {employe.Nom}" 
                            : ligne.Nom;
                        conflits.Add($"- {nomCouturier} : {piecesVerrouillees.Count} pièce(s) " +
                                   $"déjà commissionnée(s) (commission #{piecesVerrouillees[0].IdCommission})");
                    }
                }

                if (conflits.Any())
                {
                    transaction.Rollback();
                    _logger.LogWarning(
                        "Enregistrement commission refusé — {NbConflits} conflit(s) détecté(s)",
                        conflits.Count);
                    throw new InvalidOperationException(
                        "Impossible d'enregistrer : certaines pièces ont déjà été commissionnées " +
                        "depuis le calcul de l'aperçu.\n\n" +
                        string.Join("\n", conflits) +
                        "\n\nRecalculez un nouvel aperçu avec le bouton \"Aperçu\" et réessayez.");
                }

                // Enregistrement normal si aucun conflit
                foreach (var ligne in apercu)
                {
                    if (ligne.IdsPieces.Count == 0) continue;

                    // Include(Commande.Pieces) est nécessaire pour que PartEncaisseeDeLaPiece()
                    // calcule le total de la commande (toutes ses pièces) et pas seulement
                    // les pièces de ce couturier — sinon la proportion serait faussée.
                    var pieces = context.PiecesCommande
                        .Include(p => p.Commande)
                            .ThenInclude(c => c!.Pieces)
                        .Include(p => p.Commande)
                            .ThenInclude(c => c!.Paiements)
                        .Include(p => p.Commande)
                            .ThenInclude(c => c!.MaterielSupplements)
                        .Where(p => ligne.IdsPieces.Contains(p.IdPieceCommande) && p.IdCommission == null)
                        .ToList();

                    // Phase 2 : vérification que chaque pièce appartient bien à cet employé
                    // et que les conditions d'éligibilité sont toujours remplies.
                    var retoursNonResolus = context.Retours
                        .Where(r => !r.EstAnnule &&
                                    (r.Statut == "Signale" || r.Statut == "En reprise"))
                        .Select(r => r.IdPieceCommande)
                        .ToHashSet();

                    var piecesInvalides = pieces.Where(p =>
                        // Pièce n'appartient pas au bon couturier
                        p.IdCouturier != ligne.IdEmploye ||
                        // Statut invalide
                        (p.Statut != "Terminee" && p.Statut != "Livree") ||
                        // Pas de DateTerminee
                        !p.DateTerminee.HasValue ||
                        // DateTerminee hors période
                        p.DateTerminee.Value.Date < dateDebut.Date ||
                        p.DateTerminee.Value.Date > dateFin.Date ||
                        // Retour actif
                        retoursNonResolus.Contains(p.IdPieceCommande)
                    ).ToList();

                    if (piecesInvalides.Any())
                    {
                        transaction.Rollback();
                        _logger.LogWarning(
                            "Écart aperçu/base pour {Nom} — {Nb} pièce(s) invalide(s) à l'enregistrement",
                            ligne.Nom, piecesInvalides.Count);
                        throw new InvalidOperationException(
                            $"Écart détecté pour {ligne.Nom} : {piecesInvalides.Count} pièce(s) ne " +
                            "remplissent plus les conditions (statut, période, retour actif, ou couturier). " +
                            "Recalculez l'aperçu avant d'enregistrer.");
                    }

                    // Vérification de cohérence : si le nombre de pièces récupérées ne
                    // correspond pas à l'aperçu, une ou plusieurs ont été verrouillées entre-temps.
                    if (pieces.Count != ligne.IdsPieces.Count)
                    {
                        transaction.Rollback();
                        _logger.LogWarning(
                            "Incohérence détectée pour {Nom} — aperçu: {Apercu} pièces, base: {Base} pièces",
                            ligne.Nom, ligne.IdsPieces.Count, pieces.Count);
                        throw new InvalidOperationException(
                            $"Incohérence détectée pour {ligne.Nom} : " +
                            $"{ligne.IdsPieces.Count - pieces.Count} pièce(s) manquante(s). " +
                            "Une autre opération a modifié les données. Recalculez l'aperçu.");
                    }

                    if (pieces.Count == 0) continue;

                    var employe = context.Employes.Find(ligne.IdEmploye);

                    var commission = new Commission
                    {
                        IdEmploye = ligne.IdEmploye,
                        NomEmployeSnapshot = employe != null
                            ? employe.Prenom + " " + employe.Nom
                            : ligne.Nom,
                        DateDebutPeriode = dateDebut.Date,
                        DateFinPeriode = dateFin.Date,
                        BaseCalcul = surMontantEncaisse ? "Encaisse" : "Total",
                        Pourcentage = pourcentage,
                        NbCommandes = pieces.Select(p => p.IdCommande).Distinct().Count(),
                        DateCalcul = DateTime.Now,
                        IdOperateur = idOperateur,
                        NomOperateur = nomOperateur,
                        EstAnnulee = false
                    };

                    // Base recalculée en base (pas depuis "ligne", pour la même
                    // raison de fraîcheur que le filtre ci-dessus) :
                    if (surMontantEncaisse)
                    {
                        // La part proportionnelle réelle de chaque pièce est recalculée
                        // depuis la base — même logique que dans CalculerApercu.
                        commission.BaseMontant = pieces.Sum(p => PartEncaisseeDeLaPiece(p));
                    }
                    else
                    {
                        commission.BaseMontant = pieces.Sum(p => p.MontantCouture);
                    }

                    commission.MontantCommission =
                        Math.Round(commission.BaseMontant * (pourcentage / 100m), 0, MidpointRounding.AwayFromZero);

                    // Prime qualité zéro défaut (calculée dans l'aperçu et transmise ici)
                    commission.PrimeQualite = ligne.PrimeQualite;

                    context.Commissions.Add(commission);
                    context.SaveChanges();

                    foreach (var piece in pieces)
                        piece.IdCommission = commission.IdCommission;

                    context.SaveChanges();

                    _logger.LogInformation(
                        "Commission enregistrée — {Nom} — {NbPieces} pièce(s) — {Montant:N0} FCFA — opérateur {Op}",
                        commission.NomEmployeSnapshot, pieces.Count,
                        commission.MontantCommission, nomOperateur);
                }

                transaction.Commit();
            }
        }

        public List<Commission> ObtenirHistorique()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commissions
                .Include(c => c.Employe)
                .OrderByDescending(c => c.DateCalcul)
                .ToList();
        }

        public void Annuler(int idCommission, string motif, int idAnnulateur, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            // Seul le Boss peut annuler une commission.
            // Identification par ID (jamais par nom, pour éviter les homonymes).
            // On vérifie aussi que l'annulateur est Actif.
            var annulateur = context.Employes.Find(idAnnulateur);
            if (annulateur == null || annulateur.Statut != "Actif")
                throw new UnauthorizedAccessException(
                    "Opérateur introuvable ou inactif.");
            Helpers.AuthorizationHelper.RequireRoleEnum(annulateur, RoleEmploye.Boss);

            var commission = context.Commissions
                .Include(c => c.Commandes) // historique legacy (commissions antérieures au verrouillage par pièce)
                .Include(c => c.Pieces)    // verrouillage actuel par pièce
                .FirstOrDefault(c => c.IdCommission == idCommission)
                ?? throw new InvalidOperationException("Commission introuvable.");

            if (commission.EstAnnulee)
                throw new InvalidOperationException("Cette commission est déjà annulée.");

            commission.EstAnnulee = true;
            commission.MotifAnnulation = motif.Trim();
            commission.DateAnnulation = DateTime.Now;
            commission.NomAnnulateur = nomAnnulateur;

            // Déverrouiller les pièces : une commission annulée doit libérer
            // ses pièces pour qu'elles puissent être incluses dans un futur calcul.
            foreach (var cmd in commission.Commandes)
#pragma warning disable CS0618 // déverrouillage de l'historique legacy
                cmd.IdCommission = null;
#pragma warning restore CS0618
            foreach (var piece in commission.Pieces)
                piece.IdCommission = null;

            context.SaveChanges();

            _logger.LogWarning(
                "Commission {Id} ANNULÉE par {Annulateur} — {NbPieces} pièce(s) déverrouillée(s) — motif : {Motif}",
                idCommission, nomAnnulateur, commission.Pieces.Count, motif);
        }
    }
}
