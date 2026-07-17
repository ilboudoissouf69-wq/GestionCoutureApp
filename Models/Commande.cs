using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Commande
    {
        [Key]
        public int IdCommande { get; set; }

        [Required]
        public int IdClient { get; set; }
        [ForeignKey("IdClient")]
        public Client? Client { get; set; }

        public int? IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        [Required]
        public string TypeVetement { get; set; } = string.Empty;

        public string DescriptionPrecision { get; set; } = string.Empty;

        public string CheminPhoto { get; set; } = string.Empty;

        [Required]
        public DateTime DateDebut { get; set; } = DateTime.Now;

        [Required]
        public DateTime DateFin { get; set; }

        public string Statut { get; set; } = "A faire";

        [Required]
        public double MontantTotal { get; set; }

        public List<Mesure> Mesures { get; set; } = new List<Mesure>();
        public List<Paiement> Paiements { get; set; } = new List<Paiement>();

        [NotMapped]
        public double ResteAPayer => MontantTotal - Paiements.Sum(p => p.MontantPaye);

        public TimeSpan HeureDebut { get; set; }
        public TimeSpan? HeureFin { get; set; }
    }
}
