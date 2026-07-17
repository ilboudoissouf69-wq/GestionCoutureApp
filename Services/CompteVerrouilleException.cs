using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Exception levée quand un compte est temporairement verrouillé
    /// suite à trop de tentatives de connexion échouées.
    /// </summary>
    public class CompteVerrouilleException : Exception
    {
        public TimeSpan TempsRestant { get; }

        public CompteVerrouilleException(TimeSpan tempsRestant)
            : base($"Trop de tentatives échouées. Réessayez dans {Math.Ceiling(tempsRestant.TotalSeconds)} secondes.")
        {
            TempsRestant = tempsRestant;
        }
    }

    /// <summary>
    /// Contrat du service d'authentification.
    /// Définit CE QUE le service doit faire (pas comment).
    /// </summary>
    public interface IAuthService
    {
        /// <exception cref="CompteVerrouilleException">
        /// Levée si l'identifiant a dépassé le nombre maximal de tentatives échouées récentes.
        /// </exception>
        Employe? Authentifier(string identifiant, string motDePasse);
        string HasherMotDePasse(string motDePasse);
        Employe? UtilisateurConnecte { get; }
    }
}