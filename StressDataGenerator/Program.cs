using System.Globalization;
using System.IO;
using GestionCoutureApp.Data;
using GestionCoutureApp.Helpers;
using GestionCoutureApp.Models;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.StressDataGenerator;

/// <summary>
/// Générateur de données de stress paramétrable pour GestionCoutureApp.
///
/// Produit une distribution réaliste ET des cas extrêmes (dizaines de milliers
/// de retards, pièces terminées, RDV exceptionnels, clients homonymes) pour
/// tester les limites de performance de chaque écran.
///
/// Usage :
///   dotnet run --project StressDataGenerator -- --clients 2000000 --pieces 3000000 --output-dir bin\Stress2M
///   dotnet run --project StressDataGenerator -- --clients 120000 --pieces 180000 --output-dir bin\StressProfile-20261027
///
/// Volumes par défaut (stress intermédiaire, ~30s) :
///   120 000 clients, 120 000 commandes, 180 000 pièces, 90 000 paiements
/// </summary>
internal static class Program
{
    private static readonly Random _rng = new(42);

    private static readonly string[] Types =
        { "Pantalon", "Chemise", "Robe", "Boubou", "Veste", "Jupe", "Tailleur", "Pagne" };
    private static readonly string[] Modes = { "Especes", "Mobile Money", "Virement" };
    // Distribution des statuts de pièce → ~49 % en retard (stress profile)
    // 30 % "A faire", 20 % "En cours", 33 % "Terminee", 17 % "Livree"
    private static readonly string[] StatutPool = BuildStatutPool();

    private static string[] BuildStatutPool()
    {
        var pool = new List<string>(100);
        for (int i = 0; i < 30; i++) pool.Add("A faire");
        for (int i = 0; i < 20; i++) pool.Add("En cours");
        for (int i = 0; i < 33; i++) pool.Add("Terminee");
        for (int i = 0; i < 17; i++) pool.Add("Livree");
        return pool.ToArray();
    }

