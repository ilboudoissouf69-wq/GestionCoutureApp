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
            DateTime debutInclus = debut.Date;
            DateTime finExclusive = fin.Date.AddDays(1);
            var q = context.Depenses
                .AsNoTracking()
                .Where(d => d.DateDepense >= debutInclus &&
                            d.DateDepense < finExclusive);

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

        public DepensePageResult FiltrerPage(DateTime debut, DateTime fin, int page, int taillePage,
                                               string? categorie = null,
                                               string? statutValidation = null)
        {
            using var context = _contextFactory.CreateDbContext();
            DateTime debutInclus = debut.Date;
            DateTime finExclusive = fin.Date.AddDays(1);
            var q = context.Depenses
                .AsNoTracking()
                .Where(d => d.DateDepense >= debutInclus && d.DateDepense < finExclusive);

            if (!string.IsNullOrEmpty(categorie) && categorie != "Toutes")
                q = q.Where(d => d.Categorie == categorie);

            if (!string.IsNullOrEmpty(statutValidation) && statutValidation != "Tous")
            {
                q = statutValidation == "Annulée"
                    ? q.Where(d => d.EstAnnulee)
                    : q.Where(d => !d.EstAnnulee && d.StatutValidation == statutValidation);
            }

            int taille = Math.Clamp(taillePage, 1, 100);
            int pageSecurisee = Math.Max(page, 1);
            int total = q.Count();
            var depenses = q.OrderByDescending(d => d.DateDepense)
                .ThenByDescending(d => d.IdDepense)
                .Skip((pageSecurisee - 1) * taille)
                .Take(taille)
                .ToList();

            return new DepensePageResult { Depenses = depenses, Total = total };
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

        public void Valider(int idDepense, int idBoss)
        {
            using var context = _contextFactory.CreateDbContext();

            // Phase 1.4 : identification par ID (pas par nom, résistant aux homonymes)
            // RequireRoleByIdEnum charge l'employé et vérifie Actif + rôle Boss.
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(_contextFactory, idBoss, RoleEmploye.Boss);

            // Relire le nom réel en base pour l'audit (ne pas faire confiance au paramètre)
            var boss = context.Employes.Find(idBoss)
                ?? throw new InvalidOperationException("Boss introuvable en base.");
            string nomBossRelu = boss.Prenom + " " + boss.Nom;

            var dep = context.Depenses.Find(idDepense)
                ?? throw new InvalidOperationException("Dépense introuvable.");
            if (dep.EstAnnulee)
                throw new InvalidOperationException("Impossible de valider une dépense annulée.");
            dep.StatutValidation = "Validee";
            context.SaveChanges();
            _eventAggregator?.Publish(new DomainChangedEvent { Type = DomainChangedType.Depense });
        }

        public void Annuler(int idDepense, string motif, int idAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            // Phase 1.4 : identification par ID (pas par nom, résistant aux homonymes)
            Helpers.AuthorizationHelper.RequireRoleByIdEnum(_contextFactory, idAnnulateur, RoleEmploye.Boss);

            // Relire le nom réel en base
            var annulateur = context.Employes.Find(idAnnulateur)
                ?? throw new InvalidOperationException("Annulateur introuvable en base.");
            string nomAnnulateurRelu = annulateur.Prenom + " " + annulateur.Nom;

            var depense = context.Depenses.Find(idDepense)
                ?? throw new InvalidOperationException("Dépense introuvable.");
            if (depense.EstAnnulee)
                throw new InvalidOperationException("Cette dépense est déjà annulée.");

            depense.EstAnnulee = true;
            depense.MotifAnnulation = motif.Trim();
            depense.DateAnnulation = DateTime.Now;
            depense.NomAnnulateur = nomAnnulateurRelu;
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
            DateTime debutInclus = debut.Date;
            DateTime finExclusive = fin.Date.AddDays(1);

            // CA encaissé (paiements non annulés)
            // Agréger directement dans SQLite. Charger chaque paiement avec
            // AsEnumerable() faisait exploser le travail mémoire pour une année
            // complète (les bases de l'atelier contiennent beaucoup de lignes).
            decimal caEncaisse = (decimal)context.Paiements
                .AsNoTracking()
                .Where(p => !p.EstAnnule &&
                            p.DatePaiement >= debutInclus &&
                            p.DatePaiement < finExclusive)
                .Sum(p => (double)p.MontantPaye);

            // Commissions validées (couturiers + primes)
            decimal totalCommissions = (decimal)context.Commissions
                .AsNoTracking()
                .Where(c => !c.EstAnnulee &&
                            c.DateDebutPeriode >= debutInclus &&
                            c.DateDebutPeriode < finExclusive)
                .Sum(c => (double)(c.MontantCommission + c.PrimeQualite));

            DateTime? derniereCommissionHorsPeriode = context.Commissions
                .AsNoTracking()
                .Where(c => !c.EstAnnulee &&
                            (c.DateDebutPeriode < debutInclus || c.DateDebutPeriode >= finExclusive))
                .Max(c => (DateTime?)c.DateDebutPeriode);

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
            decimal totalDepenses = (decimal)context.Depenses
                .AsNoTracking()
                .Where(d => !d.EstAnnulee &&
                            d.StatutValidation == "Validee" &&
                            d.DateDepense >= debutInclus &&
                            d.DateDepense < finExclusive)
                .Sum(d => (double)d.Montant);

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
                DerniereCommissionHorsPeriode = derniereCommissionHorsPeriode,
                TotalMateriaux         = totalMateriaux,
                CaEncaisseCoutureCalc  = caEncaisseCoutureCalcule,
                TotalDepenses          = totalDepenses,
                SalaireSecretaire      = salaireSecretaire,
                MateriauxNonRembourses = materiauxNonRembourses
            };
        }
    }
}
