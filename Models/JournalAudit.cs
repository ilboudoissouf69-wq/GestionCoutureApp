using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Journal d'audit immuable avec chaînage de hash (blockchain simplifiée).
    /// Cette table est APPEND-ONLY : aucune méthode Update/Delete ne doit exister
    /// dans aucun service, même pour le Boss. Toute modification manuelle de la
    /// base SQLite sera détectable par vérification de la chaîne de hash.
    /// </summary>
    public class JournalAudit
    {
        [Key]
        public int IdJournal { get; set; }

        /// <summary>
        /// Date et heure UTC de l'action (UTC pour éviter les ambiguïtés de fuseaux horaires)
        /// </summary>
        [Required]
        public DateTime DateHeureUtc { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// ID de l'opérateur qui a effectué l'action
        /// </summary>
        [Required]
        public int IdOperateur { get; set; }

        /// <summary>
        /// Nom complet de l'opérateur (snapshot au moment de l'action)
        /// </summary>
        [Required]
        [MaxLength(200)]
        public string NomOperateur { get; set; } = string.Empty;

        /// <summary>
        /// Rôle de l'opérateur au moment de l'action (Boss, Secretaire, Couturier)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string RoleOperateur { get; set; } = string.Empty;

        /// <summary>
        /// Type d'action effectuée
        /// Exemples: "PAIEMENT_ANNULE", "COMMANDE_SUPPRIMEE", "DEPENSE_ANNULEE", 
        ///          "COMMISSION_ANNULEE", "COMMANDE_MODIFIEE", "PAIEMENT_AJOUTE"
        /// </summary>
        [Required]
        [MaxLength(100)]
        public string TypeAction { get; set; } = string.Empty;

        /// <summary>
        /// Type d'entité affectée (Commande, Paiement, Depense, Commission, Retour, etc.)
        /// </summary>
        [Required]
        [MaxLength(50)]
        public string Entite { get; set; } = string.Empty;

        /// <summary>
        /// ID de l'entité affectée (ex: IdPaiement, IdCommande)
        /// </summary>
        public int IdEntite { get; set; }

        /// <summary>
        /// Valeurs avant modification (JSON) - null pour les créations
        /// </summary>
        [Column(TypeName = "TEXT")]
        public string? ValeursAvant { get; set; }

        /// <summary>
        /// Valeurs après modification (JSON) - null pour les suppressions
        /// </summary>
        [Column(TypeName = "TEXT")]
        public string? ValeursApres { get; set; }

        /// <summary>
        /// Motif de l'action (obligatoire pour les annulations et suppressions)
        /// </summary>
        [MaxLength(500)]
        public string? Motif { get; set; }

        /// <summary>
        /// Hash SHA256 de l'entrée précédente dans le journal (chaînage blockchain)
        /// Permet de détecter toute modification ou suppression d'une entrée
        /// </summary>
        [MaxLength(64)]
        public string? HashPrecedent { get; set; }

        /// <summary>
        /// Hash SHA256 de cette entrée (calculé à partir de toutes les propriétés ci-dessus)
        /// </summary>
        [Required]
        [MaxLength(64)]
        public string HashCourant { get; set; } = string.Empty;

        /// <summary>
        /// Adresse IP de l'opérateur (si disponible) - utile pour détecter des accès suspects
        /// </summary>
        [MaxLength(50)]
        public string? AdresseIp { get; set; }

        /// <summary>
        /// Indicateur si une notification externe a été envoyée (WhatsApp/Email)
        /// pour les actions critiques
        /// </summary>
        public bool NotificationEnvoyee { get; set; } = false;

        /// <summary>
        /// Calcule le hash SHA256 de cette entrée pour le chaînage.
        /// IMPORTANT : IdJournal est volontairement EXCLU du hash.
        /// IdJournal est un identifiant technique assigné par EF Core après
        /// SaveChanges() — l'inclure forcerait un double SaveChanges ou un
        /// calcul de hash avant persistance (où IdJournal vaut 0), ce qui
        /// rendrait le hash stocké incohérent avec tout recalcul ultérieur.
        /// L'intégrité du chaînage est assurée par HashPrecedent → HashCourant,
        /// qui lie chaque entrée à la précédente par son contenu métier.
        /// </summary>
        public string CalculerHash()
        {
            // Toutes les colonnes métier, sauf IdJournal (clé technique).
            var data = $"{DateHeureUtc:O}|{IdOperateur}|{NomOperateur}|{RoleOperateur}|" +
                      $"{TypeAction}|{Entite}|{IdEntite}|{ValeursAvant ?? ""}|{ValeursApres ?? ""}|" +
                      $"{Motif ?? ""}|{HashPrecedent ?? ""}|{AdresseIp ?? ""}";

            using var sha256 = SHA256.Create();
            var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(data));
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        /// <summary>
        /// Vérifie que le hash courant correspond au contenu de l'entrée
        /// </summary>
        public bool VerifierIntegrite()
        {
            return HashCourant == CalculerHash();
        }

        // ── Propriétés calculées pour affichage ──────────────────────────────

        [NotMapped]
        public string DateHeureLocale => DateHeureUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm:ss");

        [NotMapped]
        public string ActionAffichee => TypeAction switch
        {
            "PAIEMENT_ANNULE" => "Paiement annulé",
            "COMMANDE_SUPPRIMEE" => "Commande supprimée",
            "COMMANDE_ANNULEE" => "Commande annulée",
            "DEPENSE_ANNULEE" => "Dépense annulée",
            "COMMISSION_ANNULEE" => "Commission annulée",
            "RETOUR_ANNULE" => "Retour annulé",
            "PAIEMENT_AJOUTE" => "Paiement ajouté",
            "COMMANDE_CREEE" => "Commande créée",
            "COMMANDE_MODIFIEE" => "Commande modifiée",
            "DEPENSE_VALIDEE" => "Dépense validée",
            "COMMISSION_ENREGISTREE" => "Commission enregistrée",
            _ => TypeAction
        };

        [NotMapped]
        public string CouleurAction => TypeAction switch
        {
            var a when a.Contains("ANNULE") || a.Contains("SUPPRIME") => "#DC2626", // Rouge
            var a when a.Contains("AJOUTE") || a.Contains("CREE") || a.Contains("VALIDEE") => "#059669", // Vert
            var a when a.Contains("MODIFIE") => "#D97706", // Orange
            _ => "#6B7280" // Gris
        };

        [NotMapped]
        public string ResumeModification
        {
            get
            {
                if (!string.IsNullOrEmpty(Motif))
                    return Motif;

                if (string.IsNullOrEmpty(ValeursAvant) && !string.IsNullOrEmpty(ValeursApres))
                    return "Création";

                if (!string.IsNullOrEmpty(ValeursAvant) && string.IsNullOrEmpty(ValeursApres))
                    return "Suppression";

                return "Modification";
            }
        }
    }

    /// <summary>
    /// Helper pour sérialiser les objets en JSON pour le journal d'audit
    /// </summary>
    public static class AuditJsonHelper
    {
        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };

        public static string Serialize(object? obj)
        {
            if (obj == null) return "";
            return JsonSerializer.Serialize(obj, Options);
        }

        public static T? Deserialize<T>(string? json)
        {
            if (string.IsNullOrEmpty(json)) return default;
            return JsonSerializer.Deserialize<T>(json, Options);
        }
    }
}
