using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    public class Client
    {
        [Key]
        public int IdClient { get; set; }
        public string Nom { get; set; } = string.Empty;
        public string Prenom { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
    }
}