using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    public class Employe
    {
        [Key]
        public int IdEmploye { get; set; }
        public string Nom { get; set; } = string.Empty;
        public string Prenom { get; set; } = string.Empty;
        public string Identifiant { get; set; } = string.Empty;
        public string MotDePasse { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string Statut { get; set; } = "Actif";
    }
}