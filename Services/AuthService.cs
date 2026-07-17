using System.Collections.Concurrent;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;

        public Employe? UtilisateurConnecte { get; private set; }

        // ------------------------------------------------------------------
        // Protection anti brute-force (en mémoire, par identifiant).
        // Suffisant pour une appli mono-poste : un redémarrage de l'appli
        // réinitialise les compteurs, ce qui est un compromis acceptable ici
        // (contrairement à une appli web exposée sur Internet).
        // ------------------------------------------------------------------
        private const int MaxTentatives = 5;
        private static readonly TimeSpan DureeVerrouillage = TimeSpan.FromMinutes(2);

        private class SuiviTentatives
        {
            public int Echecs;
            public DateTime? VerrouJusqua;
        }

        private static readonly ConcurrentDictionary<string, SuiviTentatives> _tentatives = new();

        public AuthService(ApplicationDbContext context)
        {
            _context = context;
        }

        /// <summary>Hache un mot de passe avec l'algorithme sécurisé actuel (PBKDF2 + sel).</summary>
        public string HasherMotDePasse(string motDePasse) => PasswordHasher.Hasher(motDePasse);

        public Employe? Authentifier(string identifiant, string motDePasse)
        {
            string cle = identifiant.Trim().ToLowerInvariant();
            var suivi = _tentatives.GetOrAdd(cle, _ => new SuiviTentatives());

            lock (suivi)
            {
                if (suivi.VerrouJusqua.HasValue)
                {
                    var restant = suivi.VerrouJusqua.Value - DateTime.Now;
                    if (restant > TimeSpan.Zero)
                        throw new CompteVerrouilleException(restant);

                    // Le verrou a expiré : on repart sur un compteur propre.
                    suivi.Echecs = 0;
                    suivi.VerrouJusqua = null;
                }
            }

            var employe = AuthentifierInterne(identifiant, motDePasse);

            lock (suivi)
            {
                if (employe == null)
                {
                    suivi.Echecs++;
                    if (suivi.Echecs >= MaxTentatives)
                    {
                        suivi.VerrouJusqua = DateTime.Now + DureeVerrouillage;
                    }
                }
                else
                {
                    // Connexion réussie : on remet le compteur à zéro.
                    suivi.Echecs = 0;
                    suivi.VerrouJusqua = null;
                }
            }

            return employe;
        }

        private Employe? AuthentifierInterne(string identifiant, string motDePasse)
        {
            // On récupère l'employé par identifiant seul (le hash n'est plus comparable
            // directement en SQL puisqu'il dépend d'un sel unique par utilisateur).
            var employe = _context.Employes.FirstOrDefault(e => e.Identifiant == identifiant);

            if (employe == null || employe.Statut != "Actif")
                return null;

            bool motDePasseValide;

            if (PasswordHasher.EstAncienFormatSha256(employe.MotDePasse))
            {
                // Compatibilité : compte pas encore migré vers PBKDF2.
                motDePasseValide = employe.MotDePasse == PasswordHasher.HasherAncienSha256(motDePasse);

                if (motDePasseValide)
                {
                    // Migration transparente vers le hachage sécurisé, à la prochaine connexion réussie.
                    employe.MotDePasse = PasswordHasher.Hasher(motDePasse);
                    _context.SaveChanges();
                }
            }
            else
            {
                motDePasseValide = PasswordHasher.Verifier(motDePasse, employe.MotDePasse);
            }

            if (!motDePasseValide)
                return null;

            UtilisateurConnecte = employe;
            return employe;
        }
    }
}
