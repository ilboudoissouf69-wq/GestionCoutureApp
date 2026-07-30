using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GestionCoutureApp.Models
{
    public class Commande
    {
        [Key]
        public int IdCommande { get; set; }

        [Required]
        public int IdClient { get; set; }
        [ForeignKey("IdClient")]
        public Client? Client { get; set; }

        // ============================================================
        // ÉTAPE 1a — CHAMPS DÉPRÉCIÉS (Point 1.1 du cahier des charges V2)
        // ------------------------------------------------------------
        // Ces champs représentaient jusqu'ici l'identité d'UN SEUL
        // vêtement par commande. Le Point 1 ("Commandes multi-pièces")
        // demande de les faire migrer intégralement vers PieceCommande,
        // pour éviter deux sources de vérité contradictoires (une commande
        // qui aurait à la fois un TypeVetement direct ET une liste de
        // pièces avec leur propre TypeVetement).
        //
        // Ils restent PLEINEMENT FONCTIONNELS pour l'instant (Étape 1a
        // n'a encore rien migré côté services/vues) : c'est volontaire et
        // sans risque. L'attribut [Obsolete] ci-dessous ne casse rien à la
        // compilation (c'est un warning, pas une erreur) — c'est un outil :
        // lancez `dotnet build` après l'Étape 1b et la liste des warnings
        // vous donnera exactement les endroits du code qu'il reste à migrer
        // vers PieceCommande, sans avoir à les chercher à la main.
        // ============================================================

        [Obsolete("Étape 1 (multi-pièces) : migrer vers PieceCommande.TypeVetement.")]
        [Required]
        public string TypeVetement { get; set; } = string.Empty;

        [Obsolete("Étape 1 (multi-pièces) : migrer vers PieceCommande.IdCouturier / Couturier.")]
        public int? IdCouturier { get; set; }
        [ForeignKey("IdCouturier")]
        public Employe? Couturier { get; set; }

        [Obsolete("Étape 1 (multi-pièces) : migrer vers PieceCommande.MontantCouture.")]
        [Required]
        [Column(TypeName = "TEXT")]
        public decimal MontantTotal { get; set; }

        [Obsolete("Étape 1 (multi-pièces) : migrer vers PieceCommande.IdCommission / Commission.")]
        public int? IdCommission { get; set; }
        [ForeignKey("IdCommission")]
        public Commission? Commission { get; set; }

        // NOUVEAU (Point 1) : une commande contient désormais une ou
        // plusieurs pièces. C'est cette collection qui, à partir de
        // l'Étape 1b, portera réellement TypeVetement/couturier/montant/
        // statut/commission — les champs ci-dessus ne seront alors plus
        // que des reliquats vidés avant suppression définitive (Étape 1c).
        public List<PieceCommande> Pieces { get; set; } = new();

        [Obsolete("Étape 1 (multi-pièces) : migrer vers PieceCommande.DescriptionPrecision (détail propre à chaque vêtement).")]
        public string DescriptionPrecision { get; set; } = string.Empty;

        [Obsolete("Étape 1 (multi-pièces) : migrer vers PieceCommande.CheminPhoto (une photo par vêtement, pas par commande).")]
        public string CheminPhoto { get; set; } = string.Empty;

        [Required]
        public DateTime DateDebut { get; set; } = DateTime.Now;

        [Required]
        public DateTime DateFin { get; set; }

        public string Statut { get; set; } = "A faire";

        public List<Mesure> Mesures { get; set; } = new();
        public List<Paiement> Paiements { get; set; } = new();
        public List<MaterielSupplement> MaterielSupplements { get; set; } = new();

        // Seuls les paiements NON annulés comptent pour le reste à payer
        //
        // ÉTAPE 1b-i : bascule effective sur MontantTotalCalcule (somme des
        // Pieces.MontantCouture), comme annoncé dans le commentaire de
        // l'Étape 1a. L'ancien champ MontantTotal n'est plus jamais renseigné
        // par CommandeService à partir de maintenant : continuer à l'utiliser
        // ici afficherait silencieusement 0 FCFA de reste à payer pour toute
        // nouvelle commande.
        [NotMapped]
        public decimal ResteAPayer =>
            MontantTotalCalcule - Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

        // Total réellement encaissé (paiements valides) — base pour les commissions
        [NotMapped]
        public decimal MontantEncaisse =>
            Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);

        public TimeSpan HeureDebut { get; set; }
        public TimeSpan? HeureFin { get; set; }

        // ============================================================
        // NOUVEAU Étape 1b-i (Point 1 — Commandes multi-pièces)
        // ------------------------------------------------------------
        // Ces propriétés calculées permettent aux vues existantes (Dashboard,
        // Reçu, Paiements, Commissions...) de continuer à lire "le montant",
        // "le type de vêtement", "le couturier" d'une commande SANS avoir à
        // naviguer manuellement dans Pieces partout. Tant que CommandesView
        // ne crée qu'UNE SEULE pièce par commande (garanti jusqu'à l'Étape
        // 1b-ii), elles ont un sens univoque. Dès que l'UI multi-pièces
        // arrivera (1b-ii), chaque vue qui les utilise devra être revue
        // individuellement — ces raccourcis ne seront plus suffisants pour
        // une commande de plusieurs pièces différentes.
        // ============================================================

        [NotMapped]
        public decimal MontantTotalCalcule => Pieces.Sum(p => p.MontantCouture);

        [NotMapped]
        public string TypeVetementAffiche => Pieces.Count switch
        {
            0 => "(aucune pièce)",
            1 => Pieces[0].TypeVetement,
            _ => string.Join(" + ", Pieces.Select(p => p.TypeVetement))
        };

        [NotMapped]
        public int? IdCouturierUnique => Pieces.Count == 1 ? Pieces[0].IdCouturier : null;

        [NotMapped]
        public Employe? CouturierUnique => Pieces.Count == 1 ? Pieces[0].Couturier : null;

        // [DÉCISION ACTÉE — 1.2] Statut global TOUJOURS calculé, jamais un
        // champ modifiable directement. Le champ "Statut" ci-dessus reste en
        // base pour compatibilité (lignes existantes) mais n'est plus la
        // source de vérité affichée : c'est StatutGlobal qui doit être lu
        // partout dans les vues à partir de maintenant.
        //
        // Note de conception (à raffiner en 1b-ii avec de vrais cas d'usage
        // vus par la secrétaire) : le cahier des charges précise seulement
        // "toutes les pièces au même statut" et "certaines Livrées/Terminées,
        // d'autres non". Pour un mélange qui n'atteint encore aucun statut
        // "avancé" (uniquement des pièces "À faire"/"En cours"), le choix
        // fait ici est d'afficher le statut le plus avancé du groupe plutôt
        // que d'inventer un nouveau libellé non demandé dans le cahier.
        [NotMapped]
        public string StatutGlobal
        {
            get
            {
                if (Pieces.Count == 0) return Statut; // ne devrait plus arriver après 1b-i
                if (Pieces.Count == 1) return Pieces[0].Statut; // règle 1.2 : jamais de "partiellement" à une pièce

                var statutsDistincts = Pieces.Select(p => p.Statut).Distinct().ToList();
                if (statutsDistincts.Count == 1) return statutsDistincts[0];

                if (Pieces.Any(p => p.Statut == "Livree"))
                    return "Livree partiellement";
                if (Pieces.Any(p => p.Statut == "Terminee"))
                    return "Terminee partiellement";

                return Pieces.Any(p => p.Statut == "En cours") ? "En cours" : "A faire";
            }
        }

        [NotMapped]
        public string StatutGlobalAffiche => StatutGlobal switch
        {
            "A faire" => "À faire",
            "En cours" => "En cours",
            "Terminee" => "Terminée",
            "Livree" => "Livrée",
            "Terminee partiellement" => "Terminée partiellement",
            "Livree partiellement" => "Livrée partiellement",
            _ => StatutGlobal
        };
    }
}
