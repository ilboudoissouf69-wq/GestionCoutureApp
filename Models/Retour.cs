using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Point 4 — Retours / Reprises gratuites.
    /// Un retour est rattaché à une PIÈCE précise d'une commande livrée.
    /// Coût = 0 FCFA. Traçabilité du couturier responsable pour statistiques Boss.
    /// </summary>
    public class Retour
    {
        [Key]
        public int IdRetour { get; set; }

        // ── Rattachement commande ──────────────────────────────────────────
        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        // ── Pièce concernée (multi-pièces Point 1) ────────────────────────
        [Required]
        public int IdPieceCommande { get; set; }
        [ForeignKey("IdPieceCommande")]
        public PieceCommande? PieceCommande { get; set; }

        // ── Couturier INITIAL (celui qui a fait la pièce — responsable) ───
        [Required]
        public int IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        // ── Couturier REPRISE (peut être différent ou le même) ────────────
        // Null = même couturier que l'initial
        public int? IdCouturierReprise { get; set; }
        [ForeignKey("IdCouturierReprise")]
        public Employe? CouturierReprise { get; set; }

        // ── Description du problème ───────────────────────────────────────
        [Required]
        [MaxLength(500)]
        public string DescriptionProbleme { get; set; } = string.Empty;

        // ── Photo du défaut (optionnel) ───────────────────────────────────
        public string? CheminPhotoDefaut { get; set; }

        // ── Statut ───────────────────────────────────────────────────────
        // Signale → En reprise → Pret → Rendu
        public string Statut { get; set; } = "Signale";

        // ── Dates ────────────────────────────────────────────────────────
        [Required]
        public DateTime DateSignalement { get; set; } = DateTime.Now;

        // RDV pour récupérer la pièce après reprise
        public DateTime? DateRdvReprise { get; set; }
        public TimeSpan? HeureDebutReprise { get; set; }
        public TimeSpan? HeureFinReprise { get; set; }

        public DateTime? DateResolution { get; set; }

        // ── Traçabilité opérateurs ────────────────────────────────────────
        [Required]
        public int IdOperateurEnregistrement { get; set; }
        public string NomOperateurEnregistrement { get; set; } = string.Empty;

        public int? IdOperateurResolution { get; set; }
        public string? NomOperateurResolution { get; set; }

        // ── Annulation (jamais de suppression physique) ───────────────────
        public bool EstAnnule { get; set; } = false;
        public string? MotifAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        // ── Propriétés calculées ──────────────────────────────────────────
        [NotMapped]
        public string StatutAffiche
        {
            get
            {
                if (EstAnnule) return "Annulé";
                return Statut switch
                {
                    "Signale"    => "Signalé",
                    "En reprise" => "En reprise",
                    "Pret"       => "Prêt ✓",
                    "Rendu"      => "Rendu au client",
                    _            => Statut
                };
            }
        }

        [NotMapped]
        public string DateSignalementAffichee => DateSignalement.ToString("dd/MM/yyyy");

        [NotMapped]
        public string DateRdvAffichee =>
            DateRdvReprise.HasValue
                ? DateRdvReprise.Value.ToString("dd/MM/yy") +
                  (HeureDebutReprise.HasValue
                      ? " à " + HeureDebutReprise.Value.ToString(@"hh\:mm")
                      : "")
                : "—";

        [NotMapped]
        public string CouturierRepriseAffiche =>
            CouturierReprise != null
                ? CouturierReprise.Prenom + " " + CouturierReprise.Nom
                : Couturier != null
                    ? Couturier.Prenom + " " + Couturier.Nom + " (même)"
                    : "—";

        [NotMapped]
        public string ClientAffiche =>
            Commande?.Client != null
                ? Commande.Client.Nom + " " + Commande.Client.Prenom
                : "—";

        [NotMapped]
        public string StatutCouleur => Statut switch
        {
            "Signale"    => "#DC2626",
            "En reprise" => "#D97706",
            "Pret"       => "#059669",
            "Rendu"      => "#6B7280",
            _            => "#374151"
        };
    }
}