    private static async Task<int> Main(string[] args)
    {
        var opts = ParseArgs(args);

        Console.WriteLine($"╔══ Générateur de données de stress ═══════════════════════╗");
        Console.WriteLine($"║ Clients    : {opts.Clients,12:N0}                          ║");
        Console.WriteLine($"║ Commandes : {opts.Commandes,12:N0}                          ║");
        Console.WriteLine($"║ Pièces    : {opts.Pieces,12:N0}                          ║");
        Console.WriteLine($"║ Paiements  : {opts.Paiements,12:N0}                          ║");
        Console.WriteLine($"║ Retours    : {opts.Retours,12:N0}                          ║");
        Console.WriteLine($"║ Sortie     : {opts.OutputDir}                    ");
        Console.WriteLine($"╚══════════════════════════════════════════════════════════╝");

        Directory.CreateDirectory(opts.OutputDir);
        string dbPath = Path.Combine(opts.OutputDir, "gestion_couture.db");

        // Nettoyer l'ancienne base
        SqliteConnection.ClearAllPools();
        foreach (string suffix in new[] { "", "-wal", "-shm", "-journal" })
        {
            string f = dbPath + suffix;
            if (File.Exists(f)) File.Delete(f);
        }

        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseSqlite($"Data Source={dbPath}")
            .Options;

        using var ctx = new ApplicationDbContext(options);

        // PRAGMAs d'insertion rapide (sécurité moindre = acceptable pour un outil de génération)
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = MEMORY;");
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA synchronous = OFF;");
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA cache_size = -131072;"); // 128 Mo

        Console.WriteLine("Création du schéma...");
        await ctx.Database.EnsureCreatedAsync();
        // Marquer toutes les migrations comme appliquées (EnsureCreated crée le schéma final)
        await ctx.Database.ExecuteSqlRawAsync(
            "CREATE TABLE IF NOT EXISTS __EFMigrationsHistory (MigrationId TEXT NOT NULL PRIMARY KEY, ProductVersion TEXT NOT NULL);");
        const string productVersion = "8.0.11";
        foreach (string mid in ctx.Database.GetMigrations())
            await ctx.Database.ExecuteSqlInterpolatedAsync(
                $"INSERT OR IGNORE INTO __EFMigrationsHistory (MigrationId, ProductVersion) VALUES ({mid}, {productVersion});");

        var sw = System.Diagnostics.Stopwatch.StartNew();

        // ── Employés (1 Boss + N couturiers) ──────────────────────────────
        int nbCouturiers = Math.Max(150, Math.Min(2000, opts.Clients / 10_000));
        await GenererEmployes(ctx, nbCouturiers);
        Console.WriteLine($"  Employés : {nbCouturiers} ({sw.Elapsed.TotalSeconds:F1}s)");

        // ── Clients ───────────────────────────────────────────────────────
        await GenererClients(ctx, opts.Clients);
        Console.WriteLine($"  Clients : {opts.Clients:N0} ({sw.Elapsed.TotalSeconds:F1}s)");

        // ── Commandes + Pièces + Paiements + Matériels ────────────────────
        await GenererCommandesEtPieces(ctx, opts.Commandes, opts.Pieces, opts.Paiements, nbCouturiers);
        Console.WriteLine($"  Commandes/pièces/paiements ({sw.Elapsed.TotalSeconds:F1}s)");

        // ── Retours ───────────────────────────────────────────────────────
        await GenererRetours(ctx, opts.Retours, opts.Pieces);
        Console.WriteLine($"  Retours : {opts.Retours:N0} ({sw.Elapsed.TotalSeconds:F1}s)");

        // ── Suivis alerte retard (pour tester NOT EXISTS) ─────────────────
        await GenererSuivis(ctx, opts.Pieces);
        Console.WriteLine($"  Suivis alerte retard ({sw.Elapsed.TotalSeconds:F1}s)");

        sw.Stop();

        // ANALYZE pour que le query planner ait des statistiques
        Console.WriteLine("ANALYZE...");
        await ctx.Database.ExecuteSqlRawAsync("ANALYZE;");

        // Restaurer des PRAGMAs sûrs pour l'utilisation de la base par l'app
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL;");
        await ctx.Database.ExecuteSqlRawAsync("PRAGMA synchronous = NORMAL;");

        Console.WriteLine($"");
        Console.WriteLine($"✅ Génération terminée en {sw.Elapsed.TotalSeconds:F1}s");
        Console.WriteLine($"   Base : {dbPath}");
        Console.WriteLine($"   Pour lancer l'app sur cette base :");
        Console.WriteLine($"   $env:GESTIONCOUTUREAPP_DATA_DIR = '{Path.GetFullPath(opts.OutputDir)}'");
        Console.WriteLine($"   dotnet run --project GestionCoutureApp.csproj");

        return 0;
    }

    // ════════════════════════════════════════════════════════════════════
    // GÉNÉRATION
    // ════════════════════════════════════════════════════════════════════

