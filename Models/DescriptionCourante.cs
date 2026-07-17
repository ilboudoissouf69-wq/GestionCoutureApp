using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    [Table("DescriptionsCourantes")]
    public class DescriptionCourante
    {
        [Key]
        [Column("IdDescription")]
        public int IdDescription { get; set; }

        [Required]
        [StringLength(200)]
        public string Texte { get; set; } = string.Empty;

        [Column("IdTypeVetement")]
        public int IdTypeVetement { get; set; }

        [ForeignKey("IdTypeVetement")]
        public TypeVetement? TypeVetement { get; set; }
    }
}