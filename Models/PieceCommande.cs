using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Une pièce individuelle au sein d'une commande (Point 1 du cahier des
    /// charges V2 — "Commandes multi-pièces").
    ///
    /// ÉTAPE 1a — ajout additif : cette entité est introduite EN PARALLÈLE du
    /// modèle actuel, qui garde pour l'instant TypeVetement/IdCouturier/
    /// MontantTotal/IdCommission directement sur Commande. Rien n'est encore
    /// supprimé côté Commande, et aucun service/vue n'utilise encore
    /// PieceCommande : l'application continue de compiler et de fonctionner
    /// à l'identique après cette étape. C'est l'Étape 1b qui migrera
    /// CommandeService/CommissionService/CommandesView pour lire et écrire
    /// réellement via cette nouvelle table, puis l'Étape 1c qui retirera les
    /// champs devenus redondants sur Commande.
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
        // travail et calculer les commissions individuellement, contrairement
        // à l'ancien modèle où un seul couturier était rattaché à toute la
        // commande, même si elle contenait plusieurs vêtements différents.
        public int? IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        // Point 2 (Matériel/suppléments) : ce montant ne représente QUE la
        // couture, jamais le matériel facturé en plus. C'est cette valeur,
        // et uniquement elle, qui sert de base au calcul de la commission du
        // couturier — le nom "MontantCouture" (et non "MontantTotal", comme
        // sur l'ancien Commande.MontantTotal) est choisi précisément pour que
        // ce soit imposssible à confondre avec le futur montant facturé au
        // client une fois le matériel du Point 2 ajouté.
        [Required]
        [Column(TypeName = "TEXT")]
        public decimal MontantCouture { get; set; }

        // Cycle de vie propre à CHAQUE pièce (Point 1) : "A faire" -> "En cours"
        // -> "Terminee" -> "Livree". Le statut global de la commande n'est
        // jamais stocké : il est recalculé à la volée à partir des statuts de
        // toutes les pièces (voir la future propriété calculée sur Commande,
        // Étape 1b). [DÉCISION ACTÉE — 1.2] Pour une commande à une seule
        // pièce, ce statut de pièce EST directement le statut affiché à la
        // secrétaire — le mot "partiellement" n'existe que pour les commandes
        // à plusieurs pièces avec des statuts hétérogènes.
        public string Statut { get; set; } = "A faire";

        public List<Mesure> Mesures { get; set; } = new();

        // Point 5 (Alertes) : le rendez-vous est normalement porté par la
        // commande entière (un seul horaire de retrait pour toutes les
        // pièces). Ce champ ne sert QUE dans le cas d'exception documenté au
        // Point 5 — une pièce précise a un rendez-vous différent (urgence à
        // livrer avant les autres). Null = pas d'exception, on utilise le
        // rendez-vous global de la commande.
        public DateTime? RendezVousException { get; set; }

        // Verrouillage commission — migré depuis Commande.IdCommission
        // (Point 1.1) : une pièce déjà incluse dans une commission calculée
        // et enregistrée ne doit plus voir son couturier ou son montant de
        // couture modifiés (même logique de garde-fou que l'actuel
        // CommandeService.Modifier, à reporter sur PieceCommandeService en
        // Étape 1b).
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
