using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class CommissionService : ICommissionService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        // Meme logique de verrou que PaiementService : evite qu'un double-clic
        // n'enregistre deux fois la meme commission.
        private static readonly object _verrou = new object();

        public CommissionService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, double pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre)
        {
            using var context = _contextFactory.CreateDbContext();

            // IMPORTANT : IdCommission == null => commande pas encore rattachee
            // a une commission enregistree => eligible a un nouveau calcul.
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
                var commandesDuCouturier = commandes
                    .Where(c => c.IdCouturier == couturier.IdEmploye)
                    .ToList();

                if (commandesDuCouturier.Count == 0) continue;

                double caTotal = commandesDuCouturier.Sum(c => c.MontantTotal);
                double caEncaisse = commandesDuCouturier.Sum(c => c.MontantEncaisse);
                double base_ = surMontantEncaisse ? caEncaisse : caTotal;
                double commission = base_ * (pourcentage / 100);

                resultat.Add(new ApercuCommission
                {
                    IdEmploye = couturier.IdEmploye,
                    Nom = couturier.Prenom + " " + couturier.Nom,
                    NbCommandes = commandesDuCouturier.Count,
                    CaTotal = caTotal,
                    CaEncaisse = caEncaisse,
                    BaseCalcul = base_,
                    Commission = commission,
                    IdsCommandes = commandesDuCouturier.Select(c => c.IdCommande).ToList()
                });
            }

            return resultat;
        }

        public void EnregistrerCommissions(
            List<ApercuCommission> apercu, DateTime dateDebut, DateTime dateFin,
            double pourcentage, bool surMontantEncaisse, int idOperateur, string nomOperateur)
        {
            if (apercu == null || apercu.Count == 0)
                throw new InvalidOperationException("Aucune commission a enregistrer pour cette periode.");

            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();
                using var transaction = context.Database.BeginTransaction();

                foreach (var ligne in apercu)
                {
                    if (ligne.IdsCommandes.Count == 0) continue;

                    // Revalider en base que ces commandes ne sont toujours pas
                    // deja verrouillees par une autre commission (protege contre
                    // une double validation presque simultanee).
                    var commandes = context.Commandes
                        .Where(c => ligne.IdsCommandes.Contains(c.IdCommande) && c.IdCommission == null)
                        .ToList();

                    if (commandes.Count == 0) continue;

                    var employe = context.Employes.Find(ligne.IdEmploye);

                    var commission = new Commission
                    {
                        IdEmploye = ligne.IdEmploye,
                        NomEmployeSnapshot = employe != null ? employe.Prenom + " " + employe.Nom : ligne.Nom,
                        DateDebutPeriode = dateDebut.Date,
                        DateFinPeriode = dateFin.Date,
                        BaseCalcul = surMontantEncaisse ? "Encaisse" : "Total",
                        Pourcentage = pourcentage,
                        BaseMontant = commandes.Sum(c => surMontantEncaisse ? c.MontantEncaisse : c.MontantTotal),
                        NbCommandes = commandes.Count,
                        DateCalcul = DateTime.Now,
                        IdOperateur = idOperateur,
                        NomOperateur = nomOperateur,
                        EstAnnulee = false
                    };
                    commission.MontantCommission = commission.BaseMontant * (pourcentage / 100);

                    context.Commissions.Add(commission);
                    context.SaveChanges(); // pour obtenir l'IdCommission genere

                    foreach (var cmd in commandes)
                        cmd.IdCommission = commission.IdCommission;

                    context.SaveChanges();
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
                throw new InvalidOperationException("Cette commission est deja annulee.");

            commission.EstAnnulee = true;
            commission.MotifAnnulation = motif.Trim();
            commission.DateAnnulation = DateTime.Now;
            commission.NomAnnulateur = nomAnnulateur;

            // Deverrouille les commandes : elles redeviennent eligibles a un futur calcul.
            foreach (var cmd in commission.Commandes)
                cmd.IdCommission = null;

            context.SaveChanges();
        }
    }
}
