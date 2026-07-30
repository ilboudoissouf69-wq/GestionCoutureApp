using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Réglage générique de l'application (Point 8 — écran Paramètres).
    /// Table clé-valeur : évite une migration de base à chaque nouveau
    /// réglage ajouté (délai alerte, fréquence sync, salaire secrétaire,
    /// et ceux à venir : % commission par défaut, seuil d'alerte...).
    /// </summary>
    public class Parametre
    {
        [Key]
        [MaxLength(100)]
        public string Cle { get; set; } = string.Empty;

        [Required]
        public string Valeur { get; set; } = string.Empty;
    }
}