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
