using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Une pièce individuelle au sein d'une commande (Point 1 du cahier des
    /// charges V2 — "Commandes multi-pièces").
    ///
    /// [DÉCISION ACTÉE — 1.1] À terme (fin de l'Étape 1c), c'est CETTE classe
    /// qui portera l'identité du "vêtement" — TypeVetement, couturier,
    /// montant, verrouillage de commission — et Commande deviendra un pur
    /// conteneur (client, dépôt/rendez-vous global, paiements).
    /// </summary>
    public class PieceCommande
    {
        [Key]
        public int IdPieceCommande { get; set; }

        [Required]
        public int IdCommande { get; set; }
        [ForeignKey("IdCommande")]
        public Commande? Commande { get; set; }

        [Required]
        public string TypeVetement { get; set; } = string.Empty;

        public string DescriptionPrecision { get; set; } = string.Empty;

        public string CheminPhoto { get; set; } = string.Empty;

        // Un couturier PAR PIÈCE (Point 1) — indispensable pour répartir le
        // travail et calculer les commissions individuellement.
        public int? IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        // Point 2 (Matériel/suppléments) : ce montant ne représente QUE la
        // couture, jamais le matériel facturé en plus.
        [Required]
        [Column(TypeName = "TEXT")]
        public decimal MontantCouture { get; set; }

        // Cycle de vie propre à CHAQUE pièce (Point 1) : "A faire" -> "En cours"
        // -> "Terminee" -> "Livree".
        // [DÉCISION ACTÉE — 1.2] Pour une commande à une seule
        // pièce, ce statut de pièce EST directement le statut affiché.
        public string Statut { get; set; } = "A faire";

        public List<Mesure> Mesures { get; set; } = new();

        public List<MaterielSupplement> MaterielSupplements { get; set; } = new();

        // CORRECTIF (audit) : le cahier (Point 1) exige une "traçabilité" pour
        // l'exception Boss d'ajout de pièce après encaissement — jusqu'ici, le
        // motif saisi à l'écran était validé (non vide) puis jeté, sans jamais
        // être conservé nulle part. Ce champ le conserve avec la pièce
        // concernée. Reste null pour toutes les pièces ajoutées normalement
        // (sans acompte déjà encaissé).
        public string? MotifAjoutApresEncaissement { get; set; }

        // Propriété [NotMapped] pour la réutilisation des mesures
        // dans la ComboBox de CommandesView.
        [NotMapped]
        public string LabelReutilisation =>
            $"Cmd {IdCommande} — {TypeVetement} (piece {IdPieceCommande})";

        // Point 5 (Alertes) : le rendez-vous est normalement porté par la
        // commande entière. Ce champ ne sert QUE dans le cas d'exception —
        // une pièce précise a un rendez-vous différent (urgence).
        // Null = pas d'exception, on utilise le rendez-vous global de la commande.
        public DateTime? RendezVousException { get; set; }

        // Verrouillage commission — migré depuis Commande.IdCommission (Point 1.1)
        public int? IdCommission { get; set; }
        [ForeignKey("IdCommission")]
        public Commission? Commission { get; set; }

        [NotMapped]
        public string StatutAffiche => Statut switch
        {
            "A faire" => "À faire",
            "En cours" => "En cours",
            "Terminee" => "Terminée",
            "Livree" => "Livrée",
            _ => Statut
        };
    }
}