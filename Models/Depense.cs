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
    }
}