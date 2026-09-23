using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Depense
    {
        [Key]
        public int IdDepense { get; set; }

        // ── Catégorie métier (regroupement analytique) ────────────────────
        // Valeurs possibles : "Charges fixes", "Masse salariale",
        //                     "Matériel & Entretien", "Divers"
        [MaxLength(50)]
        public string Categorie { get; set; } = "Divers";

        [Required]
        [MaxLength(80)]
        public string TypeDepense { get; set; } = string.Empty;

        [Required]
        [Column(TypeName = "TEXT")]
        public decimal Montant { get; set; }

        [Required]
        public DateTime DateDepense { get; set; } = DateTime.Now;

        [MaxLength(200)]
        public string Description { get; set; } = string.Empty;

        // ── Traçabilité ───────────────────────────────────────────────────
        // IdOperateur complète NomOperateur (déjà présent) : permet la recherche
        // par identité sans dépendre d'une correspondance de nom (qui peut changer
        // ou être usurpée). Valeur 0 pour les dépenses historiques.
        [Required]
        public int IdOperateur { get; set; }

        // Tracabilité nom (snapshot au moment de la saisie)
        public string NomOperateur { get; set; } = string.Empty;

        // ── Statut de validation (workflow Secrétaire → Boss) ─────────────
        // "En attente" = saisie par la secrétaire, en attente d'approbation Boss
        // "Validee"    = approuvée (ou saisie directement par le Boss)
        public string StatutValidation { get; set; } = "Validee";

        // ── Annulation (jamais de suppression physique) ───────────────────
        public bool EstAnnulee { get; set; } = false;
        public string? MotifAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        // ── Propriétés calculées ──────────────────────────────────────────
        [NotMapped]
        public string StatutAffiche
        {
            get
            {
                if (EstAnnulee) return "ANNULÉE";
                return StatutValidation == "En attente" ? "En attente" : "Validée";
            }
        }

        [NotMapped]
        public string CouleurStatut => EstAnnulee ? "#9CA3AF"
            : StatutValidation == "En attente" ? "#D97706" : "#059669";

        [NotMapped]
        public string DateAffichee => DateDepense.ToString("dd/MM/yyyy");

        [NotMapped]
        public string MontantAffiche => Montant.ToString("N0") + " FCFA";

        // ── Catalogue des catégories et types ────────────────────────────
        public static readonly Dictionary<string, List<string>> CatalogueTypes
            = new()
            {
                ["Charges fixes"] = new()
                {
                    "Loyer", "Électricité", "Eau", "Connexion Internet", "Téléphone"
                },
                ["Masse salariale"] = new()
                {
                    "Salaire Secrétaire", "Avance sur salaire", "Prime exceptionnelle"
                },
                ["Matériel & Entretien"] = new()
                {
                    "Fils & Accessoires", "Fermetures & Boutons", "Aiguilles",
                    "Réparation machine", "Entretien atelier", "Achat fournitures"
                },
                ["Divers"] = new()
                {
                    "Transport", "Restauration", "Faux frais", "Autre"
                }
            };
    }
}
