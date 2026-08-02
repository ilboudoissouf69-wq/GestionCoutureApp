using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Depense
    {
        [Key]
        public int IdDepense { get; set; }

        [Required(ErrorMessage = "Le type de depense est obligatoire.")]
        [MaxLength(50)]
        public string TypeDepense { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le montant est obligatoire.")]
        [Column(TypeName = "TEXT")]
        public decimal Montant { get; set; }

        [Required]
        public DateTime DateDepense { get; set; } = DateTime.Now;

        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        // Tracabilite : qui a enregistre la depense
        public string NomOperateur { get; set; } = string.Empty;

        // CORRECTIF (audit — Décision 3.1 actée mais non implémentée) :
        // une dépense enregistrée ne doit jamais disparaître physiquement de
        // la base — exactement le même mécanisme que Paiement et Commission.
        // Une dépense annulée reste dans l'historique mais sort des totaux
        // du tableau de bord (voir DepenseService.TotalParPeriode).
        public bool EstAnnulee { get; set; } = false;
        public string? MotifAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        [NotMapped]
        public string StatutAffiche => EstAnnulee ? "ANNULÉE" : "Validée";
    }
}
