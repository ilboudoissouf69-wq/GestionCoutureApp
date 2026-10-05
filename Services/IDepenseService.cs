using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IDepenseService
    {
        List<Depense> ObtenirTous();
        List<Depense> Filtrer(DateTime debut, DateTime fin,
                              string? categorie = null,
                              string? statutValidation = null);
        void Ajouter(Depense depense);
        void Valider(int idDepense, string nomBoss);
        void Annuler(int idDepense, string motif, string nomAnnulateur);

        decimal TotalParPeriode(DateTime debut, DateTime fin);

        /// <summary>Statistiques financières pour les 4 cartes KPI.</summary>
        StatsFinancieres ObtenirStats(DateTime debut, DateTime fin);
    }

    public class StatsFinancieres
    {
        public decimal CaEncaisse          { get; set; }  // Total paiements clients (couture + matériaux)
        public decimal TotalCommissions    { get; set; }  // Commissions couturiers
        public decimal TotalMateriaux      { get; set; }  // Matériaux : INFORMATIF uniquement (flux trésorerie séparé)
        public decimal TotalDepenses       { get; set; }  // Dépenses exploitation (loyer, élec, salaires, etc.)
        public decimal SalaireSecretaire   { get; set; }  // Depuis Paramètres

        // ── TÂCHE 2 : Formule unique du bénéfice ─────────────────────────────
        //
        // Règle métier validée (extrait du cahier des charges) :
        //   Les matériaux (tissu, boutons, galon…) sont une AVANCE remboursée par
        //   le client. L'atelier avance la somme et se la fait rembourser dans le
        //   paiement. Ils NE SONT PAS une charge de l'atelier.
        //   → On ne les déduit JAMAIS du bénéfice.
        //
        // Exemple : couture 1 000 + galon 1 000 → client paie 2 000.
        //   CA encaissé = 2 000.  Matériaux = 1 000 (avance récupérée).
        //   Bénéfice couture = 1 000. Les 1 000 de galon circulent mais ne
        //   restent pas dans la caisse de l'atelier.
        //
        // Formule unifiée (identique à BilanFinancier.BilanNet) :
        //   Bénéfice = CA encaissé − commissions − primes − dépenses réelles
        //
        // CaEncaisseCouture = CA encaissé − matériaux (la part couture pure)
        // C'est la base cohérente avec CommissionService (même calcul prorata).
        public decimal CaEncaisseCouture =>
            Math.Max(0m, CaEncaisse - TotalMateriaux);

        // Bénéfice = CA couture encaissé − commissions − primes − charges
        // = BilanFinancier.BilanNet quand les périodes et les filtres sont identiques.
        public decimal BeneficeCouture =>
            CaEncaisseCouture - TotalCommissions - ChargesExploitation;

        // Charges exploitation = Dépenses validées + Salaire Secrétaire
        public decimal ChargesExploitation => TotalDepenses + SalaireSecretaire;

        // Bénéfice global = CA total encaissé − commissions − charges
        // (les matériaux s'annulent : avancés puis remboursés par le client)
        public decimal BeneficeNet => CaEncaisse - TotalCommissions - ChargesExploitation;

        // ── Rétrocompatibilité (lectures encore présentes dans DepensesView) ──
        // MargeBrute était l'ancien nom avant la correction Tâche 2.
        // Maintenant alignée sur la vraie formule : CA − commissions − charges.
        [Obsolete("Tâche 2 : utiliser BeneficeNet à la place.")]
        public decimal MargeBrute => BeneficeNet;

        // Indicateur matériaux non remboursés (commandes avec matériaux et solde non nul)
        public decimal MateriauxNonRembourses { get; set; }
    }
}
