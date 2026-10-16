using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Compteur annuel pour la numérotation des retours.
    /// Format : RET-AAAA-00001 (compteur remis à 1 chaque 1er janvier).
    /// Une ligne par année. Jamais supprimée.
    /// </summary>
    public class RetourCompteur
    {
        [Key]
        public int Annee { get; set; }

        /// <summary>Dernier numéro attribué pour l'année. 0 = aucun encore attribué.</summary>
        public int DernierNumero { get; set; } = 0;
    }
}
