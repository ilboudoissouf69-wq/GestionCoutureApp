using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    public class Client
    {
        [Key]
        public int IdClient { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom est obligatoire.")]
        [MaxLength(100, ErrorMessage = "Le prénom ne peut pas dépasser 100 caractères.")]
        public string Prenom { get; set; } = string.Empty;

        [MaxLength(20, ErrorMessage = "Le téléphone ne peut pas dépasser 20 caractères.")]
        [RegularExpression(@"^[\d\s\+\-\(\)]{7,20}$",
            ErrorMessage = "Numéro de téléphone invalide (7 à 20 chiffres/espaces/+/- autorisés).")]
        public string Telephone { get; set; } = string.Empty;

        public List<Commande> Commandes { get; set; } = new();
    }
}
