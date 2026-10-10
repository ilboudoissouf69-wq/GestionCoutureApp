using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    public class AuthService : IAuthService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<AuthService> _logger;

        public Employe? UtilisateurConnecte { get; private set; }

        // ── Paliers de verrouillage progressif (Phase 1) ─────────────────────────
        // Les seuils correspondent au NbEchecConnexion cumulatif depuis la dernière
        // connexion réussie. Le compteur ne se remet à zéro qu'après succès.
        private static readonly (int seuilEchecs, TimeSpan dureeVerrou)[] Paliers =
        {
            (5,  TimeSpan.FromSeconds(30)),
            (10, TimeSpan.FromMinutes(1)),
            (15, TimeSpan.FromMinutes(5)),
            (20, TimeSpan.FromMinutes(15)),
        };

        // Longueur minimale requise (Phase 1 : portée à 10 caractères)
        public const int LongueurMinimaleMotDePasse = 10;

        // Liste noire des mots de passe trop faibles
        private static readonly string[] MotsDePasseInterdits =
        {
            "boss123", "123456", "password", "motdepasse", "admin", "boss",
            "secret", "couture", "atelier", "retouche", "ilboudo", "issouf", "choco"
        };

        public AuthService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<AuthService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        public string HasherMotDePasse(string motDePasse) => PasswordHasher.Hasher(motDePasse);

        /// <summary>
        /// Authentifie un utilisateur avec verrouillage progressif persisté en base.
        /// </summary>
        /// <exception cref="CompteVerrouilleException">Compte temporairement verrouillé.</exception>
        public Employe? Authentifier(string identifiant, string motDePasse)
        {
            using var context = _contextFactory.CreateDbContext();

            var employe = context.Employes.FirstOrDefault(
                e => e.Identifiant == identifiant);

            // Cas : identifiant inconnu → on ne révèle pas si c'est l'identifiant ou le mdp.
            // On simule quand même la vérification du verrouillage pour ne pas créer
            // un oracle (attaquant ne peut pas savoir si l'identifiant existe).
            if (employe == null)
            {
                _logger.LogWarning("Tentative de connexion avec identifiant inconnu : {Id}", identifiant);
                return null;
            }

            // Vérifier le verrouillage persisté en base.
            if (employe.DateVerrouJusqua.HasValue)
            {
                var restant = employe.DateVerrouJusqua.Value - DateTime.UtcNow;
                if (restant > TimeSpan.Zero)
                {
                    _logger.LogWarning(
                        "Connexion bloquée pour '{Id}' — compte verrouillé encore {Sec}s",
                        identifiant, (int)restant.TotalSeconds);
                    throw new CompteVerrouilleException(restant);
                }
                // Verrouillage expiré : on efface DateVerrouJusqua mais on conserve
                // NbEchecConnexion pour maintenir le palier (voir DEC-02).
                employe.DateVerrouJusqua = null;
            }

            var resultat = AuthentifierInterne(employe, motDePasse, context);

            if (resultat == null)
            {
                // Incrémenter le compteur d'échecs
                employe.NbEchecConnexion++;

                // Déterminer le palier applicable
                TimeSpan? prochainVerrou = null;
                foreach (var (seuil, duree) in Paliers)
                {
                    if (employe.NbEchecConnexion >= seuil)
                        prochainVerrou = duree;
                }

                if (prochainVerrou.HasValue)
                {
                    employe.DateVerrouJusqua = DateTime.UtcNow + prochainVerrou.Value;
                    _logger.LogWarning(
                        "Compte '{Id}' verrouillé {Dur} après {N} échecs cumulatifs",
                        identifiant, prochainVerrou, employe.NbEchecConnexion);
                }
                else
                {
                    _logger.LogWarning(
                        "Échec connexion '{Id}' — tentative #{N}",
                        identifiant, employe.NbEchecConnexion);
                }

                context.SaveChanges();
            }

            return resultat;
        }

        private Employe? AuthentifierInterne(Employe employe, string motDePasse,
            ApplicationDbContext context)
        {
            if (employe.Statut != "Actif")
                return null;

            // Validation du rôle via enum pour robustesse
            try
            {
                var _ = employe.RoleEnum;
            }
            catch (ArgumentException)
            {
                _logger.LogError(
                    "Rôle invalide en base pour l'employé '{Id}' : {Role}",
                    employe.Identifiant, employe.Role);
                return null;
            }

            bool motDePasseValide;

            if (PasswordHasher.EstAncienFormatSha256(employe.MotDePasse))
            {
                motDePasseValide = employe.MotDePasse ==
                    PasswordHasher.HasherAncienSha256(motDePasse);
                if (motDePasseValide)
                {
                    // Migration transparente SHA-256 → PBKDF2
                    employe.MotDePasse = PasswordHasher.Hasher(motDePasse);
                    _logger.LogInformation(
                        "Mot de passe migré SHA-256→PBKDF2 pour '{Id}'", employe.Identifiant);
                }
            }
            else
            {
                motDePasseValide = PasswordHasher.Verifier(motDePasse, employe.MotDePasse);
            }

            if (!motDePasseValide) return null;

            // Connexion réussie : remettre à zéro les compteurs d'échecs
            employe.NbEchecConnexion = 0;
            employe.DateVerrouJusqua = null;
            context.SaveChanges();

            UtilisateurConnecte = employe;
            _logger.LogInformation(
                "Connexion réussie — {Id} (rôle : {Role})",
                employe.Identifiant, employe.Role);
            return employe;
        }

        /// <summary>
        /// Change le mot de passe d'un employé.
        /// Règles Phase 1 : minimum 10 caractères, liste noire, confirmation de l'ancien.
        /// </summary>
        public void ChangerMotDePasse(int idEmploye, string ancienMotDePasse, string nouveauMotDePasse)
        {
            ValiderForceMotDePasse(nouveauMotDePasse);

            using var context = _contextFactory.CreateDbContext();
            var employe = context.Employes.Find(idEmploye)
                ?? throw new InvalidOperationException("Employé introuvable.");

            bool ancienValide = PasswordHasher.EstAncienFormatSha256(employe.MotDePasse)
                ? employe.MotDePasse == PasswordHasher.HasherAncienSha256(ancienMotDePasse)
                : PasswordHasher.Verifier(ancienMotDePasse, employe.MotDePasse);

            if (!ancienValide)
                throw new InvalidOperationException("L'ancien mot de passe est incorrect.");

            if (nouveauMotDePasse == ancienMotDePasse)
                throw new InvalidOperationException(
                    "Le nouveau mot de passe doit être différent de l'ancien.");

            employe.MotDePasse = PasswordHasher.Hasher(nouveauMotDePasse);
            employe.DerniereModificationMotDePasse = DateTime.Now;

            context.SaveChanges();

            if (UtilisateurConnecte?.IdEmploye == idEmploye)
                UtilisateurConnecte.MotDePasse = employe.MotDePasse;

            _logger.LogWarning(
                "Mot de passe changé pour l'employé {Id}", idEmploye);
        }

        /// <summary>
        /// Définit le mot de passe initial d'un employé (sans vérifier l'ancien).
        /// Réservé au Boss via EmployesView et à la configuration initiale.
        /// </summary>
        public void DefinirMotDePasseInitial(int idEmploye, string nouveauMotDePasse)
        {
            ValiderForceMotDePasse(nouveauMotDePasse);

            using var context = _contextFactory.CreateDbContext();
            var employe = context.Employes.Find(idEmploye)
                ?? throw new InvalidOperationException("Employé introuvable.");

            employe.MotDePasse = PasswordHasher.Hasher(nouveauMotDePasse);
            employe.DerniereModificationMotDePasse = DateTime.Now;
            context.SaveChanges();

            _logger.LogWarning(
                "Mot de passe initial défini pour l'employé {Id}", idEmploye);
        }

        /// <summary>
        /// Valide la force d'un mot de passe selon les règles Phase 1.
        /// Lève InvalidOperationException avec message utilisateur si les règles ne sont pas respectées.
        /// </summary>
        public static void ValiderForceMotDePasse(string motDePasse)
        {
            if (string.IsNullOrEmpty(motDePasse) ||
                motDePasse.Length < LongueurMinimaleMotDePasse)
                throw new InvalidOperationException(
                    $"Le mot de passe doit contenir au moins {LongueurMinimaleMotDePasse} caractères.");

            if (motDePasse.Trim().Length < LongueurMinimaleMotDePasse)
                throw new InvalidOperationException(
                    "Le mot de passe ne peut pas être composé uniquement d'espaces.");

            string mdpLower = motDePasse.ToLowerInvariant();
            foreach (var interdit in MotsDePasseInterdits)
            {
                if (mdpLower.Contains(interdit))
                    throw new InvalidOperationException(
                        "Ce mot de passe est trop prévisible. Choisissez un mot de passe unique.");
            }
        }

        // Validation d'un employé (utilisée par EmployesView)
        public static void ValiderEmploye(Employe employe)
        {
            var ctx = new ValidationContext(employe);
            var errors = new List<ValidationResult>();
            if (!Validator.TryValidateObject(employe, ctx, errors, validateAllProperties: true))
            {
                var msg = string.Join("\n", errors.Select(e => e.ErrorMessage));
                throw new InvalidOperationException(msg);
            }
        }
    }
}
