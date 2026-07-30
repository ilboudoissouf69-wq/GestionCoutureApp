using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Data
{
    /// <summary>
    /// Contexte de base de données SQLite pour l'application.
    /// </summary>
    public class ApplicationDbContext : DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        // ====== DbSets (tables de la base) ======
        public DbSet<Client> Clients { get; set; }
        public DbSet<Commande> Commandes { get; set; }
        public DbSet<Mesure> Mesures { get; set; }
        public DbSet<Paiement> Paiements { get; set; }
        public DbSet<Employe> Employes { get; set; }
        // NOUVEAU Étape 6
        public DbSet<TypeVetement> TypesVetements { get; set; }
        public DbSet<MesureRequise> MesuresRequises { get; set; }

        public DbSet<DescriptionCourante> DescriptionsCourantes { get; set; }
        public DbSet<Commission> Commissions { get; set; }

        // NOUVEAU Étape 1a (Point 1 — Commandes multi-pièces)
        public DbSet<PieceCommande> PiecesCommande { get; set; }

        public DbSet<Parametre> Parametres { get; set; }

        // Point 4 — Retours (reprises gratuites)
        public DbSet<Retour> Retours { get; set; }


        // Point 3 — Depenses
        public DbSet<Depense> Depenses { get; set; }

        // Point 2 - Matériaux supplémentaires
        public DbSet<MaterielSupplement> MaterielsSupplements { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {

            // Point 2 - Relation PieceCommande -> MaterielSupplements (Restrict)
            // Pas cascade ici car MaterielSupplement a aussi une FK Commande
            // déjà en cascade. EF Core refuse les chemins de cascade multiples.
            modelBuilder.Entity<PieceCommande>()
                .HasMany(p => p.MaterielSupplements)
                .WithOne(m => m.PieceCommande)
                .HasForeignKey(m => m.IdPieceCommande)
                .OnDelete(DeleteBehavior.Restrict)
                .IsRequired(false);

            // Point 2 - Relation Commande -> MaterielSupplements (cascade)
            modelBuilder.Entity<Commande>()
                .HasMany(c => c.MaterielSupplements)
                .WithOne(m => m.Commande)
                .HasForeignKey(m => m.IdCommande)
                .OnDelete(DeleteBehavior.Cascade);
            base.OnModelCreating(modelBuilder);

            // Securite/robustesse : un identifiant de connexion ne peut exister
            // qu'une seule fois, applique au niveau de la base (et plus
            // seulement verifie cote application, qui reste sujet a une
            // course en cas d'acces quasi simultane).
            modelBuilder.Entity<Employe>()
                .HasIndex(e => e.Identifiant)
                .IsUnique();

            // Relation Commission -> Commandes : Restrict (jamais de cascade).
            // Une commande "verrouillee" par une commission ne doit jamais
            // disparaitre silencieusement si la commission est modifiee.
            modelBuilder.Entity<Commission>()
                .HasMany(co => co.Commandes)
                .WithOne(c => c.Commission)
                .HasForeignKey(c => c.IdCommission)
                .OnDelete(DeleteBehavior.Restrict);

            // Relation Client -> Commandes : Restrict (et NON cascade, qui est le
            // comportement par defaut d'EF Core pour une FK obligatoire non-nullable).
            // Sans cette ligne, supprimer un client efface silencieusement tout son
            // historique de commandes (ou plante si des paiements y sont rattaches,
            // via la contrainte Restrict sur Commande->Paiements). Voir ClientService.Supprimer.
            modelBuilder.Entity<Client>()
                .HasMany(cl => cl.Commandes)
                .WithOne(c => c.Client)
                .HasForeignKey(c => c.IdClient)
                .OnDelete(DeleteBehavior.Restrict);

            // Relation Commande -> Mesures (cascade)
            modelBuilder.Entity<Commande>()
                .HasMany(c => c.Mesures)
                .WithOne(m => m.Commande)
                .HasForeignKey(m => m.IdCommande)
                .OnDelete(DeleteBehavior.Cascade);

            // Relation Commande -> Paiements : Restrict (et NON Cascade).
            // Le système de paiement est conçu pour ne JAMAIS supprimer un paiement
            // (annulation avec motif obligatoire, traçabilité complète — voir PaiementService).
            // Un cascade delete sur la commande contournerait cette règle en effaçant
            // silencieusement tout l'historique financier. On bloque donc la suppression
            // d'une commande tant qu'elle a des paiements (voir CommandeService.Supprimer).
            modelBuilder.Entity<Commande>()
                .HasMany(c => c.Paiements)
                .WithOne(p => p.Commande)
                .HasForeignKey(p => p.IdCommande)
                .OnDelete(DeleteBehavior.Restrict);

            // NOUVEAU : Relation TypeVetement -> MesuresRequises (cascade)
            modelBuilder.Entity<TypeVetement>()
                .HasMany(t => t.MesuresRequises)
                .WithOne(m => m.TypeVetement)
                .HasForeignKey(m => m.IdTypeVetement)
                .OnDelete(DeleteBehavior.Cascade);

            // ============================================================
            // NOUVEAU Étape 1a (Point 1 — Commandes multi-pièces)
            // ============================================================

            // Relation Commande -> Pieces (cascade) : une pièce n'a strictement
            // aucun sens sans sa commande parente (contrairement aux paiements,
            // qui doivent survivre même si on voulait supprimer la commande —
            // d'ailleurs interdit par CommandeService.Supprimer tant qu'il y a
            // des paiements). Supprimer une commande doit donc pouvoir
            // supprimer ses pièces en cascade, SANS pour autant supprimer la
            // commande elle-même si des paiements existent (cette garde reste
            // gérée au niveau service, pas au niveau de la base).
            modelBuilder.Entity<Commande>()
                .HasMany(c => c.Pieces)
                .WithOne(p => p.Commande)
                .HasForeignKey(p => p.IdCommande)
                .OnDelete(DeleteBehavior.Cascade);

            // Relation PieceCommande -> Mesures (cascade) — même logique que
            // Commande -> Mesures aujourd'hui : les mesures d'une pièce n'ont
            // pas de sens indépendamment de cette pièce. Optionnelle pour
            // l'instant (IsRequired(false)) car IdPieceCommande est nullable
            // tant que l'Étape 1b n'a pas basculé la création des mesures
            // vers les pièces plutôt que vers la commande entière.
            modelBuilder.Entity<PieceCommande>()
                .HasMany(p => p.Mesures)
                .WithOne(m => m.PieceCommande)
                .HasForeignKey(m => m.IdPieceCommande)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired(false);

            // Relation Commission -> Pieces : Restrict, exactement comme pour
            // Commission -> Commandes aujourd'hui. Une pièce "verrouillée" par
            // une commission déjà calculée ne doit jamais disparaître
            // silencieusement si la commission est modifiée/annulée.
            modelBuilder.Entity<Commission>()
                .HasMany(co => co.Pieces)
                .WithOne(p => p.Commission)
                .HasForeignKey(p => p.IdCommission)
                .OnDelete(DeleteBehavior.Restrict);

            // Valeur par défaut pour le statut d'un employé
            modelBuilder.Entity<Employe>()
                .Property(e => e.Statut)
                .HasDefaultValue("Actif");
        }
    }
}