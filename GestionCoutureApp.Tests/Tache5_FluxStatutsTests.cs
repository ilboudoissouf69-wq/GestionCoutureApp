using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 5 — Flux strict de statuts + file À attribuer
    //
    //   T5_01 : flux normal (A faire → En cours → Terminee → Livree) autorisé
    //   T5_02 : saut d'étape autorisé (A faire → Terminee, etc.)
    //   T5_03 : retour en arrière bloqué pour la Secrétaire
    //   T5_04 : retour en arrière Boss sans motif bloqué
    //   T5_05 : retour en arrière Boss avec motif autorisé
    //   T5_06 : Livree bloqué si non soldée (Secrétaire)
    //   T5_07 : ForcerStatutToutesPieces respecte flux strict
    //   T5_08 : CompterPiecesAAttribuer retourne le bon nombre
    //   T5_09 : ObtenirCommandesAAttribuerAsync filtre correctement
    // ====================================================================

    [TestFixture]
    public class Tache5_FluxStatutsTests
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
                .UseInMemoryDatabase($"T5_{Guid.NewGuid()}").Options;
            _factory = new T5DbContextFactory(opts);
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

        // ── T5_01 : flux normal complet ───────────────────────────────────

        [Test]
        public void T5_01_Flux_Normal_Autorise()
        {
            var (idCommande, idPiece) = CreerPiece();
            // Solder la commande (montant = 10001 à cause du compteur)
            using (var ctx = _factory.CreateDbContext())
            {
                var pce = ctx.PiecesCommande.Find(idPiece)!;
                Payer(idCommande, pce.MontantCouture);
            }

            Assert.DoesNotThrow(() => _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdSecretaire, "Marie FALL"));
            Assert.DoesNotThrow(() => _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdSecretaire, "Marie FALL"));
            Assert.DoesNotThrow(() => _commandeService.ChangerStatutPiece(idPiece, "Livree",   IdSecretaire, "Marie FALL"));

            Assert.That(PieceEnBase(idPiece).Statut, Is.EqualTo("Livree"));
        }

        // ── T5_02 : saut d'étape autorisé (avant) ────────────────────────

        [Test]
        public void T5_02_Saut_Etape_Avance_Autorise()
        {
            var (idCommande, idPiece) = CreerPiece();
            Payer(idCommande, 10000m);

            // Sauter directement à Terminee depuis A faire
            Assert.DoesNotThrow(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdSecretaire, "Marie FALL"),
                "Un saut en avant (A faire → Terminee) doit être autorisé");
        }

        // ── T5_03 : retour arrière bloqué Secrétaire ─────────────────────

        [Test]
        public void T5_03_Retour_Arriere_Bloque_Secretaire()
        {
            var (_, idPiece) = CreerPiece();
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Secrétaire ne peut pas rétrograder
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "En cours", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("Boss").IgnoreCase);
        }

        // ── T5_04 : retour arrière Boss sans motif bloqué ─────────────────

        [Test]
        public void T5_04_Retour_Arriere_Boss_Sans_Motif_Bloque()
        {
            var (_, idPiece) = CreerPiece();
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO"));
            Assert.That(ex!.Message, Does.Contain("motif").IgnoreCase);
        }

        // ── T5_05 : retour arrière Boss avec motif autorisé ───────────────

        [Test]
        public void T5_05_Retour_Arriere_Boss_Avec_Motif_Autorise()
        {
            var (_, idPiece) = CreerPiece();
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // motifLivraisonNonSoldee sert aussi de motif de rétrogradation
            Assert.DoesNotThrow(() =>
                _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "Retouche complémentaire nécessaire"));

            Assert.That(PieceEnBase(idPiece).Statut, Is.EqualTo("En cours"));
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Null, "DateTerminee remise à null lors du retour arrière");
        }

        // ── T5_06 : Livree bloqué si non soldée ──────────────────────────

        [Test]
        public void T5_06_Livree_Bloque_Commande_Non_Soldee()
        {
            var (_, idPiece) = CreerPiece(); // aucun paiement

            var ex = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Livree", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.PeutForcer, Is.False);
        }

        // ── T5_07 : ForcerStatutToutesPieces respect flux ─────────────────

        [Test]
        public void T5_07_ForcerStatutToutesPieces_Retour_Arriere_Boss_Avec_Motif()
        {
            var (idCommande, idPiece) = CreerPiece();
            _commandeService.ForcerStatutToutesPieces(idCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            Assert.DoesNotThrow(() =>
                _commandeService.ForcerStatutToutesPieces(idCommande, "A faire", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "Reprise de toute la commande"));

            Assert.That(PieceEnBase(idPiece).Statut, Is.EqualTo("A faire"));
        }

        [Test]
        public void T5_07b_ForcerStatutToutesPieces_Retour_Arriere_Secretaire_Bloque()
        {
            var (idCommande, _) = CreerPiece();
            _commandeService.ForcerStatutToutesPieces(idCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ForcerStatutToutesPieces(idCommande, "A faire", IdSecretaire, "Marie FALL"));
        }

        // ── T5_08 : CompterPiecesAAttribuer ───────────────────────────────

        [Test]
        public void T5_08_CompterPiecesAAttribuer_Correct()
        {
            // Pièce avec couturier → ne compte pas
            CreerPiece(avecCouturier: true);
            // Pièce sans couturier → compte
            CreerPiece(avecCouturier: false);
            CreerPiece(avecCouturier: false);

            int nb = _commandeService.CompterPiecesAAttribuer();
            Assert.That(nb, Is.EqualTo(2));
        }

        // ── T5_09 : ObtenirCommandesAAttribuerAsync ───────────────────────

        [Test]
        public async Task T5_09_ObtenirCommandesAAttribuerAsync_Filtre_Correct()
        {
            // 2 commandes sans couturier
            CreerPiece(avecCouturier: false);
            CreerPiece(avecCouturier: false);
            // 1 commande avec couturier → ne doit pas apparaître
            CreerPiece(avecCouturier: true);

            var result = await _commandeService.ObtenirCommandesAAttribuerAsync(1, 50);
            Assert.That(result.TotalCount, Is.EqualTo(2));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private int _pieceCounter = 0;

        private (int idCommande, int idPiece) CreerPiece(bool avecCouturier = true)
        {
            _pieceCounter++;
            var cmd = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var pce = new PieceCommande
            {
                TypeVetement   = "Chemise",
                MontantCouture = 10000m + _pieceCounter, // montant unique pour éviter anti-doublon
                IdCouturier    = avecCouturier ? IdCouturier : (int?)null
            };
            _commandeService.Ajouter(cmd, pce, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
            return (cmd.IdCommande, pce.IdPieceCommande);
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

    internal class T5DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public T5DbContextFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_options);
    }
}
