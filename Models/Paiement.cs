using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Paiement
    {
        [Key]
        public int IdPaiement { get; set; }

        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        // decimal : type exact pour l'argent
        [Required]
        [Column(TypeName = "TEXT")]
        public decimal MontantPaye { get; set; }

        public DateTime DatePaiement { get; set; } = DateTime.Now;

        public string ModePaiement { get; set; } = "Especes";

        // Numéro de reçu unique et traçable (ex: REC-20260713-0001)
        public string RecuNumero { get; set; } = string.Empty;

        // Traçabilité : opérateur qui a enregistré le paiement
        public int? IdOperateur { get; set; }
        public string NomOperateur { get; set; } = string.Empty;

        // Annulation : jamais de suppression, seulement annulation avec motif
        public bool EstAnnule { get; set; } = false;
        public string? MotifsAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        // Snapshots financiers au moment de l'enregistrement
        [Column(TypeName = "TEXT")]
        public decimal MontantTotalCommande { get; set; }
        [Column(TypeName = "TEXT")]
        public decimal ResteAvantPaiement { get; set; }

        [NotMapped]
        public string StatutAffichage => EstAnnule ? "ANNULE" : "Valide";

        [NotMapped]
        public string AffichageHistorique =>
            $"{DatePaiement:dd/MM/yyyy HH:mm}  |  {MontantPaye:N0} FCFA  |  {ModePaiement}  |  {NomOperateur}" +
            (EstAnnule ? "  [ANNULE]" : "");
    }
}
