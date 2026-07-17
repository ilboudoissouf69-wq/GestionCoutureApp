// Models/TypeVetement.cs
// Représente un type de vêtement (ex: Pantalon, Chemise)
// avec son prix de base, les mesures requises et les descriptions courantes.
using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    public class TypeVetement
    {
        [Key]
        public int IdTypeVetement { get; set; }
        public string Nom { get; set; } = string.Empty;         // "Pantalon", "Robe"...
        public double PrixBase { get; set; }                    // Prix par défaut en FCFA
        public List<MesureRequise> MesuresRequises { get; set; } = new();
        public List<DescriptionCourante> Descriptions { get; set; } = new();
    }
}