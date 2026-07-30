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

        private static readonly object _verrou = new();

        public CommissionService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<CommissionService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        // ÉTAPE 1b-i (Point 1 — Commandes multi-pièces) : le moteur de calcul
        // de commission opère maintenant sur PieceCommande, plus sur Commande
        // directement. C'est un changement CRITIQUE et pas seulement
        // cosmétique : depuis que CommandeService ne renseigne plus jamais
        // Commande.IdCouturier/Statut/MontantTotal (dépréciés, voir Commande.cs),
        // continuer à interroger ces champs ici aurait fait calculer un
        // aperçu de commission TOUJOURS VIDE pour tout le monde, silencieusement
        // (aucune exception levée — juste "0 couturier(s) éligible(s)").
        public List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, decimal pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre)
        {
            using var context = _contextFactory.CreateDbContext();

            var query = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Where(p => (p.Statut == "Terminee" || p.Statut == "Livree") &&
                            p.Commande != null &&
                            p.Commande.DateFin.Date >= dateDebut.Date &&
                            p.Commande.DateFin.Date <= dateFin.Date &&
                            p.IdCouturier.HasValue &&
                            p.IdCommission == null);

            if (idCouturierFiltre.HasValue)
                query = query.Where(p => p.IdCouturier == idCouturierFiltre.Value);

            var pieces = query.ToList();

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

                decimal caTotal = piecesCouturier.Sum(p => p.MontantCouture);

                // CaEncaisse par pièce : pour l'instant (tant qu'une commande
                // ne contient qu'UNE SEULE pièce, garanti jusqu'à l'Étape
                // 1b-ii), le montant encaissé sur la commande entière
                // équivaut exactement au montant encaissé pour cette pièce.
                // ATTENTION — dès que l'UI multi-pièces (1b-ii) puis le
                // Point 2 (matériel) seront en place, cette ligne devra être
                // remplacée par la répartition au prorata décrite dans le
                // cahier des charges V2 (Point 2) : un acompte non détaillé
                // se répartit au prorata du montant de chaque pièce de la
                // commande, et seule la part "couture" de cette répartition
                // entre dans la base de commission.
                decimal caEncaisse = piecesCouturier.Sum(p => p.Commande?.MontantEncaisse ?? 0m);

                decimal base_ = surMontantEncaisse ? caEncaisse : caTotal;
                decimal commission = Math.Round(base_ * (pourcentage / 100m), 0);

                resultat.Add(new ApercuCommission
                {
                    IdEmploye = couturier.IdEmploye,
                    Nom = couturier.Prenom + " " + couturier.Nom,
                    NbCommandes = piecesCouturier.Select(p => p.IdCommande).Distinct().Count(),
                    CaTotal = caTotal,
                    CaEncaisse = caEncaisse,
                    BaseCalcul = base_,
                    Commission = commission,
                    IdsCommandes = piecesCouturier.Select(p => p.IdCommande).Distinct().ToList(),
                    IdsPieces = piecesCouturier.Select(p => p.IdPieceCommande).ToList()
                });
            }

            _logger.LogInformation(
                "Aperçu commission calculé — période {Debut:dd/MM/yyyy}→{Fin:dd/MM/yyyy} " +
                "— {Pct}% — {NbCouturiers} couturier(s)",
                dateDebut, dateFin, pourcentage, resultat.Count);

            return resultat;
        }

        public void EnregistrerCommissions(
            List<ApercuCommission> apercu, DateTime dateDebut, DateTime dateFin,
            decimal pourcentage, bool surMontantEncaisse, int idOperateur, string nomOperateur)
        {
            if (apercu == null || apercu.Count == 0)
                throw new InvalidOperationException("Aucune commission à enregistrer pour cette période.");

            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();
                using var transaction = context.Database.BeginTransaction();

                foreach (var ligne in apercu)
                {
                    if (ligne.IdsPieces.Count == 0) continue;

                    // Reverrouille en base (et non sur la liste déjà en mémoire
                    // dans "ligne") : entre l'aperçu affiché à l'écran et le
                    // clic sur "Enregistrer", une pièce a pu être verrouillée
                    // entre-temps par une autre opération. Le filtre
                    // "IdCommission == null" ici est ce qui empêche réellement
                    // qu'une même pièce soit comptée deux fois.
                    var pieces = context.PiecesCommande
                        .Where(p => ligne.IdsPieces.Contains(p.IdPieceCommande) && p.IdCommission == null)
                        .ToList();

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
                        // Voir le commentaire équivalent dans CalculerApercu :
                        // tant qu'une commande = une pièce, l'encaissé de la
                        // commande équivaut à celui de sa pièce unique.
                        var idsCommandesConcernees = pieces.Select(p => p.IdCommande).Distinct().ToList();
                        decimal encaisseTotal = context.Commandes
                            .Include(c => c.Paiements)
                            .Where(c => idsCommandesConcernees.Contains(c.IdCommande))
                            .ToList()
                            .Sum(c => c.MontantEncaisse);
                        commission.BaseMontant = encaisseTotal;
                    }
                    else
                    {
                        commission.BaseMontant = pieces.Sum(p => p.MontantCouture);
                    }

                    commission.MontantCommission =
                        Math.Round(commission.BaseMontant * (pourcentage / 100m), 0);

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

        public void Annuler(int idCommission, string motif, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            var commission = context.Commissions
                .Include(c => c.Commandes) // legacy : historique enregistré avant l'Étape 1b-i
                .Include(c => c.Pieces)    // ÉTAPE 1b-i : verrouillage réel désormais ici
                .FirstOrDefault(c => c.IdCommission == idCommission)
                ?? throw new InvalidOperationException("Commission introuvable.");

            if (commission.EstAnnulee)
                throw new InvalidOperationException("Cette commission est déjà annulée.");

            commission.EstAnnulee = true;
            commission.MotifAnnulation = motif.Trim();
            commission.DateAnnulation = DateTime.Now;
            commission.NomAnnulateur = nomAnnulateur;

            // CORRECTIF (Étape 1b-i) : sans déverrouiller aussi "Pieces", une
            // commission annulée laissait ses pièces à jamais verrouillées
            // (IdCommission toujours renseigné) — elles n'auraient plus jamais
            // pu être incluses dans un futur calcul de commission
            // (CalculerApercu filtre explicitement IdCommission == null),
            // alors même que la commission qui les verrouillait est annulée.
            foreach (var cmd in commission.Commandes)
#pragma warning disable CS0618 // déverrouillage de l'historique légataire, avant l'Étape 1b-i
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
