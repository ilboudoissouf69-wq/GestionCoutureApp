using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Enregistrement persisté d'un calcul de commission pour un couturier.
    /// Jamais supprimé, seulement annulé avec motif (même philosophie que Paiement).
    /// Les commandes incluses sont verrouillées via Commande.IdCommission.
    /// </summary>
    public class Commission
    {
        [Key]
        public int IdCommission { get; set; }

        [Required]
        public int IdEmploye { get; set; }
        [ForeignKey("IdEmploye")]
        public Employe? Employe { get; set; }

        // Snapshot du nom au moment du calcul
        public string NomEmployeSnapshot { get; set; } = string.Empty;

        public DateTime DateDebutPeriode { get; set; }
        public DateTime DateFinPeriode { get; set; }

        // "Encaisse" = calculée sur paiements réels | "Total" = sur montant total commande
        public string BaseCalcul { get; set; } = "Encaisse";

        // decimal : type exact pour l'argent
        [Column(TypeName = "TEXT")]
        public decimal Pourcentage { get; set; }

        [Column(TypeName = "TEXT")]
        public decimal BaseMontant { get; set; }

        [Column(TypeName = "TEXT")]
        public decimal MontantCommission { get; set; }

        public int NbCommandes { get; set; }

        public DateTime DateCalcul { get; set; } = DateTime.Now;

        public int IdOperateur { get; set; }
        public string NomOperateur { get; set; } = string.Empty;

        // Annulation (jamais de suppression)
        public bool EstAnnulee { get; set; } = false;
        public string? MotifAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        public List<Commande> Commandes { get; set; } = new();

        [NotMapped]
        public string StatutAffichage => EstAnnulee ? "ANNULEE" : "Validee";

        [NotMapped]
        public string PeriodeAffichee =>
            $"{DateDebutPeriode:dd/MM/yyyy} — {DateFinPeriode:dd/MM/yyyy}";

        [NotMapped]
        public string DateCalculAffichee => DateCalcul.ToString("dd/MM/yyyy HH:mm");

        [NotMapped]
        public string MontantAffiche => MontantCommission.ToString("N0");
    }
}
