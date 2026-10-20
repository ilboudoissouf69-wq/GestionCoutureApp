using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // ÉTAPE 4 — Mesures réutilisables (MesureClient)
    //
    //   MC_01 : ObtenirMesuresPourType retourne vide si pas de fiche
    //   MC_02 : UpsertMesures crée les mesures si elles n'existent pas
    //   MC_03 : UpsertMesures met à jour si elles existent
    //   MC_04 : Pré-remplissage : MesureClient est indépendant des Mesures de pièce
    //   MC_05 : Copie figée — modifier MesureClient ne change pas les Mesures de pièce
    //   MC_06 : Migration — UpsertMesures depuis la pièce la plus récente
    //   MC_07 : ObtenirFicheMesures groupe par type de vêtement
    //   MC_08 : LabelDate formaté selon DateMiseAJour
    //   MC_09 : SupprimerMesuresPourType vide le groupe
    //   MC_10 : Index unique — pas de doublon (IdClient, TypeVetement, NomMesure)
    // ====================================================================

    [TestFixture]
    public class Etape4_MesuresReutilisablesTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private MesureClientService _service = null!;
        private CommandeService _commandeService = null!;

        private const int IdClient1 = 10;
        private const int IdClient2 = 20;
        private const int IdOp      = 1;

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();

            var opts = TestDbHelper.CreerOptions("MC");
            _factory        = new E4DbContextFactory(opts);
            _service        = new MesureClientService(_factory, NullLogger<MesureClientService>.Instance);
            _commandeService = new CommandeService(_factory, NullLogger<CommandeService>.Instance,
                TestDbHelper.NoOpAudit);

            using var ctx = _factory.CreateDbContext();
            ctx.Clients.AddRange(
                new Client { IdClient = IdClient1, Nom = "DIALLO", Prenom = "Fatoumata", Telephone = "771111111" },
                new Client { IdClient = IdClient2, Nom = "FALL",   Prenom = "Oumar",     Telephone = "772222222" }
            );
            ctx.Employes.Add(new Employe
            {
                IdEmploye = IdOp, Nom = "BOSS", Prenom = "Admin",
                Identifiant = "boss", MotDePasse = "x", Role = "Boss", Statut = "Actif"
            });
            ctx.SaveChanges();
        }

        // ── MC_01 ────────────────────────────────────────────────────────
        [Test]
        public void MC_01_ObtenirMesuresPourType_Vide_Si_Pas_De_Fiche()
        {
            var result = _service.ObtenirMesuresPourType(IdClient1, "Pantalon");
            Assert.That(result, Is.Empty,
                "Aucune mesure ne doit exister pour un client sans fiche");
        }

        // ── MC_02 ────────────────────────────────────────────────────────
        [Test]
        public void MC_02_UpsertMesures_Cree_Si_Inexistantes()
        {
            _service.UpsertMesures(IdClient1, "Pantalon", new[]
            {
                ("Tour de taille", "86"),
                ("Longueur",       "105")
            }, IdOp, "Admin BOSS");

            var mesures = _service.ObtenirMesuresPourType(IdClient1, "Pantalon");

            Assert.That(mesures.Count, Is.EqualTo(2));
            Assert.That(mesures.Any(m => m.NomMesure == "Tour de taille" && m.Valeur == "86"), Is.True);
            Assert.That(mesures.Any(m => m.NomMesure == "Longueur"       && m.Valeur == "105"), Is.True);
        }

        // ── MC_03 ────────────────────────────────────────────────────────
        [Test]
        public void MC_03_UpsertMesures_MetsAJour_Si_Existantes()
        {
            // Créer une mesure initiale
            _service.UpsertMesures(IdClient1, "Chemise", new[] { ("Tour de poitrine", "92") },
                IdOp, "Admin BOSS");

            // Mettre à jour avec une nouvelle valeur
            _service.UpsertMesures(IdClient1, "Chemise", new[] { ("Tour de poitrine", "95") },
                IdOp, "Admin BOSS");

            var mesures = _service.ObtenirMesuresPourType(IdClient1, "Chemise");
            Assert.That(mesures.Count, Is.EqualTo(1),
                "Pas de doublon — upsert doit remplacer, pas dupliquer");
            Assert.That(mesures[0].Valeur, Is.EqualTo("95"),
                "La valeur doit être mise à jour");
        }

        // ── MC_04 ────────────────────────────────────────────────────────
        [Test]
        public void MC_04_MesureClient_Independante_Des_Mesures_De_Piece()
        {
            // Créer une commande avec des mesures dans la pièce
            var cmd  = new Commande { IdClient = IdClient1, DateFin = DateTime.Now.AddDays(7) };
            var pce  = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 5000m };
            var mesuresPiece = new List<Mesure>
            {
                new Mesure { NomMesure = "Tour de taille", Valeur = "84" },
                new Mesure { NomMesure = "Longueur",       Valeur = "102" }
            };
            _commandeService.Ajouter(cmd, pce, mesuresPiece, IdOp, "Admin BOSS");

            // La fiche MesureClient est vide — la commande ne l'alimente PAS automatiquement
            var ficheMesures = _service.ObtenirMesuresPourType(IdClient1, "Pantalon");
            Assert.That(ficheMesures, Is.Empty,
                "La création d'une commande ne doit pas remplir MesureClient automatiquement — " +
                "c'est l'opérateur qui choisit de mettre à jour la fiche.");

            // Maintenant on alimente la fiche manuellement (simulation de l'UI)
            _service.UpsertMesures(IdClient1, "Pantalon",
                mesuresPiece.Select(m => (m.NomMesure, m.Valeur)),
                IdOp, "Admin BOSS");

            ficheMesures = _service.ObtenirMesuresPourType(IdClient1, "Pantalon");
            Assert.That(ficheMesures.Count, Is.EqualTo(2));
        }

        // ── MC_05 ────────────────────────────────────────────────────────
        [Test]
        public void MC_05_Copie_Figee_Piece_Non_Modifiee_Par_UpsertMesures()
        {
            // 1. Créer une commande avec mesures
            var cmd  = new Commande { IdClient = IdClient1, DateFin = DateTime.Now.AddDays(7) };
            var pce  = new PieceCommande { TypeVetement = "Robe", MontantCouture = 8000m };
            var mesuresPiece = new List<Mesure>
            {
                new Mesure { NomMesure = "Tour de poitrine", Valeur = "90" },
                new Mesure { NomMesure = "Longueur",         Valeur = "120" }
            };
            _commandeService.Ajouter(cmd, pce, mesuresPiece, IdOp, "Admin BOSS");

            // 2. Alimenter la fiche client
            _service.UpsertMesures(IdClient1, "Robe",
                mesuresPiece.Select(m => (m.NomMesure, m.Valeur)),
                IdOp, "Admin BOSS");

            // 3. Modifier la fiche client (nouvelles mesures)
            _service.UpsertMesures(IdClient1, "Robe", new[]
            {
                ("Tour de poitrine", "94"),
                ("Longueur",         "118")
            }, IdOp, "Admin BOSS");

            // 4. Les mesures de la pièce existante DOIVENT rester inchangées
            using var ctx = _factory.CreateDbContext();
            var mesuresPieceEnBase = ctx.Mesures
                .Where(m => m.IdPieceCommande == pce.IdPieceCommande)
                .ToList();

            Assert.That(mesuresPieceEnBase.Count, Is.EqualTo(2));
            Assert.That(mesuresPieceEnBase.Any(m => m.NomMesure == "Tour de poitrine" && m.Valeur == "90"),
                Is.True, "La copie figée de la pièce ne doit PAS être modifiée par UpsertMesures");
            Assert.That(mesuresPieceEnBase.Any(m => m.NomMesure == "Longueur" && m.Valeur == "120"),
                Is.True, "La copie figée de la pièce ne doit PAS être modifiée par UpsertMesures");

            // 5. Et la fiche client est bien à jour
            var ficheMesures = _service.ObtenirMesuresPourType(IdClient1, "Robe");
            Assert.That(ficheMesures.Any(m => m.NomMesure == "Tour de poitrine" && m.Valeur == "94"),
                Is.True, "La fiche client doit être mise à jour");
        }

        // ── MC_06 ────────────────────────────────────────────────────────
        [Test]
        public void MC_06_Migration_Depuis_Piece_Plus_Recente()
        {
            // Simuler la migration : pour chaque (client, type), prendre les mesures
            // de la pièce la plus récente (logique de App.cs / AppliquerMigrationsManquantes)

            // Commande ancienne
            var cmdAncienne = new Commande { IdClient = IdClient1, DateFin = DateTime.Now.AddDays(-10) };
            var pceAncienne = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 5000m };
            _commandeService.Ajouter(cmdAncienne, pceAncienne, new List<Mesure>
            {
                new Mesure { NomMesure = "Tour de taille", Valeur = "80" }
            }, IdOp, "Admin BOSS");

            // Commande récente (après)
            var cmdRecente = new Commande { IdClient = IdClient1, DateFin = DateTime.Now.AddDays(7) };
            var pceRecente = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 5500m };
            _commandeService.Ajouter(cmdRecente, pceRecente, new List<Mesure>
            {
                new Mesure { NomMesure = "Tour de taille", Valeur = "84" }
            }, IdOp, "Admin BOSS");

            // Simuler la migration : récupérer la pièce la plus récente pour ce (client, type)
            using var ctx = _factory.CreateDbContext();
            var piecesClient = ctx.PiecesCommande
                .Include(p => p.Mesures)
                .Include(p => p.Commande)
                .Where(p => p.Commande!.IdClient == IdClient1
                         && p.TypeVetement == "Pantalon"
                         && !p.Commande.EstSupprimee)
                .OrderByDescending(p => p.Commande!.DateCreation)
                .ThenByDescending(p => p.IdPieceCommande)
                .ToList();

            Assert.That(piecesClient.Count, Is.EqualTo(2), "Deux pièces Pantalon pour ce client");

            // La pièce la plus récente est pceRecente (IdPieceCommande plus grand)
            var pieceSource = piecesClient.First();
            Assert.That(pieceSource.IdPieceCommande, Is.EqualTo(pceRecente.IdPieceCommande),
                "La pièce la plus récente doit être sélectionnée pour la migration");

            // Alimenter la fiche depuis la pièce la plus récente (comme le fait App.cs)
            _service.UpsertMesures(IdClient1, "Pantalon",
                pieceSource.Mesures.Select(m => (m.NomMesure, m.Valeur)),
                IdOp, "Migration automatique");

            var ficheMesures = _service.ObtenirMesuresPourType(IdClient1, "Pantalon");
            Assert.That(ficheMesures[0].Valeur, Is.EqualTo("84"),
                "La fiche doit contenir les mesures de la pièce la plus récente");
        }

        // ── MC_07 ────────────────────────────────────────────────────────
        [Test]
        public void MC_07_ObtenirFicheMesures_Groupe_Par_Type()
        {
            _service.UpsertMesures(IdClient1, "Pantalon", new[]
            {
                ("Tour de taille", "86"), ("Longueur", "105")
            }, IdOp, "Admin BOSS");

            _service.UpsertMesures(IdClient1, "Chemise", new[]
            {
                ("Tour de poitrine", "92")
            }, IdOp, "Admin BOSS");

            var fiches = _service.ObtenirFicheMesures(IdClient1);

            Assert.That(fiches.Count, Is.EqualTo(2), "Deux groupes : Pantalon et Chemise");
            Assert.That(fiches.Any(f => f.TypeVetement == "Pantalon"), Is.True);
            Assert.That(fiches.Any(f => f.TypeVetement == "Chemise"),  Is.True);

            var pantalon = fiches.First(f => f.TypeVetement == "Pantalon");
            Assert.That(pantalon.Mesures.Count, Is.EqualTo(2));

            // Client2 doit être indépendant
            var fichesClient2 = _service.ObtenirFicheMesures(IdClient2);
            Assert.That(fichesClient2, Is.Empty, "Les mesures ne doivent pas fuiter entre clients");
        }

        // ── MC_08 ────────────────────────────────────────────────────────
        [Test]
        public void MC_08_LabelDate_Formate_Correctement()
        {
            _service.UpsertMesures(IdClient1, "Robe", new[]
            {
                ("Tour de taille", "70")
            }, IdOp, "Admin BOSS");

            var fiches = _service.ObtenirFicheMesures(IdClient1);
            Assert.That(fiches.Count, Is.EqualTo(1));

            var robe = fiches[0];
            Assert.That(robe.DateDerniereMaj, Is.Not.Null);
            Assert.That(robe.LabelDate, Does.StartWith("Mesures du "),
                "Le label doit commencer par 'Mesures du '");
            Assert.That(robe.LabelDate, Does.Match(@"Mesures du \d{2}/\d{2}/\d{4}"),
                "Le label doit contenir une date au format dd/MM/yyyy");
        }

        // ── MC_09 ────────────────────────────────────────────────────────
        [Test]
        public void MC_09_SupprimerMesuresPourType_Vide_Le_Groupe()
        {
            _service.UpsertMesures(IdClient1, "Pantalon", new[]
            {
                ("Tour de taille", "86"), ("Longueur", "105")
            }, IdOp, "Admin BOSS");

            _service.UpsertMesures(IdClient1, "Chemise", new[]
            {
                ("Tour de poitrine", "92")
            }, IdOp, "Admin BOSS");

            // Supprimer Pantalon
            _service.SupprimerMesuresPourType(IdClient1, "Pantalon", IdOp, "Admin BOSS");

            var apresSuppr = _service.ObtenirFicheMesures(IdClient1);
            Assert.That(apresSuppr.Count, Is.EqualTo(1), "Il ne doit rester qu'un groupe (Chemise)");
            Assert.That(apresSuppr[0].TypeVetement, Is.EqualTo("Chemise"));

            var pantalon = _service.ObtenirMesuresPourType(IdClient1, "Pantalon");
            Assert.That(pantalon, Is.Empty, "Pantalon doit être vide après suppression");
        }

        // ── MC_10 ────────────────────────────────────────────────────────
        [Test]
        public void MC_10_Pas_De_Doublon_Index_Unique()
        {
            // Appeler deux fois UpsertMesure sur la même clé
            _service.UpsertMesure(IdClient1, "Pantalon", "Tour de taille", "84", IdOp, "Admin BOSS");
            _service.UpsertMesure(IdClient1, "Pantalon", "Tour de taille", "88", IdOp, "Admin BOSS");

            using var ctx = _factory.CreateDbContext();
            int count = ctx.MesuresClient.Count(mc =>
                mc.IdClient      == IdClient1 &&
                mc.TypeVetement  == "Pantalon" &&
                mc.NomMesure     == "Tour de taille");

            Assert.That(count, Is.EqualTo(1), "L'index unique doit empêcher les doublons");

            // Vérifier que c'est la dernière valeur qui est conservée
            var mesure = ctx.MesuresClient.Single(mc =>
                mc.IdClient == IdClient1 && mc.TypeVetement == "Pantalon");
            Assert.That(mesure.Valeur, Is.EqualTo("88"), "La valeur la plus récente doit être conservée");
        }

        // ── Classe factory interne ────────────────────────────────────────
        private class E4DbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public E4DbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }
    }
}
