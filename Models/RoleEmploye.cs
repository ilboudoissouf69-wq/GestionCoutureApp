namespace GestionCoutureApp.Models
{
    /// <summary>
    /// Énumération des rôles d'employés dans l'atelier de couture.
    /// Utilisée pour remplacer les chaînes magiques ("Boss", "Secretaire", "Couturier")
    /// et garantir la cohérence des vérifications d'autorisation.
    /// </summary>
    public enum RoleEmploye
    {
        /// <summary>Administrateur avec accès complet à toutes les fonctionnalités</summary>
        Boss = 0,
        
        /// <summary>Secrétaire avec accès partiel (clients, commandes, paiements)</summary>
        Secretaire = 1,
        
        /// <summary>Couturier avec accès restreint (ses propres commandes uniquement)</summary>
        Couturier = 2
    }

    /// <summary>
    /// Méthodes d'extension pour RoleEmploye
    /// </summary>
    public static class RoleEmployeExtensions
    {
        /// <summary>
        /// Convertit l'enum en chaîne pour stockage en base de données (compatibilité EF Core)
        /// </summary>
        public static string ToDbString(this RoleEmploye role)
        {
            return role switch
            {
                RoleEmploye.Boss => "Boss",
                RoleEmploye.Secretaire => "Secretaire",
                RoleEmploye.Couturier => "Couturier",
                _ => throw new ArgumentOutOfRangeException(nameof(role), $"Rôle inconnu : {role}")
            };
        }

        /// <summary>
        /// Convertit une chaîne de base de données en enum (avec tolérance aux variations)
        /// </summary>
        public static RoleEmploye FromDbString(string roleStr)
        {
            return roleStr?.Trim().ToLowerInvariant() switch
            {
                "boss" => RoleEmploye.Boss,
                "secretaire" => RoleEmploye.Secretaire,
                "couturier" => RoleEmploye.Couturier,
                _ => throw new ArgumentException($"Rôle invalide : {roleStr}")
            };
        }

        /// <summary>
        /// Vérifie si une chaîne correspond à un rôle valide (pour validation avant conversion)
        /// </summary>
        public static bool IsValidRoleString(string? roleStr)
        {
            if (string.IsNullOrWhiteSpace(roleStr)) return false;
            
            return roleStr.Trim().ToLowerInvariant() switch
            {
                "boss" or "secretaire" or "couturier" => true,
                _ => false
            };
        }
    }
}
