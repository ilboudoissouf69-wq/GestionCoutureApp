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

        // ── Formules financières ──────────────────────────────────────────────
        //
        // Les matériaux sont une AVANCE remboursée par le client.
        // Exemple : couture 2 000 + tissu 3 000 → client paie 5 000.
        //   CA encaissé = 5 000
        //   Dont matériaux = 3 000  (récupérés, flux neutre pour l'atelier)
        //   Marge brute    = 5 000 − 3 000 − commissions  (ce qui reste à l'atelier)
        //   Bénéfice net   = Marge brute − charges exploitation
        //
        // CaEncaisseCouture = part couture pure (CA − matériaux)
        public decimal CaEncaisseCouture =>
            Math.Max(0m, CaEncaisse - TotalMateriaux);

        // Charges exploitation = Dépenses validées + Salaire Secrétaire proraté
        public decimal ChargesExploitation => TotalDepenses + SalaireSecretaire;

        // Marge Brute = CA encaissé − matériaux récupérés − commissions couturiers
        // → ce que l'atelier garde avant les charges fixes
        public decimal MargeBrute =>
            CaEncaisse - TotalMateriaux - TotalCommissions;

        // Bénéfice Net = Marge Brute − charges d'exploitation
        // → résultat final après toutes les charges
        public decimal BeneficeNet => MargeBrute - ChargesExploitation;

        // Alias pour compatibilité avec d'autres vues (TresorerieService, etc.)
        public decimal BeneficeCouture => BeneficeNet;

        // Indicateur matériaux non remboursés (commandes avec matériaux et solde non nul)
        public decimal MateriauxNonRembourses { get; set; }
    }
}
