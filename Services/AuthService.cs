using System.Security.Cryptography;
using System.Text;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class AuthService : IAuthService
    {
        private readonly ApplicationDbContext _context;

        public Employe? UtilisateurConnecte { get; private set; }

        public AuthService(ApplicationDbContext context)
        {
            _context = context;
        }

        public string HasherMotDePasse(string motDePasse)
        {
            using SHA256 sha = SHA256.Create();
            byte[] bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(motDePasse));
            var builder = new StringBuilder();
            foreach (byte b in bytes)
                builder.Append(b.ToString("x2"));
            return builder.ToString();
        }

        public Employe? Authentifier(string identifiant, string motDePasse)
        {
            string hash = HasherMotDePasse(motDePasse);

            var employe = _context.Employes.FirstOrDefault(
                e => e.Identifiant == identifiant && e.MotDePasse == hash
            );

            if (employe != null && employe.Statut == "Actif")
            {
                UtilisateurConnecte = employe;
                return employe;
            }

            return null;
        }
    }
}
