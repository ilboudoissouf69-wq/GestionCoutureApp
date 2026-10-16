using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Étape 7 — Suivi d'une alerte de retard déjà traitée (client prévenu).
    /// Une ligne par cible (commande, pièce à RDV exceptionnel, ou retour).
    /// Anti-doublon : tant que DateRdvTraite correspond encore à l'échéance actuelle,
    /// l'alerte ne se redéclenche pas. Après un report, l'échéance change et l'alerte
    /// peut revenir.
    /// </summary>
    public class SuiviAlerteRetard
    {
        [Key]
        public int IdSuivi { get; set; }

        /// <summary>"Commande", "Piece" (RDV exception) ou "Retour".</summary>
        [Required]
        [MaxLength(20)]
        public string TypeCible { get; set; } = "Commande";

        public int IdCible { get; set; }

        /// <summary>Client concerné (stats Dashboard « clients prévenus »).</summary>
        public int IdClient { get; set; }

        /// <summary>Échéance pour laquelle le client a été prévenu (à la minute).</summary>
        public DateTime DateRdvTraite { get; set; }

        public int NbPreventions { get; set; }

        public int NbReports { get; set; }

        public DateTime? DateDernierMessage { get; set; }

        public int? IdOperateurDernierMessage { get; set; }

        [MaxLength(200)]
        public string? NomOperateurDernierMessage { get; set; }

        public DateTime? AncienneDateDernierReport { get; set; }

        public DateTime? NouvelleDateDernierReport { get; set; }

        public DateTime? DateDernierReport { get; set; }

        [NotMapped]
        public string CompteurAffiche =>
            NbPreventions <= 0 ? string.Empty : $"Client prévenu {NbPreventions} fois";
    }
}
