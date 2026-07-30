using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Mesure
    {
        [Key]
        public int IdMesure { get; set; }

        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        // NOUVEAU Étape 1a (Point 1 — Commandes multi-pièces) : dans le futur
        // modèle, une mesure appartient à une PIÈCE, pas à toute la commande
        // (une commande de 3 pantalons + 1 chemise n'a pas les mêmes mesures
        // pour chaque pièce). Ce champ est ajouté en NULLABLE et en plus de
        // IdCommande (qui reste requis pour l'instant) : c'est l'Étape 1b qui
        // renseignera systématiquement IdPieceCommande à la création, et
        // l'Étape 1c qui rendra IdCommande obsolète puis le retirera une fois
        // toutes les mesures existantes basculées vers leur pièce.
        public int? IdPieceCommande { get; set; }
        [ForeignKey("IdPieceCommande")]
        public PieceCommande? PieceCommande { get; set; }

        [Required]
        public string NomMesure { get; set; } = string.Empty;
        [Required]
        public string Valeur { get; set; } = string.Empty;
    }
}
