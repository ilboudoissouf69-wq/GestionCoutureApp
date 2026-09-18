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
        public decimal CaEncaisse          { get; set; }  // Total paiements clients
        public decimal TotalCommissions    { get; set; }  // Commissions couturiers
        public decimal TotalMateriaux      { get; set; }  // Matériaux achetés
        public decimal TotalDepenses       { get; set; }  // Dépenses exploitation (validées)
        public decimal SalaireSecretaire   { get; set; }  // Depuis Paramètres

        // Marge Brute = CA - Commissions - Matériaux
        public decimal MargeBrute => CaEncaisse - TotalCommissions - TotalMateriaux;

        // Charges exploitation = Dépenses + Salaire Secrétaire
        public decimal ChargesExploitation => TotalDepenses + SalaireSecretaire;

        // Bénéfice Net = Marge Brute - Charges Exploitation
        public decimal BeneficeNet => MargeBrute - ChargesExploitation;
    }
}
