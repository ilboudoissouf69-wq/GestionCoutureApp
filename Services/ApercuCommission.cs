
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Résultat d'aperçu (non enregistré) d'un calcul de commission pour un couturier.
    /// Utilise decimal pour tous les montants (précision financière exacte).
    /// </summary>
    public class ApercuCommission
    {
        public int IdEmploye { get; set; }
        public string Nom { get; set; } = string.Empty;
        public int NbCommandes { get; set; }
        public decimal CaTotal { get; set; }       // montant total des commandes concernées
        public decimal CaEncaisse { get; set; }    // montant réellement encaissé
        public decimal BaseCalcul { get; set; }    // = CaTotal ou CaEncaisse selon le mode
        public decimal Commission { get; set; }
        public List<int> IdsCommandes { get; set; } = new();

        // ÉTAPE 1b-i (Point 1) : verrouillage réel désormais par PIÈCE, pas par
        // commande (voir CommissionService). IdsCommandes reste rempli (avec les
        // IdCommande des commandes concernées, potentiellement en double si
        // plusieurs pièces d'une même commande sont couturées par le même
        // couturier) pour ne rien casser dans CommissionsView, qui ne l'affiche
        // qu'à titre indicatif (NbCommandes). C'est IdsPieces qui sert
        // réellement à verrouiller/déverrouiller lors de l'enregistrement.
        public List<int> IdsPieces { get; set; } = new();

        // Qualité — remplis par BtnCalculer_Click dans CommissionsView
        public int NbRetours         { get; set; }
        public double TauxQualite    { get; set; } = 100.0;
        public decimal PrimeQualite  { get; set; }

        // Matériaux — exclus de la commission, affichés séparément ("dont matériaux")
        // NE rentrent JAMAIS dans le calcul de ResteAtelier ou resteAtelierGlobal.
        public decimal TotalMateriaux   { get; set; }

        // CA encaissé COUTURE uniquement (hors matériaux).
        // CORRECTIF BUG 4 : avant ce correctif TotalEncaisse = caEncaisse + totalMateriaux,
        // ce qui gonflait artificiellement le bénéfice atelier résiduel. Désormais
        // TotalEncaisse == CaEncaisse (couture seule) et TotalMateriaux est un champ
        // d'information distinct, jamais additionné ici.
        public decimal TotalEncaisse    { get; set; }

        // Totaux calculés
        public decimal TotalAvecPrime   => Commission + PrimeQualite;
        // Bénéfice couture résiduel atelier = encaissé couture - commission couturier.
        // Les matériaux sont exclus : ils ne font pas partie du bénéfice atelier.
        public decimal ResteAtelier     => TotalEncaisse - Commission;

        // Propriétés d'affichage formatées pour la DataGrid
        public string CaTotalAffiche        => CaTotal.ToString("N0");
        public string CaEncaisseAffiche     => CaEncaisse.ToString("N0");
        public string BaseAffichee          => BaseCalcul.ToString("N0");
        public string CommissionAffichee    => Commission.ToString("N0");
        public string TauxQualiteAffiche    => TauxQualite.ToString("0.#") + "%";
        public string TotalMateriauxAffiche => TotalMateriaux.ToString("N0");
        public string TotalEncaisseAffiche  => TotalEncaisse.ToString("N0");
        public string ResteAtelierAffiche   => ResteAtelier.ToString("N0");
        public string PrimeQualiteAffiche   => PrimeQualite > 0
            ? "+" + PrimeQualite.ToString("N0") : "—";
        public string TotalAffiche          => TotalAvecPrime.ToString("N0");
    }

    public interface ICommissionService
    {
        /// <summary>
        /// Calcule un APERÇU (rien n'est enregistré) des commissions par couturier.
        /// Seules les commandes terminées/livrées et pas encore rattachées à une
        /// commission enregistrée sont prises en compte.
        /// </summary>
        List<ApercuCommission> CalculerApercu(
            DateTime dateDebut, DateTime dateFin, decimal pourcentage,
            bool surMontantEncaisse, int? idCouturierFiltre);

        /// <summary>
        /// Enregistre définitivement les commissions calculées et verrouille les
        /// commandes concernées pour qu'elles ne soient plus jamais comptées deux fois.
        /// </summary>
        void EnregistrerCommissions(
            List<ApercuCommission> apercu, DateTime dateDebut, DateTime dateFin,
            decimal pourcentage, bool surMontantEncaisse, int idOperateur, string nomOperateur);

        List<Commission> ObtenirHistorique();

        /// <summary>Annule une commission déjà enregistrée et déverrouille les commandes.</summary>
        void Annuler(int idCommission, string motif, int idAnnulateur, string nomAnnulateur);
    }
}
