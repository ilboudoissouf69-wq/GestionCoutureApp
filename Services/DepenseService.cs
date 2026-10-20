using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class DepenseService : IDepenseService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IEventAggregator? _eventAggregator;
        private readonly ITresorerieService? _tresorerieService;

        public DepenseService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IEventAggregator? eventAggregator = null,
            ITresorerieService? tresorerieService = null)
        {
            _contextFactory = contextFactory;
            _eventAggregator = eventAggregator;
            _tresorerieService = tresorerieService;
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
            // IdOperateur est obligatoire — 0 indique que l'appelant n'a pas
            // transmis l'utilisateur connecté, ce qui est une erreur de programmation.
            if (depense.IdOperateur <= 0)
                throw new InvalidOperationException(
                    "Impossible d'enregistrer une dépense sans opérateur identifié. " +
                    "Vérifiez que IdOperateur est renseigné depuis AuthService.UtilisateurConnecte.");

            using var context = _contextFactory.CreateDbContext();
            context.Depenses.Add(depense);
            context.SaveChanges();
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Depense });
        }

        public void Valider(int idDepense, string nomBoss)
        {
            using var context = _contextFactory.CreateDbContext();

            // Seul le Boss peut valider une dépense.
            var validateur = context.Employes.FirstOrDefault(e => 
                (e.Prenom + " " + e.Nom) == nomBoss);
            Helpers.AuthorizationHelper.RequireRole(validateur, "Boss");

            var dep = context.Depenses.Find(idDepense)
                ?? throw new InvalidOperationException("Dépense introuvable.");
            if (dep.EstAnnulee)
                throw new InvalidOperationException("Impossible de valider une dépense annulée.");
            dep.StatutValidation = "Validee";
            context.SaveChanges();
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Depense });
        }

        public void Annuler(int idDepense, string motif, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            // Seul le Boss peut annuler une dépense.
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
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Depense });
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

            // Commissions validées (couturiers + primes)
            decimal totalCommissions = context.Commissions
                .Where(c => !c.EstAnnulee &&
                            c.DateCalcul.Date >= debut.Date &&
                            c.DateCalcul.Date <= fin.Date)
                .AsEnumerable()
                .Sum(c => c.MontantCommission + c.PrimeQualite);

            // ── RÉPARTITION MATÉRIAUX / COUTURE via la méthode centrale ─────────────
            // Si TresorerieService est injecté on délègue directement pour éviter toute
            // duplication de formule. Sinon on applique la même logique en local.
            // Règle : matériaux remboursés EN PREMIER
            //   matériaux encaissés (cmd) = min( paye, totalMat )
            //   couture encaissée  (cmd)  = max( 0, paye − totalMat )
            decimal totalMateriaux;
            decimal materiauxNonRembourses;
            decimal caEncaisseCoutureCalcule;

            if (_tresorerieService != null)
            {
                var repartition = _tresorerieService.CalculerRepartitionCa(debut, fin);
                totalMateriaux         = repartition.TotalMateriauxFactures;
                materiauxNonRembourses = repartition.MateriauxNonRembourses;
                caEncaisseCoutureCalcule = repartition.CaEncaisseCouture;
            }
            else
            {
                // Fallback : même logique que TresorerieService.CalculerRepartitionCa
                var commandes = context.Commandes
                    .Include(c => c.Pieces)
                    .Include(c => c.MaterielSupplements)
                    .Include(c => c.Paiements)
                    .Where(c => !c.EstSupprimee
                             && c.DateFin.Date >= debut.Date
                             && c.DateFin.Date <= fin.Date)
                    .AsNoTracking()
                    .AsEnumerable()
                    .ToList();

                decimal matFactures   = 0m;
                decimal matNonRemb    = 0m;
                decimal coutureEnc    = 0m;

                var idsCommandesPeriode = commandes.Select(c => c.IdCommande).ToHashSet();

                foreach (var cmd in commandes)
                {
                    decimal totalMat = cmd.MaterielSupplements.Sum(m => m.Quantite * m.PrixUnitaire);
                    decimal paye     = cmd.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                    decimal totalCmd = cmd.Pieces.Sum(p => p.MontantCouture) + totalMat;

                    matFactures += totalMat;

                    if (totalMat <= 0m)
                    {
                        coutureEnc += paye;
                        continue;
                    }

                    decimal matEnc = Math.Min(paye, totalMat);
                    coutureEnc   += Math.Max(0m, paye - totalMat);

                    // Matériaux non remboursés = commande non soldée qui a des matériaux
                    // (alerte : les matériaux peuvent ne pas être intégralement récupérés)
                    if (paye < totalCmd - 0.01m)
                        matNonRemb += totalMat;
                }

                // Paiements hors-période (commandes hors DateFin)
                var payHors = context.Paiements
                    .Where(p => !p.EstAnnule
                             && p.DatePaiement.Date >= debut.Date
                             && p.DatePaiement.Date <= fin.Date
                             && !idsCommandesPeriode.Contains(p.IdCommande))
                    .AsEnumerable()
                    .Sum(p => p.MontantPaye);
                coutureEnc += payHors;

                totalMateriaux           = matFactures;
                materiauxNonRembourses   = matNonRemb;
                caEncaisseCoutureCalcule = coutureEnc;
            }

            // Dépenses validées
            decimal totalDepenses = context.Depenses
                .Where(d => !d.EstAnnulee &&
                            d.StatutValidation == "Validee" &&
                            d.DateDepense.Date >= debut.Date &&
                            d.DateDepense.Date <= fin.Date)
                .AsEnumerable()
                .Sum(d => d.Montant);

            // Salaire secrétaire depuis Paramètres (proratisé)
            decimal salaireSecretaire = 0m;
            var paramSalaire = context.Parametres.Find("SalaireMensuelSecretaire");
            if (paramSalaire != null)
                decimal.TryParse(paramSalaire.Valeur, out salaireSecretaire);

            double nbJoursPeriode = (fin.Date - debut.Date).TotalDays + 1;
            double nbJoursMois    = DateTime.DaysInMonth(debut.Year, debut.Month);
            salaireSecretaire = Math.Round(
                salaireSecretaire * (decimal)(nbJoursPeriode / nbJoursMois),
                0, MidpointRounding.AwayFromZero);

            return new StatsFinancieres
            {
                CaEncaisse             = caEncaisse,
                TotalCommissions       = totalCommissions,
                TotalMateriaux         = totalMateriaux,
                CaEncaisseCoutureCalc  = caEncaisseCoutureCalcule,
                TotalDepenses          = totalDepenses,
                SalaireSecretaire      = salaireSecretaire,
                MateriauxNonRembourses = materiauxNonRembourses
            };
        }
    }
}
