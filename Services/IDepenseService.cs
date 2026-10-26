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
        /// <summary>Valider une dépense. idBoss doit être l'ID d'un employé Boss actif.</summary>
        void Valider(int idDepense, int idBoss);
        /// <summary>Annuler une dépense. idAnnulateur doit être l'ID d'un employé Boss actif.</summary>
        void Annuler(int idDepense, string motif, int idAnnulateur);

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
        // RÈGLE MÉTIER (validée par le Boss) :
        //   Les matériaux sont une avance remboursée par le client.
        //   L'achat n'est PAS saisi comme dépense ; le remboursement n'est PAS un gain.
        //   → Seule la part couture encaissée entre dans le bénéfice.
        //
        //   Règle "matériaux remboursés EN PREMIER" :
        //     Matériaux encaissés (par commande) = min( paye, totalMat )
        //     CaEncaisseCouture  (par commande)  = max( 0, paye − totalMat )
        //
        //   Exemples chiffrés :
        //     1) couture 1 000 + galon 1 000, client paie 2 000
        //          CaEncaisse = 2 000
        //          matériaux enc = min(2 000, 1 000) = 1 000
        //          CaEncaisseCouture = max(0, 2 000 − 1 000) = 1 000
        //          BeneficeNet = 1 000 − 0 − 0 = 1 000  ✓
        //
        //     2) couture 2 000 + matériaux 1 000, acompte 1 500
        //          CaEncaisse = 1 500
        //          matériaux enc = min(1 500, 1 000) = 1 000
        //          CaEncaisseCouture = max(0, 1 500 − 1 000) = 500
        //          BeneficeNet = 500 − 0 − 0 = 500  ✓
        //
        //     3) commande sans paiement
        //          CaEncaisse = 0, CaEncaisseCouture = 0, BeneficeNet = 0  ✓

        /// <summary>
        /// CA couture encaissé (règle "matériaux remboursés EN PREMIER").
        /// Calculé par TresorerieService.CalculerRepartitionCa (méthode centrale).
        /// C'est la BASE du bénéfice net.
        /// </summary>
        public decimal CaEncaisseCouture =>
            CaEncaisseCoutureCalc > 0m || CaEncaisse == 0m
                ? CaEncaisseCoutureCalc
                : Math.Max(0m, CaEncaisse - TotalMateriaux);

        // Charges exploitation = Dépenses validées + Salaire Secrétaire proraté
        public decimal ChargesExploitation => TotalDepenses + SalaireSecretaire;

        // Marge Brute couture = CA couture − commissions
        public decimal MargeBrute => CaEncaisseCouture - TotalCommissions;

        // ── FORMULE UNIQUE DU BÉNÉFICE ────────────────────────────────────────
        //
        //   BeneficeNet = CaEncaisseCouture − commissions − charges d'exploitation
        //
        // Les matériaux refacturés (argent de passage) ne gonfient PAS le bénéfice.
        // IDENTIQUE au BilanNet de TresorerieService.CalculerBilan.
        public decimal BeneficeNet => CaEncaisseCouture - TotalCommissions - ChargesExploitation;

        // Alias pour compatibilité avec d'autres vues
        public decimal BeneficeCouture => BeneficeNet;

        // Indicateur matériaux non remboursés (commandes avec matériaux et solde non nul)
        public decimal MateriauxNonRembourses { get; set; }
    }
}
