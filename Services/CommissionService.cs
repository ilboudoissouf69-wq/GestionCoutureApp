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

        public List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, decimal pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre)
        {
            using var context = _contextFactory.CreateDbContext();

            var query = context.Commandes
                .Include(c => c.Paiements)
                .Where(c => (c.Statut == "Terminee" || c.Statut == "Livree") &&
                            c.DateFin.Date >= dateDebut.Date &&
                            c.DateFin.Date <= dateFin.Date &&
                            c.IdCouturier.HasValue &&
                            c.IdCommission == null);

            if (idCouturierFiltre.HasValue)
                query = query.Where(c => c.IdCouturier == idCouturierFiltre.Value);

            var commandes = query.ToList();

            var couturiers = context.Employes
                .Where(e => e.Statut == "Actif" && (e.Role == "Couturier" || e.Role == "Boss"))
                .ToList();

            var resultat = new List<ApercuCommission>();

            foreach (var couturier in couturiers)
            {
                var cmdsCouturier = commandes
                    .Where(c => c.IdCouturier == couturier.IdEmploye)
                    .ToList();

                if (cmdsCouturier.Count == 0) continue;

                // AsEnumerable() : SQLite ne supporte pas Sum() sur decimal côté SQL
                decimal caTotal    = cmdsCouturier.Sum(c => c.MontantTotal);
                decimal caEncaisse = cmdsCouturier.Sum(c => c.MontantEncaisse);
                decimal base_      = surMontantEncaisse ? caEncaisse : caTotal;
                decimal commission = Math.Round(base_ * (pourcentage / 100m), 0);

                resultat.Add(new ApercuCommission
                {
                    IdEmploye     = couturier.IdEmploye,
                    Nom           = couturier.Prenom + " " + couturier.Nom,
                    NbCommandes   = cmdsCouturier.Count,
                    CaTotal       = caTotal,
                    CaEncaisse    = caEncaisse,
                    BaseCalcul    = base_,
                    Commission    = commission,
                    IdsCommandes  = cmdsCouturier.Select(c => c.IdCommande).ToList()
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
                    if (ligne.IdsCommandes.Count == 0) continue;

                    var commandes = context.Commandes
                        .Where(c => ligne.IdsCommandes.Contains(c.IdCommande) && c.IdCommission == null)
                        .ToList();

                    if (commandes.Count == 0) continue;

                    var employe = context.Employes.Find(ligne.IdEmploye);

                    var commission = new Commission
                    {
                        IdEmploye         = ligne.IdEmploye,
                        NomEmployeSnapshot = employe != null
                            ? employe.Prenom + " " + employe.Nom
                            : ligne.Nom,
                        DateDebutPeriode  = dateDebut.Date,
                        DateFinPeriode    = dateFin.Date,
                        BaseCalcul        = surMontantEncaisse ? "Encaisse" : "Total",
                        Pourcentage       = pourcentage,
                        NbCommandes       = commandes.Count,
                        DateCalcul        = DateTime.Now,
                        IdOperateur       = idOperateur,
                        NomOperateur      = nomOperateur,
                        EstAnnulee        = false
                    };

                    // AsEnumerable() déjà appliqué (commandes est une List<> en mémoire)
                    commission.BaseMontant = commandes.Sum(c =>
                        surMontantEncaisse ? c.MontantEncaisse : c.MontantTotal);
                    commission.MontantCommission =
                        Math.Round(commission.BaseMontant * (pourcentage / 100m), 0);

                    context.Commissions.Add(commission);
                    context.SaveChanges();

                    foreach (var cmd in commandes)
                        cmd.IdCommission = commission.IdCommission;

                    context.SaveChanges();

                    _logger.LogInformation(
                        "Commission enregistrée — {Nom} — {NbCmd} commandes — {Montant:N0} FCFA — opérateur {Op}",
                        commission.NomEmployeSnapshot, commission.NbCommandes,
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
                .Include(c => c.Commandes)
                .FirstOrDefault(c => c.IdCommission == idCommission)
                ?? throw new InvalidOperationException("Commission introuvable.");

            if (commission.EstAnnulee)
                throw new InvalidOperationException("Cette commission est déjà annulée.");

            commission.EstAnnulee      = true;
            commission.MotifAnnulation = motif.Trim();
            commission.DateAnnulation  = DateTime.Now;
            commission.NomAnnulateur   = nomAnnulateur;

            foreach (var cmd in commission.Commandes)
                cmd.IdCommission = null;

            context.SaveChanges();

            _logger.LogWarning(
                "Commission {Id} ANNULÉE par {Annulateur} — {NbCmd} commandes déverrouillées — motif : {Motif}",
                idCommission, nomAnnulateur, commission.Commandes.Count, motif);
        }
    }
}
