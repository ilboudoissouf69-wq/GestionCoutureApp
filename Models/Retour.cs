// Models/Retour.cs
// =============================================
// Point 4 — Retours (reprises gratuites) avec suivi par couturier.
//
// Après livraison, si un client revient parce que l'ajustement ne convient
// pas, c'est un RETOUR : aucun nouveau paiement, mais un enregistrement
// lié à la commande d'origine, à une PIÈCE précise, et au couturier
// responsable.
//
// Règles métier :
//   - Jamais supprimé, seulement annulé avec motif (même philosophie
//     que Paiement et Commission).
//   - Pas de limite de temps : un retour peut être signalé à tout moment
//     après la livraison.
//   - Rattaché à une PIÈCE précise (cohérent avec Point 1 multi-pièces).
//   - Statistiques par couturier utilisées par le Boss pour évaluer
//     la qualité du travail et décider des primes.
// =============================================

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Retour
    {
        [Key]
        public int IdRetour { get; set; }

        // ---- Commande d'origine ----
        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        // ---- Pièce concernée (Point 1 — multi-pièces) ----
        // Un retour est rattaché à une PIÈCE précise, jamais à la commande
        // entière. C'est cohérent avec le fait que chaque pièce a son
        // propre couturier et son propre statut.
        [Required]
        public int IdPieceCommande { get; set; }
        [ForeignKey("IdPieceCommande")]
        public PieceCommande? PieceCommande { get; set; }

        // ---- Couturier responsable ----
        // Celui qui a fait la pièce et qui doit refaire l'ajustement.
        // Snapshot du nom pour ne pas dépendre d'un éventuel renommage.
        [Required]
        public int IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        // ---- Description du problème ----
        // Ce qui ne convient pas au client (ex: "trop large au niveau
        // des épaules", "longueur trop courte", etc.).
        [Required]
        [MaxLength(500)]
        public string DescriptionProbleme { get; set; } = string.Empty;

        // ---- Statut du retour ----
        // "Signale"   : le client a signalé le problème, en attente de prise
        //                en charge par le couturier.
        // "En reprise" : le couturier travaille sur la correction.
        // "Resolu"    : la correction est faite, le client est satisfait.
        public string Statut { get; set; } = "Signale";

        // ---- Date de signalement ----
        [Required]
        public DateTime DateSignalement { get; set; } = DateTime.Now;

        // ---- Date de résolution ----
        // Null tant que le retour n'est pas résolu.
        public DateTime? DateResolution { get; set; }

        // ---- Traçabilité ----
        // Qui a enregistré ce retour et qui l'a résolu.
        [Required]
        public int IdOperateurEnregistrement { get; set; }
        public string NomOperateurEnregistrement { get; set; } = string.Empty;

        public int? IdOperateurResolution { get; set; }
        public string? NomOperateurResolution { get; set; }

        // CORRECTIF (audit) : le commentaire d'en-tête de ce fichier annonçait
        // déjà "jamais supprimé, seulement annulé avec motif", mais aucun
        // champ ni méthode ne le permettait réellement (RetourService n'avait
        // pas de méthode Annuler). Un retour signalé par erreur ne pouvait ni
        // être supprimé (pas de méthode) ni être annulé proprement (pas de
        // champ) — il restait coincé en "Signalé" pour toujours.
        public bool EstAnnule { get; set; } = false;
        public string? MotifAnnulation { get; set; }
        public DateTime? DateAnnulation { get; set; }
        public string? NomAnnulateur { get; set; }

        // ---- Propriétés calculées (affichage) ----
        [NotMapped]
        public string StatutAffiche
        {
            get
            {
                if (EstAnnule) return "Annulé";
                return Statut switch
                {
                    "Signale" => "Signalé",
                    "En reprise" => "En reprise",
                    "Resolu" => "Résolu",
                    _ => Statut
                };
            }
        }

        [NotMapped]
        public string DateSignalementAffichee => DateSignalement.ToString("dd/MM/yyyy");

        [NotMapped]
        public string DateResolutionAffichee =>
            DateResolution?.ToString("dd/MM/yyyy") ?? "-";
    }
}