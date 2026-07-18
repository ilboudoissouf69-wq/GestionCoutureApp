using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Enregistrement PERSISTE d'un calcul de commission pour un couturier
    /// sur une periode donnee. Suit la meme philosophie que Paiement :
    /// jamais supprime, seulement annule avec motif, tracabilite complete.
    ///
    /// Les commandes incluses dans une commission sont "verrouillees"
    /// (voir Commande.IdCommission) pour ne jamais etre comptees deux fois.
    /// </summary>
    public class Commission
    {
        [Key]
        public int IdCommission { get; set; }

        [Required]
        public int IdEmploye { get; set; }
        [ForeignKey("IdEmploye")]
        public Employe? Employe { get; set; }

        // Snapshot du nom au moment du calcul (si l'employe est renomme/supprime plus tard)
        public string NomEmployeSnapshot { get; set; } = string.Empty;

        public DateTime DateDebutPeriode { get; set; }
        public DateTime DateFinPeriode { get; set; }

        // "Encaisse"  = calculee sur les paiements reellement recus (recommande)
        // "Total"     = calculee sur le montant total des commandes (ancien comportement)
        public string BaseCalcul { get; set; } = "Encaisse";

        public double Pourcentage { get; set; }

        // Montant sur lequel le pourcentage a ete applique (CA encaisse ou CA total, selon BaseCalcul)
        public double BaseMontant { get; set; }

        public double MontantCommission { get; set; }

        public int NbCommandes { get; set; }

        public DateTime DateCalcul { get; set; } = DateTime.Now;

        // Tracabilite : qui a genere/valide ce calcul
        public int IdOperateur { get; set; }
        public string NomOperateur { get; set; } = string.Empty;

        // ========== INTEGRITE : Annulation (jamais de suppression) ==========
        public bool EstAnnulee { get; set; } = false;
        public string? MotifAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        public List<Commande> Commandes { get; set; } = new List<Commande>();

        [NotMapped]
        public string StatutAffichage => EstAnnulee ? "ANNULEE" : "Validee";
    }
}
