using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 4 — Duplication pièce + suppression Secrétaire pièce vierge
    //
    //   T4_01 : DupliquerPiece crée bien une copie
    //   T4_02 : Secrétaire peut supprimer une pièce vierge (A faire, sans paiement)
    //   T4_03 : Secrétaire bloquée si la pièce n'est plus "A faire"
    //   T4_04 : Secrétaire bloquée si paiement encaissé sur la commande
    //   T4_05 : Boss peut supprimer une pièce non vierge sans paiement
    //   T4_06 : Suppression de la dernière pièce refusée pour tous
    //   T4_07 : Pièce commissionnée non supprimable
    // ====================================================================

    [TestFixture]
    public class Tache4_DuplicationPieceTests
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
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase($"T4_{Guid.NewGuid()}").Options;
            _factory = new T4DbContextFactory(opts);
            var log  = NullLoggerFactory.Instance;
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

        [Test]
        public void T4_01_DupliquerPiece_Cree_Une_Copie()
        {
            var (idCommande, idPiece1) = CreerCommande2Pieces();
            var copie = _commandeService.DupliquerPiece(idPiece1);

            Assert.That(copie.IdPieceCommande, Is.Not.EqualTo(idPiece1));
            Assert.That(copie.IdCommande,      Is.EqualTo(idCommande));
            Assert.That(copie.Statut,          Is.EqualTo("A faire"));

            using var ctx = _factory.CreateDbContext();
            Assert.That(ctx.PiecesCommande.Count(p => p.IdCommande == idCommande), Is.EqualTo(3));
        }

        [Test]
        public void T4_02_Secretaire_Peut_Supprimer_Piece_Vierge()
        {
            var (_, idPiece2) = CreerCommande2Pieces();

            Assert.DoesNotThrow(() =>
                _commandeService.SupprimerPiece(idPiece2, IdSecretaire, "Marie FALL"),
                "Secrétaire peut supprimer une pièce À faire sans paiement");

            using var ctx = _factory.CreateDbContext();
            Assert.That(ctx.PiecesCommande.Any(p => p.IdPieceCommande == idPiece2), Is.False);
        }

        [Test]
        public void T4_03_Secretaire_Bloquee_Piece_En_Cours()
        {
            var (_, idPiece2) = CreerCommande2Pieces();
            _commandeService.ChangerStatutPiece(idPiece2, "En cours", IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerPiece(idPiece2, IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("À faire").Or.Contain("A faire")
                .Or.Contain("vierge").Or.Contain("Boss"));
        }

        [Test]
        public void T4_04_Secretaire_Bloquee_Si_Paiement()
        {
            var (idCommande, idPiece2) = CreerCommande2Pieces();
            Payer(idCommande, 3000m);

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerPiece(idPiece2, IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("paiement").IgnoreCase);
        }

        [Test]
        public void T4_05_Boss_Peut_Supprimer_Piece_Non_Vierge_Sans_Paiement()
        {
            var (_, idPiece2) = CreerCommande2Pieces();
            _commandeService.ChangerStatutPiece(idPiece2, "En cours", IdBoss, "Mamadou DIALLO");

            Assert.DoesNotThrow(() =>
                _commandeService.SupprimerPiece(idPiece2, IdBoss, "Mamadou DIALLO"),
                "Boss peut supprimer une pièce En cours si aucun paiement");
        }

        [Test]
        public void T4_06_Suppression_Derniere_Piece_Refusee()
        {
            // Commande avec une seule pièce
            var cmd  = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var pce  = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 5000m };
            _commandeService.Ajouter(cmd, pce, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerPiece(pce.IdPieceCommande, IdBoss, "Mamadou DIALLO"));
            Assert.That(ex!.Message, Does.Contain("dernière").Or.Contain("commande"));
        }

        [Test]
        public void T4_07_Piece_Commissionnee_Non_Supprimable()
        {
            var (_, idPiece2) = CreerCommande2Pieces();

            using (var ctx = _factory.CreateDbContext())
            {
                var comm = new Commission
                {
                    IdEmploye = IdCouturier, NomEmployeSnapshot = "Issa CISSE",
                    DateDebutPeriode = DateTime.Today.AddMonths(-1), DateFinPeriode = DateTime.Today,
                    BaseCalcul = "Total", Pourcentage = 10m, BaseMontant = 5000m,
                    MontantCommission = 500m, NbCommandes = 1, DateCalcul = DateTime.Now,
                    IdOperateur = IdBoss, NomOperateur = "Mamadou DIALLO", EstAnnulee = false
                };
                ctx.Commissions.Add(comm);
                ctx.SaveChanges();
                ctx.PiecesCommande.Find(idPiece2)!.IdCommission = comm.IdCommission;
                ctx.SaveChanges();
            }

            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.SupprimerPiece(idPiece2, IdBoss, "Mamadou DIALLO"));
        }

        // ── Helpers ──────────────────────────────────────────────────────

        /// <summary>Crée une commande avec 2 pièces. Retourne (idCommande, idPiece2).</summary>
        private (int idCommande, int idPiece2) CreerCommande2Pieces()
        {
            var cmd  = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var pce1 = new PieceCommande { TypeVetement = "Chemise", MontantCouture = 8000m, IdCouturier = IdCouturier };
            _commandeService.Ajouter(cmd, pce1, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            var pce2 = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 5000m };
            _commandeService.AjouterPiece(cmd.IdCommande, pce2, new List<Mesure>(), roleBoss: true);

            return (cmd.IdCommande, pce2.IdPieceCommande);
        }

        private void Payer(int idCommande, decimal montant) =>
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = idCommande, MontantPaye = montant,
                DatePaiement = DateTime.Now, ModePaiement = "Espèces"
            }, IdBoss, "Mamadou DIALLO");
    }

    internal class T4DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public T4DbContextFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
