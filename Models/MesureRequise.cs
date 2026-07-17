using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class MesureRequise
    {
        [Key]
        public int IdMesureRequise { get; set; }

        public int IdTypeVetement { get; set; }
        [ForeignKey("IdTypeVetement")]
        public TypeVetement? TypeVetement { get; set; }

        public string NomMesure { get; set; } = string.Empty;
    }
}
