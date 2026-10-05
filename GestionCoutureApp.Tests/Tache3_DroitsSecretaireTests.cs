using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 3 — Droits de la Secrétaire basés sur l'état de la commande
    //
    // Règles :
    //   3A : Secrétaire peut TOUT modifier tant qu'aucun paiement non annulé
    //   3B : Après paiement → montants, type, client verrouillés pour Secrétaire
    //        (date, couturier, notes restent modifiables)
    //   3C : Après Terminee/Livree → lecture seule pour la Secrétaire
    //   3D : Secrétaire peut supprimer si aucun paiement + statut initial + motif
    //   3E : Boss garde tous les droits toujours
    // ====================================================================

    [TestFixture]
    public class Tache3_DroitsSecretaireTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private CommandeService _commandeService = null!;
        private PaiementService _paiementService = null!;

        private const int IdBoss       = 1;
        private const int IdSecretaire = 2;
        private const int IdCouturier  = 3;
        private const int IdClient     = 1;

        [SetUp]
        public void SetUp()
        {
            CommandeService.ViderCacheDoublonPourTests();
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"T3_{Guid.NewGuid()}")
                .Options;
            _factory = new T3DbContextFactory(options);
            var log = NullLoggerFactory.Instance;
            _commandeService = new CommandeService(_factory, new Logger<CommandeService>(log));
            _paiementService = new PaiementService(_factory, new Logger<PaiementService>(log));

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,       Nom = "DIALLO", Prenom = "Mamadou",
                              Identifiant = "boss",  MotDePasse = "x", Role = "Boss",       Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire, Nom = "FALL",   Prenom = "Marie",
                              Identifiant = "sec",   MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier,  Nom = "CISSE",  Prenom = "Issa",
                              Identifiant = "cout",  MotDePasse = "x", Role = "Couturier",  Statut = "Actif" }
            );
            ctx.Clients.Add(new Client { IdClient = IdClient, Nom = "SARR", Prenom = "Fatou", Telephone = "771234567" });
            ctx.SaveChanges();
        }

        // ── 3A : Sans paiement → Secrétaire peut tout ────────────────────

        [Test]
        public void T3_01_Secretaire_Peut_Tout_Modifier_Sans_Paiement()
        {
            var (idCommande, piece) = CreerCommandePiece();

            // Modifier couturier, statut, prix, type, date
            var cmdModif = new Commande { IdCommande = idCommande, IdClient = IdClient,
                DateFin = DateTime.Now.AddDays(10), HeureDebut = TimeSpan.Zero };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = "Robe",     // changement type OK
                MontantCouture  = 8000m,      // changement prix OK
                IdCouturier     = IdCouturier,
                Statut          = "En cours"
            };

            Assert.DoesNotThrow(() =>
                _commandeService.Modifier(cmdModif, pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"),
                "Sans paiement, la Secrétaire peut tout modifier");

            var enBase = PieceEnBase(piece.IdPieceCommande);
            Assert.That(enBase.TypeVetement, Is.EqualTo("Robe"));
            Assert.That(enBase.MontantCouture, Is.EqualTo(8000m));
        }

        // ── 3B : Après paiement → verrous ────────────────────────────────

        [Test]
        public void T3_02_Secretaire_Bloquee_Prix_Apres_Paiement()
        {
            var (idCommande, piece) = CreerCommandePiece();
            Payer(idCommande, 3000m);

            var pieceModif = Formulaire(piece, montant: 6000m); // prix différent

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ModifierPiece(pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("Boss").Or.Contain("paiement").IgnoreCase);
        }

        [Test]
        public void T3_03_Secretaire_Peut_Modifier_Couturier_Apres_Paiement()
        {
            var (idCommande, piece) = CreerCommandePiece();
            Payer(idCommande, 3000m);

            var pieceModif = Formulaire(piece, idCouturier: null); // changer couturier

            Assert.DoesNotThrow(() =>
                _commandeService.ModifierPiece(pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"),
                "La Secrétaire peut modifier le couturier même après un paiement");

            Assert.That(PieceEnBase(piece.IdPieceCommande).IdCouturier, Is.Null);
        }

        [Test]
        public void T3_04_Secretaire_Peut_Modifier_Date_Apres_Paiement()
        {
            var (idCommande, piece) = CreerCommandePiece();
            Payer(idCommande, 3000m);
            var nouvelleDate = DateTime.Now.AddDays(15);

            var cmdModif = new Commande { IdCommande = idCommande, IdClient = IdClient,
                DateFin = nouvelleDate, HeureDebut = TimeSpan.FromHours(10) };
            var pieceModif = Formulaire(piece); // prix/type inchangés

            Assert.DoesNotThrow(() =>
                _commandeService.Modifier(cmdModif, pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"),
                "La Secrétaire peut modifier la date après un paiement");

            using var ctx = _factory.CreateDbContext();
            Assert.That(ctx.Commandes.Find(idCommande)!.DateFin.Date, Is.EqualTo(nouvelleDate.Date));
        }

        // ── 3C : Après Terminee → lecture seule Secrétaire ───────────────

        [Test]
        public void T3_05_Secretaire_Lecture_Seule_Apres_Terminee()
        {
            var (_, piece) = CreerCommandePiece();
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            var pieceModif = Formulaire(piece, idCouturier: null);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ModifierPiece(pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("terminée").Or.Contain("Terminee")
                .Or.Contain("livrée").Or.Contain("Boss"));
        }

        [Test]
        public void T3_06_Boss_Peut_Modifier_Apres_Terminee()
        {
            var (_, piece) = CreerCommandePiece();
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            var pieceModif = Formulaire(piece, idCouturier: null);

            Assert.DoesNotThrow(() =>
                _commandeService.ModifierPiece(pieceModif, new List<Mesure>(),
                    IdBoss, "Mamadou DIALLO"),
                "Le Boss peut toujours modifier même après Terminee");
        }

        // ── 3D : Suppression par la Secrétaire ───────────────────────────

        [Test]
        public async Task T3_07_Secretaire_Peut_Supprimer_Sans_Paiement_Statut_Initial()
        {
            var (idCommande, _) = CreerCommandePiece();

            Assert.DoesNotThrowAsync(async () =>
                await _commandeService.SupprimerAsync(
                    idCommande, IdSecretaire, "Marie FALL",
                    "Erreur de saisie"));

            using var ctx = _factory.CreateDbContext();
            Assert.That(ctx.Commandes.Find(idCommande)!.EstSupprimee, Is.True);
        }

        [Test]
        public async Task T3_08_Secretaire_Ne_Peut_Pas_Supprimer_Avec_Paiement()
        {
            var (idCommande, _) = CreerCommandePiece();
            Payer(idCommande, 2000m);

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.SupprimerAsync(
                    idCommande, IdSecretaire, "Marie FALL", "Test"));
            Assert.That(ex!.Message, Does.Contain("paiement").IgnoreCase);
        }

        [Test]
        public async Task T3_09_Secretaire_Ne_Peut_Pas_Supprimer_Statut_Avance()
        {
            var (idCommande, piece) = CreerCommandePiece();
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "En cours", IdBoss, "Mamadou DIALLO");

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.SupprimerAsync(
                    idCommande, IdSecretaire, "Marie FALL", "Test"));
            Assert.That(ex!.Message, Does.Contain("initial").Or.Contain("faire").IgnoreCase
                .Or.Contain("Boss"));
        }

        [Test]
        public async Task T3_10_Suppression_Sans_Motif_Refusee()
        {
            var (idCommande, _) = CreerCommandePiece();

            var ex = Assert.ThrowsAsync<InvalidOperationException>(async () =>
                await _commandeService.SupprimerAsync(
                    idCommande, IdBoss, "Mamadou DIALLO", "   "));
            Assert.That(ex!.Message, Does.Contain("motif").IgnoreCase.Or.Contain("obligatoire").IgnoreCase);
        }

        // ── 3E : Boss garde tous les droits ──────────────────────────────

        [Test]
        public void T3_11_Boss_Peut_Modifier_Prix_Apres_Paiement()
        {
            var (idCommande, piece) = CreerCommandePiece();
            Payer(idCommande, 3000m);

            var pieceModif = Formulaire(piece, montant: 6000m);

            Assert.DoesNotThrow(() =>
                _commandeService.ModifierPiece(pieceModif, new List<Mesure>(),
                    IdBoss, "Mamadou DIALLO"),
                "Le Boss peut toujours modifier le prix même après un paiement");
            Assert.That(PieceEnBase(piece.IdPieceCommande).MontantCouture, Is.EqualTo(6000m));
        }

        [Test]
        public void T3_12_Couturier_Acces_Refuse()
        {
            var (idCommande, piece) = CreerCommandePiece();
            var pieceModif = Formulaire(piece);

            Assert.Throws<UnauthorizedAccessException>(() =>
                _commandeService.ModifierPiece(pieceModif, new List<Mesure>(),
                    IdCouturier, "Issa CISSE"));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private (int idCommande, PieceCommande piece) CreerCommandePiece()
        {
            var cmd  = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var pce  = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 10000m,
                                           IdCouturier = IdCouturier };
            _commandeService.Ajouter(cmd, pce, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
            // Objet formulaire détaché
            var form = new PieceCommande
            {
                IdPieceCommande = pce.IdPieceCommande, TypeVetement = pce.TypeVetement,
                MontantCouture  = pce.MontantCouture,  IdCouturier  = pce.IdCouturier,
                Statut          = "A faire"
            };
            return (cmd.IdCommande, form);
        }

        private PieceCommande Formulaire(PieceCommande src, decimal? montant = null,
            string? type = null, int? idCouturier = -1, string? statut = null)
        {
            return new PieceCommande
            {
                IdPieceCommande = src.IdPieceCommande,
                TypeVetement    = type ?? src.TypeVetement,
                MontantCouture  = montant ?? src.MontantCouture,
                IdCouturier     = idCouturier == -1 ? src.IdCouturier : idCouturier,
                Statut          = statut ?? src.Statut
            };
        }

        private void Payer(int idCommande, decimal montant) =>
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = idCommande, MontantPaye = montant,
                DatePaiement = DateTime.Now, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");

        private PieceCommande PieceEnBase(int id)
        {
            using var ctx = _factory.CreateDbContext();
            return ctx.PiecesCommande.Single(p => p.IdPieceCommande == id);
        }
    }

    internal class T3DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public T3DbContextFactory(DbContextOptions<ApplicationDbContext> options)
            => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