    private static async Task GenererEmployes(ApplicationDbContext ctx, int nbCouturiers)
    {
        ctx.Employes.Add(new Employe
        {
            IdEmploye = 1, Nom = "DIALLO", Prenom = "Boss",
            Identifiant = "boss_stress", MotDePasse = "x",
            Role = "Boss", Statut = "Actif"
        });
        for (int i = 2; i <= 1 + nbCouturiers; i++)
        {
            ctx.Employes.Add(new Employe
            {
                IdEmploye = i, Nom = $"Couturier{i}", Prenom = $"Prenom{i}",
                Identifiant = $"cout{i}", MotDePasse = "x",
                Role = "Couturier", Statut = "Actif"
            });
        }
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;
        await ctx.SaveChangesAsync();
        ctx.ChangeTracker.Clear();
        ctx.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private static async Task GenererClients(ApplicationDbContext ctx, int total)
    {
        const int batch = 2000;
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;

        for (int lot = 0; lot < total; lot += batch)
        {
            int count = Math.Min(batch, total - lot);
            var list = new List<Client>(count);
            for (int i = 0; i < count; i++)
            {
                int id = lot + i + 1;
                // Cas extrême : clusters de noms similaires (recherche texte)
                string nom = (id % 1000 == 0) ? "Diop" : (id % 500 == 0) ? "Ndiaye" : $"NomClient{id}";
                string prenom = (id % 1000 == 0) ? "Mamadou" : (id % 500 == 0) ? "Awa" : $"Prenom{id}";
                list.Add(new Client
                {
                    IdClient = id, Nom = nom, Prenom = prenom,
                    Telephone = $"7{id:D8}"
                });
            }
            ctx.Clients.AddRange(list);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();

            if (lot % (batch * 10) == 0)
                Console.Write($"\r  Clients : {Math.Min(lot + batch, total):N0}/{total:N0}");
        }
        Console.WriteLine();
        ctx.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private static async Task GenererCommandesEtPieces(
        ApplicationDbContext ctx, int nbCommandes, int nbPieces, int nbPaiements, int nbCouturiers)
    {
        const int batch = 1000;
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;
        var now = DateTime.Now;
        int pieceId = 0;
        int paiementId = 0;
        int paiementsRestants = nbPaiements;
        int materiels = 0;

        for (int lot = 0; lot < nbCommandes; lot += batch)
        {
            int count = Math.Min(batch, nbCommandes - lot);
            var commandes = new List<Commande>(count);
            var pieces = new List<PieceCommande>();
            var paiements = new List<Paiement>();
            var materiaux = new List<MaterielSupplement>();

            for (int i = 0; i < count; i++)
            {
                int idCmd = lot + i + 1;
                int idClient = (idCmd % nbCommandes == 0 ? 1 : idCmd) % MaxClients(nbCommandes) + 1;
                if (idClient < 1) idClient = 1;
                int idCout = (idCmd % nbCouturiers) + 2;
                var statut = StatutPool[idCmd % StatutPool.Length];
                var dateDebut = now.AddDays(-(_rng.Next(1, 730)));
                // ~70 % des pièces actives ont DateFin dans le passé (retard)
                bool enRetard = statut is "A faire" or "En cours" && _rng.Next(100) < 70;
                var dateFin = enRetard
                    ? now.AddDays(-_rng.Next(1, 60))
                    : dateDebut.AddDays(_rng.Next(3, 21));

                var commande = new Commande
                {
                    IdCommande = idCmd,
                    IdClient = idClient,
                    DateDebut = dateDebut,
                    HeureDebut = TimeSpan.FromHours(8),
                    DateFin = dateFin,
                    Statut = statut,
                    TypeVetement = Types[idCmd % Types.Length],
                    MontantTotal = 0m,
                    EstSupprimee = false,
                    EstAnnulee = false,
                    IdOperateurCreation = 1,
                    NomOperateurCreation = "Boss",
                    DateCreation = dateDebut.ToUniversalTime()
                };
                commandes.Add(commande);

                // Répartir nbPieces sur les commandes (1,5 en moyenne : 2 pour 50 %)
                int nbPiecesCmd = (nbPieces > nbCommandes && idCmd % 2 == 0) ? 2 : 1;
                decimal montantCouture = 0m;
                for (int p = 0; p < nbPiecesCmd && pieceId < nbPieces; p++)
                {
                    pieceId++;
                    var pieceStatut = (p == 1 && statut == "A faire") ? "A faire" : statut;
                    decimal montantPiece = 2500m + (pieceId % 10) * 250m;
                    montantCouture += montantPiece;
                    // Cas extrême : 5 % des pièces ont un RendezVousException
                    DateTime? rdvExc = (pieceId % 20 == 0)
                        ? dateFin.AddDays(_rng.Next(1, 5)).AddHours(_rng.Next(8, 18))
                        : null;
                    pieces.Add(new PieceCommande
                    {
                        IdPieceCommande = pieceId,
                        IdCommande = idCmd,
                        TypeVetement = Types[pieceId % Types.Length],
                        MontantCouture = montantPiece,
                        Statut = pieceStatut,
                        IdCouturier = idCout,
                        DateTerminee = (pieceStatut == "Terminee" || pieceStatut == "Livree")
                            ? dateDebut.AddDays(2) : null,
                        RendezVousException = rdvExc,
                        DateRdvEffective = DateRdvHelper.CalculerDateRdvEffective(rdvExc, dateFin, null),
                        CommandeActive = true
                    });
                }
                commande.MontantTotal = montantCouture;

                // Matériel sur 10 % des commandes
                if (idCmd % 10 == 0)
                {
                    int qte = 1 + idCmd % 3;
                    decimal pu = 750m + idCmd % 5 * 250m;
                    materiels++;
                    materiaux.Add(new MaterielSupplement
                    {
                        IdMateriel = materiels,
                        IdCommande = idCmd,
                        Designation = "Tissu de confection",
                        Quantite = qte,
                        PrixUnitaire = pu,
                        IdOperateur = 1,
                        NomOperateur = "Boss DIALLO"
                    });
                    commande.MontantTotal += qte * pu;
                }

                // Paiements sur 75 % des commandes
                if (paiementsRestants > 0 && idCmd % 4 != 0)
                {
                    paiementId++;
                    paiementsRestants--;
                    decimal montant = commande.MontantTotal * (_rng.Next(3, 11) / 10m);
                    paiements.Add(new Paiement
                    {
                        IdPaiement = paiementId,
                        IdCommande = idCmd,
                        MontantPaye = montant,
                        DatePaiement = dateDebut.AddDays(1),
                        ModePaiement = Modes[idCmd % Modes.Length],
                        EstAnnule = paiementId % 10 == 0,
                        IdOperateur = 1,
                        NomOperateur = "Boss"
                    });
                }
            }

            ctx.Commandes.AddRange(commandes);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            ctx.PiecesCommande.AddRange(pieces);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
            if (materiaux.Count > 0)
            {
                ctx.MaterielsSupplements.AddRange(materiaux);
                await ctx.SaveChangesAsync();
                ctx.ChangeTracker.Clear();
            }
            if (paiements.Count > 0)
            {
                ctx.Paiements.AddRange(paiements);
                await ctx.SaveChangesAsync();
                ctx.ChangeTracker.Clear();
            }

            if (lot % (batch * 5) == 0)
                Console.Write($"\r  Commandes : {Math.Min(lot + batch, nbCommandes):N0}/{nbCommandes:N0}");
        }
        Console.WriteLine();
        ctx.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private static async Task GenererRetours(ApplicationDbContext ctx, int nbRetours, int nbPieces)
    {
        if (nbRetours <= 0) return;
        const int batch = 1000;
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;
        int annee = DateTime.Now.Year;
        int num = 0;

        for (int lot = 0; lot < nbRetours; lot += batch)
        {
            int count = Math.Min(batch, nbRetours - lot);
            var list = new List<Retour>(count);
            for (int i = 0; i < count; i++)
            {
                num++;
                int idRetour = lot + i + 1;
                // Retour rattaché à une pièce terminée/livrée
                int idPiece = (_rng.Next(1, nbPieces / 2 + 1) * 2);
                int idCmd = (int)Math.Ceiling((double)idPiece / 2);
                var dateSig = DateTime.Now.AddDays(-_rng.Next(1, 90));
                string statut = (idRetour % 5) switch
                {
                    0 => "Signale", 1 => "En reprise", 2 => "Termine", 3 => "Rendu", _ => "Pret"
                };
                list.Add(new Retour
                {
                    IdRetour = idRetour,
                    IdCommande = idCmd,
                    IdPieceCommande = idPiece,
                    NumeroRetour = $"RET-{annee}-{num:D5}",
                    Statut = statut,
                    DateSignalement = dateSig,
                    DescriptionProbleme = "Défaut de couture",
                    EstAnnule = false,
                    IdCouturier = (idCmd % 150) + 2,
                    // Cas extrême : 30 % des retours ont un DateRdvReprise
                    DateRdvReprise = idRetour % 3 == 0
                        ? DateTime.Now.AddDays(_rng.Next(-5, 10)) : null,
                    DateResolution = statut is "Termine" or "Rendu"
                        ? dateSig.AddDays(_rng.Next(1, 14)) : null
                });
            }
            ctx.Retours.AddRange(list);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
        }
        ctx.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private static async Task GenererSuivis(ApplicationDbContext ctx, int nbPieces)
    {
        // Génère ~5 % de suivis pour tester l'exclusion NOT EXISTS
        int nbSuivis = nbPieces / 20;
        if (nbSuivis <= 0) return;
        const int batch = 1000;
        ctx.ChangeTracker.AutoDetectChangesEnabled = false;

        for (int lot = 0; lot < nbSuivis; lot += batch)
        {
            int count = Math.Min(batch, nbSuivis - lot);
            var list = new List<SuiviAlerteRetard>(count);
            for (int i = 0; i < count; i++)
            {
                int n = lot + i + 1;
                string type = n % 3 == 0 ? "Piece" : "Commande";
                int idCible = n % 3 == 0 ? (n * 3) : n;
                list.Add(new SuiviAlerteRetard
                {
                    TypeCible = type,
                    IdCible = idCible,
                    IdClient = (n % 1000) + 1,
                    DateRdvTraite = DateTime.Now.AddDays(-_rng.Next(1, 30)),
                    NbPreventions = 1 + (n % 3),
                    NbReports = n % 2,
                    DateDernierMessage = DateTime.Now.AddDays(-_rng.Next(1, 15))
                });
            }
            ctx.SuivisAlerteRetard.AddRange(list);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
        }
        ctx.ChangeTracker.AutoDetectChangesEnabled = true;
    }

    private static int MaxClients(int nbCommandes) => Math.Max(nbCommandes, 1);

    // ════════════════════════════════════════════════════════════════════
    // PARSING
    // ════════════════════════════════════════════════════════════════════

    private sealed class Options
    {
        public int Clients { get; set; } = 120_000;
        public int Commandes { get; set; } = 120_000;
        public int Pieces { get; set; } = 180_000;
        public int Paiements { get; set; } = 90_000;
        public int Retours { get; set; } = 10_000;
        public string OutputDir { get; set; } = "bin\\StressGenerated";
    }

    private static Options ParseArgs(string[] args)
    {
        var opts = new Options();
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--clients":    if (i + 1 < args.Length) opts.Clients = ParseInt(args[++i]); break;
                case "--commandes":  if (i + 1 < args.Length) opts.Commandes = ParseInt(args[++i]); break;
                case "--pieces":     if (i + 1 < args.Length) opts.Pieces = ParseInt(args[++i]); break;
                case "--paiements":  if (i + 1 < args.Length) opts.Paiements = ParseInt(args[++i]); break;
                case "--retours":    if (i + 1 < args.Length) opts.Retours = ParseInt(args[++i]); break;
                case "--output-dir": if (i + 1 < args.Length) opts.OutputDir = args[++i]; break;
                case "--help":
                case "-h":
                    Console.WriteLine("Usage: StressDataGenerator --clients N --commandes N --pieces N --paiements N --retours N --output-dir PATH");
                    Environment.Exit(0);
                    break;
            }
        }
        return opts;
    }

    private static int ParseInt(string s) => int.Parse(s, CultureInfo.InvariantCulture);
}
