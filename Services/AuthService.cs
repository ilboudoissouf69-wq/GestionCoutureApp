using System.Collections.Concurrent;
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

        // Protection anti brute-force (en mémoire, par identifiant)
        private const int MaxTentatives = 5;
        private static readonly TimeSpan DureeVerrouillage = TimeSpan.FromMinutes(2);

        private class SuiviTentatives
        {
            public int Echecs;
            public DateTime? VerrouJusqua;
        }

        private static readonly ConcurrentDictionary<string, SuiviTentatives> _tentatives = new();

        public AuthService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<AuthService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

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
                    {
                        _logger.LogWarning(
                            "Connexion bloquée pour '{Id}' — compte verrouillé encore {Sec}s",
                            identifiant, (int)restant.TotalSeconds);
                        throw new CompteVerrouilleException(restant);
                    }
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
                    _logger.LogWarning(
                        "Echec connexion '{Id}' — tentative {N}/{Max}",
                        identifiant, suivi.Echecs, MaxTentatives);

                    if (suivi.Echecs >= MaxTentatives)
                    {
                        suivi.VerrouJusqua = DateTime.Now + DureeVerrouillage;
                        _logger.LogWarning(
                            "Compte '{Id}' verrouillé pour {Min} min après {Max} échecs",
                            identifiant, DureeVerrouillage.TotalMinutes, MaxTentatives);
                    }
                }
                else
                {
                    suivi.Echecs = 0;
                    suivi.VerrouJusqua = null;
                    _logger.LogInformation(
                        "Connexion réussie — {Id} (rôle : {Role})",
                        identifiant, employe.Role);
                }
            }

            return employe;
        }

        private Employe? AuthentifierInterne(string identifiant, string motDePasse)
        {
            using var context = _contextFactory.CreateDbContext();
            var employe = context.Employes.FirstOrDefault(e => e.Identifiant == identifiant);

            if (employe == null || employe.Statut != "Actif")
                return null;

            bool motDePasseValide;

            if (PasswordHasher.EstAncienFormatSha256(employe.MotDePasse))
            {
                motDePasseValide = employe.MotDePasse == PasswordHasher.HasherAncienSha256(motDePasse);
                if (motDePasseValide)
                {
                    // Migration transparente vers PBKDF2
                    employe.MotDePasse = PasswordHasher.Hasher(motDePasse);
                    context.SaveChanges();
                    _logger.LogInformation(
                        "Mot de passe migré SHA-256→PBKDF2 pour '{Id}'", identifiant);
                }
            }
            else
            {
                motDePasseValide = PasswordHasher.Verifier(motDePasse, employe.MotDePasse);
            }

            if (!motDePasseValide) return null;

            UtilisateurConnecte = employe;
            return employe;
        }

        // ----------------------------------------------------------------
        // Changement de mot de passe (utilisé notamment pour forcer le
        // changement du mot de passe par défaut du compte Boss — voir
        // Employe.DoitChangerMotDePasse et LoginWindow).
        // ----------------------------------------------------------------
        public void ChangerMotDePasse(int idEmploye, string ancienMotDePasse, string nouveauMotDePasse)
        {
            if (string.IsNullOrWhiteSpace(nouveauMotDePasse) || nouveauMotDePasse.Length < 6)
                throw new InvalidOperationException("Le nouveau mot de passe doit contenir au moins 6 caractères.");

            using var context = _contextFactory.CreateDbContext();
            var employe = context.Employes.Find(idEmploye)
                ?? throw new InvalidOperationException("Employé introuvable.");

            bool ancienValide = PasswordHasher.EstAncienFormatSha256(employe.MotDePasse)
                ? employe.MotDePasse == PasswordHasher.HasherAncienSha256(ancienMotDePasse)
                : PasswordHasher.Verifier(ancienMotDePasse, employe.MotDePasse);

            if (!ancienValide)
                throw new InvalidOperationException("L'ancien mot de passe est incorrect.");

            employe.MotDePasse = PasswordHasher.Hasher(nouveauMotDePasse);
            context.SaveChanges();

            // Garde la session en mémoire cohérente avec la base
            if (UtilisateurConnecte != null && UtilisateurConnecte.IdEmploye == idEmploye)
                UtilisateurConnecte.MotDePasse = employe.MotDePasse;

            _logger.LogWarning("Mot de passe changé (obligatoire ou volontaire) pour l'employé {Id}", idEmploye);
        }

        // ----------------------------------------------------------------
        // Validation d'un employé (utilisée par EmployesView)
        // ----------------------------------------------------------------
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
