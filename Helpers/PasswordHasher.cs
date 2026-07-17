using System;
using System.Security.Cryptography;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// Hachage sécurisé des mots de passe avec PBKDF2 + sel aléatoire unique par utilisateur.
    /// Remplace l'ancien hachage SHA-256 sans sel, vulnérable aux rainbow tables.
    ///
    /// Format stocké en base (une seule colonne "MotDePasse", texte) :
    ///   PBKDF2.{iterations}.{saltBase64}.{hashBase64}
    ///
    /// Le préfixe "PBKDF2." permet à VerifierEtMigrerSiBesoin() de reconnaître
    /// automatiquement les anciens hash SHA-256 (hex, 64 caractères, sans point)
    /// et de les migrer en douceur à la prochaine connexion réussie.
    /// </summary>
    public static class PasswordHasher
    {
        private const int TailleSelOctets = 16;   // 128 bits
        private const int TailleHashOctets = 32;  // 256 bits
        private const int Iterations = 100_000;   // recommandation OWASP 2024+ pour PBKDF2-SHA256

        /// <summary>Hache un mot de passe en clair avec un nouveau sel aléatoire.</summary>
        public static string Hasher(string motDePasse)
        {
            byte[] sel = RandomNumberGenerator.GetBytes(TailleSelOctets);
            byte[] hash = Rfc2898DeriveBytes.Pbkdf2(
                motDePasse, sel, Iterations, HashAlgorithmName.SHA256, TailleHashOctets);

            return $"PBKDF2.{Iterations}.{Convert.ToBase64String(sel)}.{Convert.ToBase64String(hash)}";
        }

        /// <summary>Vérifie un mot de passe en clair contre un hash stocké (nouveau format PBKDF2).</summary>
        public static bool Verifier(string motDePasse, string hashStocke)
        {
            if (string.IsNullOrEmpty(hashStocke)) return false;

            var parties = hashStocke.Split('.');
            if (parties.Length != 4 || parties[0] != "PBKDF2") return false;

            if (!int.TryParse(parties[1], out int iterations)) return false;

            byte[] sel = Convert.FromBase64String(parties[2]);
            byte[] hashAttendu = Convert.FromBase64String(parties[3]);

            byte[] hashCalcule = Rfc2898DeriveBytes.Pbkdf2(
                motDePasse, sel, iterations, HashAlgorithmName.SHA256, hashAttendu.Length);

            // Comparaison en temps constant pour éviter les attaques par timing
            return CryptographicOperations.FixedTimeEquals(hashCalcule, hashAttendu);
        }

        /// <summary>
        /// Compatibilité ascendante : reconnaît un ancien hash SHA-256 (hex 64 caractères, aucun ".").
        /// Utilisé uniquement pour migrer en douceur les mots de passe existants.
        /// </summary>
        public static bool EstAncienFormatSha256(string hashStocke)
        {
            return !string.IsNullOrEmpty(hashStocke)
                   && hashStocke.Length == 64
                   && !hashStocke.Contains('.');
        }

        /// <summary>Reproduit l'ancien hachage SHA-256 (pour vérifier un mot de passe saisi contre un vieux hash).</summary>
        public static string HasherAncienSha256(string motDePasse)
        {
            using SHA256 sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(motDePasse));
            var builder = new System.Text.StringBuilder();
            foreach (byte b in bytes) builder.Append(b.ToString("x2"));
            return builder.ToString();
        }
    }
}
