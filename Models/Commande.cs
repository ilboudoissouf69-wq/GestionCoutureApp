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

        // decimal : type exact pour l'argent (pas de dérive binaire comme double)
        [Required]
        [Column(TypeName = "TEXT")]
        public decimal MontantTotal { get; set; }

        public List<Mesure> Mesures { get; set; } = new();
        public List<Paiement> Paiements { get; set; } = new();

        // Seuls les paiements NON annulés comptent pour le reste à payer
        [NotMapped]
        public decimal ResteAPayer =>
            MontantTotal - Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

        // Total réellement encaissé (paiements valides) — base pour les commissions
        [NotMapped]
        public decimal MontantEncaisse =>
            Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

        public TimeSpan HeureDebut { get; set; }
        public TimeSpan? HeureFin { get; set; }

        // Verrouillage commission : null = eligible à un calcul, sinon déjà incluse
        public int? IdCommission { get; set; }
        [ForeignKey("IdCommission")]
        public Commission? Commission { get; set; }
    }
}
