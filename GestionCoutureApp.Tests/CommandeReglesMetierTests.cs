using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // Règles métier des commandes, vérifiées côté service (pas seulement
    // à l'écran) :
    //   - seul le Boss modifie le prix d'une pièce enregistrée ;
    //   - pas de livraison si la commande n'est pas soldée (sauf Boss + motif) ;
    //   - seul le Boss supprime une pièce ;
    //   - création commande + pièce + mesures + matériaux en une seule fois.
    // ====================================================================

    [TestFixture]
    public class CommandeReglesMetierTests
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
                .UseInMemoryDatabase($"ReglesMetier_{Guid.NewGuid()}")
                .Options;
            _factory = new BugFixDbContextFactory(options);

            var logFactory = NullLoggerFactory.Instance;
            _commandeService = new CommandeService(_factory, new Logger<CommandeService>(logFactory));
            _paiementService = new PaiementService(_factory, new Logger<PaiementService>(logFactory));

            using var ctx = _factory.CreateDbContext();
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss, Nom = "DIALLO", Prenom = "Mamadou",
                              Identifiant = "boss", MotDePasse = "x", Role = "Boss", Statut = "Actif" },
                new Employe { IdEmploye = IdSecretaire, Nom = "FALL", Prenom = "Marie",
                              Identifiant = "sec", MotDePasse = "x", Role = "Secretaire", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier, Nom = "CISSE", Prenom = "Issa",
                              Identifiant = "cout", MotDePasse = "x", Role = "Couturier", Statut = "Actif" });
            ctx.Clients.Add(new Client { IdClient = IdClient, Nom = "SARR", Prenom = "Fatou", Telephone = "771234567" });
            ctx.SaveChanges();
        }

        // ── Prix ──────────────────────────────────────────────────────────

        [Test]
        public void Secretaire_NePeutPas_Modifier_Le_Prix()
        {
            var (_, piece) = CreerCommande(10000m);
            piece.MontantCouture = 7000m;

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ModifierPiece(piece, new List<Mesure>(), IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("Boss"));
            Assert.That(PieceEnBase(piece.IdPieceCommande).MontantCouture, Is.EqualTo(10000m));
        }

        [Test]
        public void Secretaire_Peut_Changer_Couturier_Et_Statut()
        {
            var (_, piece) = CreerCommande(10000m);
            piece.IdCouturier = null;
            piece.Statut = "En cours";

            Assert.DoesNotThrow(() =>
                _commandeService.ModifierPiece(piece, new List<Mesure>(), IdSecretaire, "Marie FALL"));
            var enBase = PieceEnBase(piece.IdPieceCommande);
            Assert.That(enBase.Statut, Is.EqualTo("En cours"));
            Assert.That(enBase.IdCouturier, Is.Null);
        }

        [Test]
        public void Boss_Peut_Modifier_Le_Prix()
        {
            var (_, piece) = CreerCommande(10000m);
            piece.MontantCouture = 8000m;

            _commandeService.ModifierPiece(piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");
            Assert.That(PieceEnBase(piece.IdPieceCommande).MontantCouture, Is.EqualTo(8000m));
        }

        [Test]
        public void Couturier_NePeut_Pas_Modifier_Une_Piece()
        {
            var (_, piece) = CreerCommande(10000m);
            Assert.Throws<UnauthorizedAccessException>(() =>
                _commandeService.ModifierPiece(piece, new List<Mesure>(), IdCouturier, "Issa CISSE"));
        }

        // ── Livraison ─────────────────────────────────────────────────────

        [Test]
        public void Secretaire_NePeut_Pas_Livrer_Une_Commande_Non_Soldee()
        {
            var (idCommande, piece) = CreerCommande(10000m);
            Payer(idCommande, 4000m);
            piece.Statut = "Livree";

            var ex = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ModifierPiece(piece, new List<Mesure>(), IdSecretaire, "Marie FALL"));
            Assert.That(ex!.PeutForcer, Is.False);
            Assert.That(ex.ResteAPayer, Is.EqualTo(6000m));
            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.Not.EqualTo("Livree"));
        }

        [Test]
        public void Boss_Doit_Donner_Un_Motif_Pour_Livrer_Non_Solde()
        {
            var (_, piece) = CreerCommande(10000m);
            piece.Statut = "Livree";

            var ex = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ModifierPiece(piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO"));
            Assert.That(ex!.PeutForcer, Is.True);

            _commandeService.ModifierPiece(piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Client fidèle, solde demain");
            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("Livree"));
        }

        [Test]
        public void Livraison_Commande_Soldee_Autorisee_Pour_Secretaire()
        {
            var (idCommande, piece) = CreerCommande(10000m);
            Payer(idCommande, 10000m);
            piece.Statut = "Livree";

            Assert.DoesNotThrow(() =>
                _commandeService.ModifierPiece(piece, new List<Mesure>(), IdSecretaire, "Marie FALL"));
            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("Livree"));
        }

        [Test]
        public void ForcerStatut_Livree_Non_Soldee_Refuse_Pour_Secretaire()
        {
            var (idCommande, _) = CreerCommande(10000m);

            Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ForcerStatutToutesPieces(idCommande, "Livree", IdSecretaire, "Marie FALL"));
        }

        // ── Suppression de pièce ──────────────────────────────────────────

        [Test]
        public void Secretaire_NePeut_Pas_Supprimer_Une_Piece()
        {
            var (idCommande, _) = CreerCommande(10000m);
            int idPiece2 = AjouterSecondePiece(idCommande);

            Assert.Throws<UnauthorizedAccessException>(() =>
                _commandeService.SupprimerPiece(idPiece2, IdSecretaire, "Marie FALL"));
        }

        [Test]
        public void Boss_Peut_Supprimer_Une_Piece()
        {
            var (idCommande, _) = CreerCommande(10000m);
            int idPiece2 = AjouterSecondePiece(idCommande);

            _commandeService.SupprimerPiece(idPiece2, IdBoss, "Mamadou DIALLO");

            using var ctx = _factory.CreateDbContext();
            Assert.That(ctx.PiecesCommande.Any(p => p.IdPieceCommande == idPiece2), Is.False);
        }

        // ── Création en une seule fois ────────────────────────────────────

        [Test]
        public void Creation_Enregistre_Mesures_Et_Materiaux_Avec_La_Piece()
        {
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var piece = new PieceCommande { TypeVetement = "Boubou", MontantCouture = 10000m, IdCouturier = IdCouturier };
            var mesures = new List<Mesure> { new Mesure { NomMesure = "Longueur", Valeur = "120" } };
            var materiaux = new List<MaterielSupplement>
            {
                new MaterielSupplement { Designation = "Boutons", Quantite = 6, PrixUnitaire = 100m }
            };

            _commandeService.Ajouter(commande, piece, mesures, IdSecretaire, "Marie FALL", materiaux);

            using var ctx = _factory.CreateDbContext();
            var enBase = ctx.Commandes
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.MaterielSupplements)
                .Single(c => c.IdCommande == commande.IdCommande);

            Assert.That(enBase.Pieces, Has.Count.EqualTo(1));
            Assert.That(enBase.Pieces[0].Mesures, Has.Count.EqualTo(1));
            Assert.That(enBase.MaterielSupplements, Has.Count.EqualTo(1));
            Assert.That(enBase.MaterielSupplements[0].IdPieceCommande, Is.EqualTo(enBase.Pieces[0].IdPieceCommande));
            Assert.That(enBase.MaterielSupplements[0].IdOperateur, Is.EqualTo(IdSecretaire));
            Assert.That(enBase.MontantTotalAvecMateriaux, Is.EqualTo(10600m));
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private (int idCommande, PieceCommande piece) CreerCommande(decimal montant)
        {
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var piece = new PieceCommande
            {
                TypeVetement = "Chemise",
                MontantCouture = montant,
                IdCouturier = IdCouturier,
                Statut = "Terminee"
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            // Objet "formulaire" détaché, comme le construit l'écran
            var formulaire = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement = piece.TypeVetement,
                MontantCouture = piece.MontantCouture,
                IdCouturier = piece.IdCouturier,
                Statut = piece.Statut
            };
            return (commande.IdCommande, formulaire);
        }

        private int AjouterSecondePiece(int idCommande)
        {
            var piece = new PieceCommande { TypeVetement = "Pantalon", MontantCouture = 5000m };
            _commandeService.AjouterPiece(idCommande, piece, new List<Mesure>(), roleBoss: true);
            return piece.IdPieceCommande;
        }

        private void Payer(int idCommande, decimal montant)
        {
            _paiementService.Ajouter(new Paiement
            {
                IdCommande = idCommande,
                MontantPaye = montant,
                DatePaiement = DateTime.Now,
                ModePaiement = "Espèces"
            }, idOperateur: IdBoss, nomOperateur: "Mamadou DIALLO");
        }

        private PieceCommande PieceEnBase(int idPiece)
        {
            using var ctx = _factory.CreateDbContext();
            return ctx.PiecesCommande.Single(p => p.IdPieceCommande == idPiece);
        }
    }
}
