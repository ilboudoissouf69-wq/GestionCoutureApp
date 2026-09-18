using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class DepenseService : IDepenseService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public DepenseService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<Depense> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Depenses
                .OrderByDescending(d => d.DateDepense)
                .ToList();
        }

        public List<Depense> Filtrer(DateTime debut, DateTime fin,
                                     string? categorie = null,
                                     string? statutValidation = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var q = context.Depenses
                .Where(d => d.DateDepense.Date >= debut.Date &&
                            d.DateDepense.Date <= fin.Date);

            if (!string.IsNullOrEmpty(categorie) && categorie != "Toutes")
                q = q.Where(d => d.Categorie == categorie);

            if (!string.IsNullOrEmpty(statutValidation) && statutValidation != "Tous")
            {
                if (statutValidation == "Annulée")
                    q = q.Where(d => d.EstAnnulee);
                else
                    q = q.Where(d => !d.EstAnnulee && d.StatutValidation == statutValidation);
            }

            return q.OrderByDescending(d => d.DateDepense).ToList();
        }

        public void Ajouter(Depense depense)
        {
            using var context = _contextFactory.CreateDbContext();
            context.Depenses.Add(depense);
            context.SaveChanges();
        }

        public void Valider(int idDepense, string nomBoss)
        {
            using var context = _contextFactory.CreateDbContext();

            // ✅ CORRECTIF AUDIT #8 : Vérifier que le validateur est Boss
            var validateur = context.Employes.FirstOrDefault(e => 
                (e.Prenom + " " + e.Nom) == nomBoss);
            Helpers.AuthorizationHelper.RequireRole(validateur, "Boss");

            var dep = context.Depenses.Find(idDepense)
                ?? throw new InvalidOperationException("Dépense introuvable.");
            if (dep.EstAnnulee)
                throw new InvalidOperationException("Impossible de valider une dépense annulée.");
            dep.StatutValidation = "Validee";
            context.SaveChanges();
        }

        public void Annuler(int idDepense, string motif, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            // ✅ CORRECTIF AUDIT #8 : Seul le Boss peut annuler une dépense
            var annulateur = context.Employes.FirstOrDefault(e => 
                (e.Prenom + " " + e.Nom) == nomAnnulateur);
            Helpers.AuthorizationHelper.RequireRole(annulateur, "Boss");

            var depense = context.Depenses.Find(idDepense)
                ?? throw new InvalidOperationException("Dépense introuvable.");
            if (depense.EstAnnulee)
                throw new InvalidOperationException("Cette dépense est déjà annulée.");

            depense.EstAnnulee = true;
            depense.MotifAnnulation = motif.Trim();
            depense.DateAnnulation = DateTime.Now;
            depense.NomAnnulateur = nomAnnulateur;
            context.SaveChanges();
        }

        public decimal TotalParPeriode(DateTime debut, DateTime fin)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Depenses
                .Where(d => !d.EstAnnulee &&
                            d.StatutValidation == "Validee" &&
                            d.DateDepense.Date >= debut.Date &&
                            d.DateDepense.Date <= fin.Date)
                .AsEnumerable()
                .Sum(d => d.Montant);
        }

        public StatsFinancieres ObtenirStats(DateTime debut, DateTime fin)
        {
            using var context = _contextFactory.CreateDbContext();

            // CA encaissé (paiements non annulés)
            decimal caEncaisse = context.Paiements
                .Where(p => !p.EstAnnule &&
                            p.DatePaiement.Date >= debut.Date &&
                            p.DatePaiement.Date <= fin.Date)
                .AsEnumerable()
                .Sum(p => p.MontantPaye);

            // Commissions validées
            decimal totalCommissions = context.Commissions
                .Where(c => !c.EstAnnulee &&
                            c.DateCalcul.Date >= debut.Date &&
                            c.DateCalcul.Date <= fin.Date)
                .AsEnumerable()
                .Sum(c => c.MontantCommission + c.PrimeQualite);

            // Matériaux facturés clients (sur les commandes de la période)
            // ✅ CORRECTIF AUDIT #3 : Les matériaux représentent un COÛT pour l'atelier
            // (tissu/boutons achetés et refacturés au client). Ils sont déduits dans
            // StatsFinancieres.MargeBrute = CA - Commissions - Matériaux.
            // Si l'atelier applique une marge sur les matériaux (ex: acheté 5k, vendu 7k),
            // il faudrait stocker le coût d'achat réel dans MaterielSupplement.CoutAchat.
            decimal totalMateriaux = context.MaterielsSupplements
                .Include(m => m.Commande)
                .Where(m => m.Commande != null &&
                            m.Commande.DateFin.Date >= debut.Date &&
                            m.Commande.DateFin.Date <= fin.Date)
                .AsEnumerable()
                .Sum(m => m.Quantite * m.PrixUnitaire);

            // Dépenses validées
            decimal totalDepenses = context.Depenses
                .Where(d => !d.EstAnnulee &&
                            d.StatutValidation == "Validee" &&
                            d.DateDepense.Date >= debut.Date &&
                            d.DateDepense.Date <= fin.Date)
                .AsEnumerable()
                .Sum(d => d.Montant);

            // Salaire secrétaire depuis Paramètres
            decimal salaireSecretaire = 0m;
            var paramSalaire = context.Parametres.Find("SalaireMensuelSecretaire");
            if (paramSalaire != null)
                decimal.TryParse(paramSalaire.Valeur, out salaireSecretaire);

            // Prorata mensuel du salaire selon la période
            double nbJoursPeriode = (fin.Date - debut.Date).TotalDays + 1;
            double nbJoursMois = DateTime.DaysInMonth(debut.Year, debut.Month);
            salaireSecretaire = Math.Round(salaireSecretaire * (decimal)(nbJoursPeriode / nbJoursMois), 0);

            return new StatsFinancieres
            {
                CaEncaisse       = caEncaisse,
                TotalCommissions = totalCommissions,
                TotalMateriaux   = totalMateriaux,
                TotalDepenses    = totalDepenses,
                SalaireSecretaire = salaireSecretaire
            };
        }
    }
}
