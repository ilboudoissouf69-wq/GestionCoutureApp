using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Contrat du service d'authentification.
    /// Définit CE QUE le service doit faire (pas comment).
    /// </summary>
    public interface IAuthService
    {
        Employe? Authentifier(string identifiant, string motDePasse);
        string HasherMotDePasse(string motDePasse);
        Employe? UtilisateurConnecte { get; }
    }
}