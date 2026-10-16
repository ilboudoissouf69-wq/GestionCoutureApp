using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Étape 6 — Retours / Reprises gratuites.
    /// Un retour est rattaché à une PIÈCE précise d'une commande livrée.
    /// Coût = 0 FCFA. Traçabilité du couturier responsable pour statistiques Boss.
    /// NumeroRetour : format « RET-AAAA-00001 », compteur annuel avec index UNIQUE.
    /// Statuts : Signale → En reprise → Termine → Rendu.
    /// Compteurs couturiers : imputés (IdCouturier) et reprises (IdCouturierReprise).
    /// </summary>
    public class Retour
    {
        [Key]
        public int IdRetour { get; set; }

        // ── Identifiant unique lisible ─────────────────────────────────────
        // Format : RET-2026-00001. Généré dans une transaction avec index UNIQUE.
        // Jamais null après création. Défaut vide pour la migration des anciens enreg.
        [MaxLength(20)]
        public string NumeroRetour { get; set; } = string.Empty;

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
        // Compteur "Retours imputés" côté service.
        [Required]
        public int IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        // ── Couturier REPRISE (peut être différent ou le même) ────────────
        // Null = même couturier que l'initial.
        // Compteur "Reprises effectuées" côté service.
        public int? IdCouturierReprise { get; set; }
        [ForeignKey("IdCouturierReprise")]
        public Employe? CouturierReprise { get; set; }

        // ── Description du problème ───────────────────────────────────────
        [Required]
        [MaxLength(500)]
        public string DescriptionProbleme { get; set; } = string.Empty;

        // ── Photo du défaut (optionnel) ───────────────────────────────────
        public string? CheminPhotoDefaut { get; set; }

        // ── Statut ────────────────────────────────────────────────────────
        // Signale → En reprise → Termine → Rendu
        // "Pret" est l'ancienne valeur DB, migrée vers "Termine" au démarrage.
        public string Statut { get; set; } = "Signale";

        // ── Dates ─────────────────────────────────────────────────────────
        [Required]
        public DateTime DateSignalement { get; set; } = DateTime.Now;

        // RDV pour récupérer la pièce après reprise
        public DateTime? DateRdvReprise { get; set; }
        public TimeSpan? HeureDebutReprise { get; set; }
        public TimeSpan? HeureFinReprise { get; set; }

        // DateResolution : remplie quand Statut passe à "Termine".
        // Utilisée pour les compteurs couturiers (date d'effet).
        public DateTime? DateResolution { get; set; }

        // ── Traçabilité opérateurs ────────────────────────────────────────
        [Required]
        public int IdOperateurEnregistrement { get; set; }
        public string NomOperateurEnregistrement { get; set; } = string.Empty;

        public int? IdOperateurResolution { get; set; }
        public string? NomOperateurResolution { get; set; }

        // ── Retour arrière statut (Boss uniquement) ───────────────────────
        // Motif obligatoire lors d'un retour arrière de statut.
        public string? MotifRetourStatut { get; set; }
        public DateTime? DateRetourStatut { get; set; }
        public string? NomOperateurRetourStatut { get; set; }

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
                    "Termine"    => "Terminé ✓",
                    "Pret"       => "Terminé ✓",   // ancienne valeur DB (compat.)
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
            "Termine"    => "#059669",
            "Pret"       => "#059669",  // compat. ancienne valeur
            "Rendu"      => "#6B7280",
            _            => "#374151"
        };

        /// <summary>Vrai si le statut est « terminé » (ancienne ou nouvelle valeur DB).</summary>
        [NotMapped]
        public bool EstTermine => Statut is "Termine" or "Pret";

        // ── Couturier initial affiché (pour la colonne Couturiers) ────────
        [NotMapped]
        public string CouturierOrigineAffiche =>
            Couturier != null
                ? Couturier.Prenom + " " + Couturier.Nom
                : "—";

        // ── Vrai si le RDV est passé et le retour pas encore clôturé ─────
        [NotMapped]
        public bool RdvEnRetard =>
            DateRdvReprise.HasValue &&
            DateRdvReprise.Value.Date < DateTime.Today &&
            !EstTermine &&
            Statut != "Rendu" &&
            !EstAnnule;

        /// <summary>
        /// Couturier effectif de la reprise (IdCouturierReprise ou IdCouturier si null).
        /// </summary>
        [NotMapped]
        public int IdCouturierRepriseEffectif =>
            IdCouturierReprise.HasValue ? IdCouturierReprise.Value : IdCouturier;
    }
}
