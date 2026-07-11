// Les "using" importent des boîtes à outils. Sans eux, C# ne comprendrait pas "DbContext" ou "DbSet".
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Models; // Permet d'utiliser nos modèles (Employe, Client, Commande) définis dans l'autre dossier

namespace GestionCoutureApp.Data
{
    // "public" : la classe est visible partout dans le projet.
    // ": DbContext" : cela signifie que notre classe hérite des super-pouvoirs d'Entity Framework pour gérer les bases de données.
    public class ApplicationDbContext : DbContext
    {
        // Les "DbSet" représentent les futures tables dans ton fichier SQLite.
        // C# va transformer la liste "Employes" en une table SQL nommée "Employes".
        public DbSet<Employe> Employes { get; set; }
        public DbSet<Client> Clients { get; set; }
        public DbSet<Commande> Commandes { get; set; }

        // Cette méthode configurationnelle s'exécute automatiquement pour connecter l'application au fichier physique.
        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // On indique à Entity Framework qu'on utilise SQLite et que notre fichier s'appellera "couture.db"
            optionsBuilder.UseSqlite("Data Source=couture.db");
        }
    }
}