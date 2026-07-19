using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class TypeVetement
    {
        [Key]
        public int IdTypeVetement { get; set; }

        [Required]
        [MaxLength(100)]
        public string Nom { get; set; } = string.Empty;

        // decimal : type exact pour l'argent
        [Column(TypeName = "TEXT")]
        public decimal PrixBase { get; set; }

        public List<MesureRequise> MesuresRequises { get; set; } = new();
        public List<DescriptionCourante> Descriptions { get; set; } = new();

        [NotMapped]
        public int NbMesures => MesuresRequises?.Count ?? 0;

        [NotMapped]
        public string DisplayText => $"{Nom}  —  {PrixBase:N0} FCFA";
    }
}
