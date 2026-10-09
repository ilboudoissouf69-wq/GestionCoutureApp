using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Client
    {
        [Key]
        public int IdClient { get; set; }

        [Required(ErrorMessage = "Le nom est obligatoire.")]
        [MaxLength(100, ErrorMessage = "Le nom ne peut pas dépasser 100 caractères.")]
        public string Nom { get; set; } = string.Empty;

        [Required(ErrorMessage = "Le prénom est obligatoire.")]
        [MaxLength(100, ErrorMessage = "Le prénom ne peut pas dépasser 100 caractères.")]
        public string Prenom { get; set; } = string.Empty;

        [MaxLength(20, ErrorMessage = "Le téléphone ne peut pas dépasser 20 caractères.")]
        [RegularExpression(@"^[\d\s\+\-\(\)]{7,20}$",
            ErrorMessage = "Numéro de téléphone invalide (7 à 20 chiffres/espaces/+/- autorisés).")]
        public string Telephone { get; set; } = string.Empty;

        public List<Commande> Commandes { get; set; } = new();

        // ── Propriétés [NotMapped] pour la vue Clients (aucune migration) ──

        /// <summary>Nom complet « Prénom Nom » pour les colonnes et la fiche.</summary>
        [NotMapped]
        public string NomComplet => $"{Prenom} {Nom}".Trim();

        /// <summary>Nombre de commandes non supprimées — rempli après chargement de page.</summary>
        [NotMapped]
        public int NbCommandes { get; set; }

        /// <summary>Date de la dernière commande non supprimée — remplie après chargement de page.</summary>
        [NotMapped]
        public DateTime? DerniereCommande { get; set; }

        /// <summary>
        /// Sous-titre affiché sous le NomComplet dans le DataGrid :
        /// « · dernière commande le dd/MM/yyyy » ou « · aucune commande ».
        /// </summary>
        [NotMapped]
        public string SousTitreDerniereCommande =>
            DerniereCommande.HasValue
                ? $"· dernière commande le {DerniereCommande.Value:dd/MM/yyyy}"
                : "· aucune commande";
    }
}
