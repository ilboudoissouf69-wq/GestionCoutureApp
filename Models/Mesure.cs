using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Mesure
    {
        [Key]
        public int IdMesure { get; set; }

        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        [Required]
        public string NomMesure { get; set; } = string.Empty;
        [Required]
        public string Valeur { get; set; } = string.Empty;
    }
}
