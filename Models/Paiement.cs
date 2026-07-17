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

        [Required]
        public double MontantPaye { get; set; }

        public DateTime DatePaiement { get; set; } = DateTime.Now;

        public string ModePaiement { get; set; } = "Especes";

        // Numero de recu unique et tracable (ex: REC-20260713-0001)
        public string RecuNumero { get; set; } = string.Empty;

        // ========== INTEGRITE : Operateur ==========
        // ID et nom de l'employe qui a enregistre le paiement
        public int? IdOperateur { get; set; }
        public string NomOperateur { get; set; } = string.Empty;

        // ========== INTEGRITE : Annulation ==========
        // Un paiement ne peut pas etre supprime, seulement annule avec motif
        public bool EstAnnule { get; set; } = false;
        public string? MotifsAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        // ========== INTEGRITE : Solde au moment du paiement ==========
        // Snapshot du montant total et reste au moment de l'enregistrement
        public double MontantTotalCommande { get; set; }
        public double ResteAvantPaiement { get; set; }

        [NotMapped]
        public string StatutAffichage => EstAnnule ? "ANNULE" : "Valide";

        [NotMapped]
        public string AffichageHistorique =>
            $"{DatePaiement:dd/MM/yyyy HH:mm}  |  {MontantPaye:N0} FCFA  |  {ModePaiement}  |  {NomOperateur}" +
            (EstAnnule ? "  [ANNULE]" : "");
    }
}
