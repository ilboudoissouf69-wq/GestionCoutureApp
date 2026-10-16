using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Fiche de mesures réutilisables d'un client pour un type de vêtement donné.
    ///
    /// C'est la VERSION À JOUR (source de vérité) : quand on ouvre une nouvelle
    /// commande pour ce client + ce type de vêtement, on pré-remplit les champs
    /// depuis cette table. Si l'opérateur modifie les valeurs et confirme
    /// « Mettre à jour la fiche du client ? », on met à jour cette table.
    ///
    /// Les mesures de chaque <see cref="PieceCommande"/> restent inchangées
    /// (copie figée au moment de l'enregistrement) — elles ne sont JAMAIS
    /// rétro-modifiées par une mise à jour de la fiche client.
    ///
    /// Unicité : (IdClient, TypeVetement, NomMesure) — une valeur de mesure
    /// par triplet.  Garantie par l'index unique et par la contrainte EF Core.
    /// </summary>
    public class MesureClient
    {
        [Key]
        public int IdMesureClient { get; set; }

        // ── Clé client ────────────────────────────────────────────────────
        [Required]
        public int IdClient { get; set; }
        [ForeignKey("IdClient")]
        public Client? Client { get; set; }

        // ── Clé type de vêtement (libellé, pas FK — résistant aux suppressions
        //    de types et cohérent avec PieceCommande.TypeVetement qui stocke aussi
        //    le libellé).
        [Required]
        [MaxLength(100)]
        public string TypeVetement { get; set; } = string.Empty;

        // ── Mesure proprement dite ─────────────────────────────────────────
        [Required]
        [MaxLength(100)]
        public string NomMesure { get; set; } = string.Empty;

        [Required]
        [MaxLength(20)]
        public string Valeur { get; set; } = string.Empty;

        // ── Audit ─────────────────────────────────────────────────────────
        /// <summary>Date de la dernière mise à jour de cette valeur.</summary>
        public DateTime DateMiseAJour { get; set; } = DateTime.Now;

        /// <summary>Id de l'opérateur qui a fait la dernière mise à jour.</summary>
        public int IdOperateurMiseAJour { get; set; }

        /// <summary>Nom complet de l'opérateur (dénormalisé pour affichage sans Join).</summary>
        [MaxLength(200)]
        public string NomOperateurMiseAJour { get; set; } = string.Empty;
    }
}
