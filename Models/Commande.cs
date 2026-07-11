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
        public Client Client { get; set; }

        public int? IdCouturier { get; set; }

        [ForeignKey("IdCouturier")]
        public Employe Couturier { get; set; }

        // --- NOUVEAUTÉ ICI ---
        [Required]
        public string TypeVetement { get; set; } // "Pantalon", "Chemise", "Robe", etc.

        // On stocke toutes les mesures spécifiques sous forme de texte structuré (ex: "Longueur:102, Taille:84")
        // Cela permet de sauvegarder n'importe quelle mesure sans bloquer la base de données !
        public string MesuresDetails { get; set; }

        public string DescriptionPrecision { get; set; }
        public string CheminPhoto { get; set; }

        [Required]
        public DateTime DateDebut { get; set; } = DateTime.Now;

        [Required]
        public DateTime DateFin { get; set; }

        public string Statut { get; set; } = "A faire";

        [Required]
        public double MontantTotal { get; set; }

        public double AvancePayee { get; set; } = 0.0;

        public double ResteAPayer => MontantTotal - AvancePayee;
    }
}