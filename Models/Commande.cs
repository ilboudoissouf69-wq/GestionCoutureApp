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

        // Correctif : seuls les paiements NON annules comptent pour le reste a payer.
        // (avant : les paiements annules etaient quand meme soustraits, ce qui
        // affichait un reste plus faible que la realite)
        [NotMapped]
        public double ResteAPayer => MontantTotal - Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

        // Total reellement encaisse (paiements valides uniquement) — utilise
        // notamment pour le calcul des commissions sur base "encaissee".
        [NotMapped]
        public double MontantEncaisse => Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

        public TimeSpan HeureDebut { get; set; }
        public TimeSpan? HeureFin { get; set; }

        // ========== Verrouillage commission ==========
        // Une fois une commande incluse dans un calcul de commission ENREGISTRE,
        // elle est "verrouillee" ici pour ne plus jamais etre comptee deux fois
        // (voir Models/Commission.cs et Services/CommissionService.cs).
        public int? IdCommission { get; set; }
        [ForeignKey("IdCommission")]
        public Commission? Commission { get; set; }
    }
}
