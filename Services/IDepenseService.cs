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
        public decimal TotalMateriaux      { get; set; }  // Total matériaux facturés — INFORMATIF
        public decimal TotalDepenses       { get; set; }  // Dépenses exploitation (loyer, élec, etc.)
        public decimal SalaireSecretaire   { get; set; }  // Depuis Paramètres (proratisé)

        /// <summary>
        /// CA couture encaissé calculé par la méthode centrale (règle "matériaux EN PREMIER").
        /// Injecté par DepenseService.ObtenirStats via TresorerieService.CalculerRepartitionCa.
        /// </summary>
        public decimal CaEncaisseCoutureCalc { get; set; }

        // ── Formules financières ──────────────────────────────────────────────
        //
        // RÈGLE MÉTIER : matériaux remboursés EN PREMIER
        //
        //   Exemple 1 : couture 1 000 + galon 1 000, client paie 2 000
        //     CaEncaisse = 2 000
        //     CaEncaisseCouture = max(0, 2 000 − 1 000) = 1 000  ← couture pure
        //     BeneficeNet = 2 000 − 0 − 0 = 2 000  (matériaux flux neutre)
        //
        //   Exemple 2 : couture 2 000 + mat 1 000, acompte 1 500
        //     CaEncaisse = 1 500
        //     matériaux encaissés = min(1 500, 1 000) = 1 000
        //     CaEncaisseCouture = max(0, 1 500 − 1 000) = 500
        //     BeneficeNet = 1 500 − 0 − 0 = 1 500  (matériaux flux neutre)
        //
        //   Exemple 3 : commande sans paiement
        //     CaEncaisse = 0, CaEncaisseCouture = 0, BeneficeNet = 0

        /// <summary>
        /// CA couture encaissé (règle "matériaux EN PREMIER") — informatif.
        /// Utilise CaEncaisseCoutureCalc si calculé par la méthode centrale,
        /// sinon max(0, CA − matériaux) comme approximation.
        /// </summary>
        public decimal CaEncaisseCouture =>
            CaEncaisseCoutureCalc > 0m || CaEncaisse == 0m
                ? CaEncaisseCoutureCalc
                : Math.Max(0m, CaEncaisse - TotalMateriaux);

        // Charges exploitation = Dépenses validées + Salaire Secrétaire proraté
        public decimal ChargesExploitation => TotalDepenses + SalaireSecretaire;

        // Marge Brute = CA encaissé − commissions couturiers (matériaux flux neutre)
        public decimal MargeBrute => CaEncaisse - TotalCommissions;

        // ── FORMULE UNIQUE DU BÉNÉFICE ────────────────────────────────────────
        //
        //   BeneficeNet = CA encaissé − commissions − charges d'exploitation
        //
        // Les matériaux NE sont PAS déduits : avance remboursée = flux neutre.
        // IDENTIQUE au BilanNet de TresorerieService.CalculerBilan.
        public decimal BeneficeNet => CaEncaisse - TotalCommissions - ChargesExploitation;

        // Alias pour compatibilité avec d'autres vues
        public decimal BeneficeCouture => BeneficeNet;

        // Indicateur matériaux non remboursés (commandes avec matériaux et solde non nul)
        public decimal MateriauxNonRembourses { get; set; }
    }
}
