using System;
using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Brouillon de commande — sauvegarde automatique du formulaire de création.
    /// Un seul brouillon par utilisateur (l'enregistrement remplace le précédent).
    /// </summary>
    public class BrouillonCommande
    {
        [Key]
        public int IdBrouillon { get; set; }

        /// <summary>
        /// ID de l'employé propriétaire du brouillon (clé unique).
        /// </summary>
        public int IdEmploye { get; set; }

        /// <summary>
        /// Contenu JSON du brouillon (client, pièces, mesures, prix, couturier, dates, notes, matériaux).
        /// Les photos ne sont PAS stockées en base64 : seul le chemin est conservé.
        /// </summary>
        public string ContenuJson { get; set; } = string.Empty;

        /// <summary>
        /// Date de la dernière mise à jour du brouillon.
        /// </summary>
        public DateTime DateMiseAJour { get; set; }
    }
}
