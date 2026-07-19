using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    public class Employe
    {
        [Key]
        public int IdEmploye { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom est obligatoire.")]
        [MaxLength(100)]
        public string Prenom { get; set; } = string.Empty;

        [Required(ErrorMessage = "L'identifiant est obligatoire.")]
        [MaxLength(50)]
        [RegularExpression(@"^[a-zA-Z0-9_\-\.]{3,50}$",
            ErrorMessage = "L'identifiant doit contenir entre 3 et 50 caractères alphanumériques.")]
        public string Identifiant { get; set; } = string.Empty;

        [Required]
        public string MotDePasse { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Role { get; set; } = string.Empty;

        [MaxLength(20)]
        public string Statut { get; set; } = "Actif";

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string NomComplet => $"{Prenom} {Nom}";
    }
}
