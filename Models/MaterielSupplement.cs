using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class MaterielSupplement
    {
        [Key]
        public int IdMateriel { get; set; }

        public int? IdPieceCommande { get; set; }
        [ForeignKey("IdPieceCommande")]
        public PieceCommande? PieceCommande { get; set; }

        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        [Required]
        [MaxLength(150)]
        public string Designation { get; set; } = string.Empty;

        public int Quantite { get; set; } = 1;

        [Column(TypeName = "TEXT")]
        public decimal PrixUnitaire { get; set; }

        [NotMapped]
        public decimal Montant => Quantite * PrixUnitaire;

        [NotMapped]
        public string MontantAffiche => Montant.ToString("N0") + " FCFA";

        [NotMapped]
        public string PrixUnitaireAffiche => PrixUnitaire.ToString("N0") + " FCFA";
    }
}