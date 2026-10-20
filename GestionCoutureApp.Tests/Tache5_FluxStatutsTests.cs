using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // TÂCHE 5 / ÉTAPE 1 — Flux strict de statuts
    //
    // Flux obligatoire : A faire → En cours → Terminee → Livree
    //
    // Tests inclus :
    //   T5_01 : flux normal complet autorisé (A faire → En cours → Terminee → Livree)
    //   T5_02 : saut d'une étape en avant INTERDIT (A faire → Terminee)
    //   T5_02b: saut de deux étapes en avant INTERDIT (A faire → Livree)
    //   T5_02c: saut En cours → Livree INTERDIT
    //   T5_03 : retour en arrière bloqué pour la Secrétaire
    //   T5_04 : retour en arrière Boss sans motif bloqué
    //   T5_05 : retour en arrière Boss avec motif autorisé + DateTerminee remise à null
    //   T5_05b: Livree → tout statut INTERDIT pour tous (passer par module Retour)
    //   T5_06 : Terminee → Livree bloqué si commande non soldée (Secrétaire)
    //   T5_06b: Terminee → Livree autorisé Boss avec motif si non soldée
    //   T5_07 : ForcerStatutToutesPieces respecte flux strict (retour Boss avec motif)
    //   T5_07b: ForcerStatutToutesPieces retour arrière bloqué Secrétaire
    //   T5_07c: ForcerStatutToutesPieces saut interdit (A faire → Livree)
    //   T5_08 : CompterPiecesAAttribuer retourne le bon nombre
    //   T5_09 : ObtenirCommandesAAttribuerAsync filtre correctement
    //   T5_10 : rétrogradation pièce commissionnée lève PieceCommissionneeException
    //   T5_11 : DateTerminee renseignée au passage Terminee et remise à null à la rétrogradation
    //   T5_12 : DateTerminee conservée lors de Terminee → Livree
    //   T5_13 : message d'erreur Livree→autre mentionne "module Retour"
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
                .UseInMemoryDatabase($"T5_{Guid.NewGuid()}")
                .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _factory = new T5DbContextFactory(opts);
            var log  = NullLoggerFactory.Instance;
            _commandeService = new CommandeService(_factory, new Logger<CommandeService>(log),
                TestDbHelper.NoOpAudit);
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
            Payer(idCommande, PieceEnBase(idPiece).MontantCouture);

            Assert.DoesNotThrow(() => _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdSecretaire, "Marie FALL"));
            Assert.DoesNotThrow(() => _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdSecretaire, "Marie FALL"));
            Assert.DoesNotThrow(() => _commandeService.ChangerStatutPiece(idPiece, "Livree",   IdSecretaire, "Marie FALL"));

            Assert.That(PieceEnBase(idPiece).Statut, Is.EqualTo("Livree"));
        }

        // ── T5_02 : saut d'étape interdit ────────────────────────────────

        [Test]
        public void T5_02_Saut_AFaire_Vers_Terminee_Interdit()
        {
            var (_, idPiece) = CreerPiece();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("En cours").IgnoreCase,
                "Le message doit indiquer l'étape manquante");
        }

        [Test]
        public void T5_02b_Saut_AFaire_Vers_Livree_Interdit()
        {
            var (_, idPiece) = CreerPiece();

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Livree", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("Terminée").Or.Contain("En cours").IgnoreCase);
        }

        [Test]
        public void T5_02c_Saut_EnCours_Vers_Livree_Interdit()
        {
            var (idCommande, idPiece) = CreerPiece();
            Payer(idCommande, PieceEnBase(idPiece).MontantCouture);
            _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Livree", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("Terminée").IgnoreCase);
        }

        // ── T5_03 : retour arrière bloqué Secrétaire ─────────────────────

        [Test]
        public void T5_03_Retour_Arriere_Bloque_Secretaire()
        {
            var (_, idPiece) = CreerPiece();
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "En cours", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("Boss").IgnoreCase);
        }

        // ── T5_04 : retour arrière Boss sans motif bloqué ─────────────────

        [Test]
        public void T5_04_Retour_Arriere_Boss_Sans_Motif_Bloque()
        {
            var (_, idPiece) = CreerPiece();
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
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
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            Assert.DoesNotThrow(() =>
                _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "Retouche complémentaire nécessaire"));

            Assert.That(PieceEnBase(idPiece).Statut, Is.EqualTo("En cours"));
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Null,
                "DateTerminee doit être remise à null lors du retour arrière");
        }

        // ── T5_05b : Livree → tout statut interdit pour tous ─────────────

        [Test]
        public void T5_05b_Livree_Vers_Autre_Interdit_Pour_Tous()
        {
            var (idCommande, idPiece) = CreerPiece();
            Payer(idCommande, PieceEnBase(idPiece).MontantCouture);
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Livree",   IdBoss, "Mamadou DIALLO");

            // Même le Boss ne peut pas rétrograder depuis Livree
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "Test retour"));
            Assert.That(ex!.Message, Does.Contain("Retour").Or.Contain("retour").IgnoreCase);
        }

        // ── T5_06 : Terminee → Livree bloqué si non soldée ───────────────

        [Test]
        public void T5_06_Livree_Bloque_Commande_Non_Soldee()
        {
            var (_, idPiece) = CreerPiece(); // aucun paiement
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Livree", IdSecretaire, "Marie FALL"));
            Assert.That(ex!.PeutForcer, Is.False);
        }

        // ── T5_06b : Terminee → Livree Boss avec motif si non soldée ─────

        [Test]
        public void T5_06b_Livree_Boss_Avec_Motif_Si_Non_Soldee()
        {
            var (_, idPiece) = CreerPiece(); // aucun paiement
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Boss lève d'abord PeutForcer=true
            var ex1 = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Livree", IdBoss, "Mamadou DIALLO"));
            Assert.That(ex1!.PeutForcer, Is.True, "Le Boss doit pouvoir forcer avec motif");

            // Boss avec motif : OK
            Assert.DoesNotThrow(() =>
                _commandeService.ChangerStatutPiece(idPiece, "Livree", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "Client absent, livraison reportée avec accord verbal"));
            Assert.That(PieceEnBase(idPiece).Statut, Is.EqualTo("Livree"));
        }

        // ── T5_07 : ForcerStatutToutesPieces respecte flux strict ─────────

        [Test]
        public void T5_07_ForcerStatutToutesPieces_Retour_Arriere_Boss_Avec_Motif()
        {
            var (idCommande, idPiece) = CreerPiece();
            _commandeService.ForcerStatutToutesPieces(idCommande, "En cours",  IdBoss, "Mamadou DIALLO");
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
            _commandeService.ForcerStatutToutesPieces(idCommande, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ForcerStatutToutesPieces(idCommande, "Terminee", IdBoss, "Mamadou DIALLO");

            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ForcerStatutToutesPieces(idCommande, "A faire", IdSecretaire, "Marie FALL"));
        }

        [Test]
        public void T5_07c_ForcerStatutToutesPieces_Saut_Interdit()
        {
            var (idCommande, _) = CreerPiece();

            // Saut A faire → Livree doit être interdit
            Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ForcerStatutToutesPieces(idCommande, "Livree", IdBoss, "Mamadou DIALLO"));
        }

        // ── T5_08 : CompterPiecesAAttribuer ───────────────────────────────

        [Test]
        public void T5_08_CompterPiecesAAttribuer_Correct()
        {
            CreerPiece(avecCouturier: true);   // avec couturier → ne compte pas
            CreerPiece(avecCouturier: false);  // sans couturier → compte
            CreerPiece(avecCouturier: false);  // sans couturier → compte

            int nb = _commandeService.CompterPiecesAAttribuer();
            Assert.That(nb, Is.EqualTo(2));
        }

        // ── T5_09 : ObtenirCommandesAAttribuerAsync ───────────────────────

        [Test]
        public async Task T5_09_ObtenirCommandesAAttribuerAsync_Filtre_Correct()
        {
            CreerPiece(avecCouturier: false);
            CreerPiece(avecCouturier: false);
            CreerPiece(avecCouturier: true); // ne doit pas apparaître

            var result = await _commandeService.ObtenirCommandesAAttribuerAsync(1, 50);
            Assert.That(result.TotalCount, Is.EqualTo(2));
        }

        // ── T5_10 : pièce commissionnée → PieceCommissionneeException ─────

        [Test]
        public void T5_10_Retrograder_Piece_Commissionnee_Leve_Exception()
        {
            var (idCommande, idPiece) = CreerPiece();
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            // Simuler commission enregistrée sur la pièce
            using (var ctx = _factory.CreateDbContext())
            {
                var commission = new Commission
                {
                    IdEmploye = IdCouturier,
                    NomEmployeSnapshot = "Issa CISSE",
                    DateDebutPeriode = DateTime.UtcNow.AddDays(-30),
                    DateFinPeriode = DateTime.UtcNow,
                    Pourcentage = 10,
                    BaseMontant = 10000,
                    MontantCommission = 1000,
                    BaseCalcul = "Total",
                    NbCommandes = 1,
                    DateCalcul = DateTime.UtcNow,
                    IdOperateur = IdBoss,
                    NomOperateur = "Mamadou DIALLO",
                    EstAnnulee = false
                };
                ctx.Commissions.Add(commission);
                ctx.SaveChanges();

                var piece = ctx.PiecesCommande.Find(idPiece)!;
                piece.IdCommission = commission.IdCommission;
                ctx.SaveChanges();
            }

            // Sans CommissionService → lève PieceCommissionneeException
            var ex = Assert.Throws<CommandeService.PieceCommissionneeException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "Défaut découvert après commission"));
            Assert.That(ex!.IdCommission, Is.GreaterThan(0));
            Assert.That(ex.Message, Does.Contain("commission").IgnoreCase);
        }

        // ── T5_11 : DateTerminee renseignée et effacée ────────────────────

        [Test]
        public void T5_11_DateTerminee_Renseignee_Et_Effacee()
        {
            var (_, idPiece) = CreerPiece();
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Null, "Doit être null à la création");

            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Null, "Toujours null en cours");

            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Not.Null,
                "DateTerminee doit être renseignée au passage Terminee");
            Assert.That(PieceEnBase(idPiece).IdOperateurTerminee, Is.EqualTo(IdBoss));

            _commandeService.ChangerStatutPiece(idPiece, "En cours", IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Défaut à corriger");
            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.Null,
                "DateTerminee doit être remise à null après rétrogradation");
            Assert.That(PieceEnBase(idPiece).IdOperateurTerminee, Is.Null);
        }

        // ── T5_12 : DateTerminee conservée lors de Terminee → Livree ─────

        [Test]
        public void T5_12_DateTerminee_Conservee_Terminee_Vers_Livree()
        {
            var (idCommande, idPiece) = CreerPiece();
            Payer(idCommande, PieceEnBase(idPiece).MontantCouture);

            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");

            var dateTerminee = PieceEnBase(idPiece).DateTerminee;
            Assert.That(dateTerminee, Is.Not.Null);

            _commandeService.ChangerStatutPiece(idPiece, "Livree", IdBoss, "Mamadou DIALLO");

            Assert.That(PieceEnBase(idPiece).DateTerminee, Is.EqualTo(dateTerminee),
                "DateTerminee ne doit pas changer lors du passage Terminee → Livree");
        }

        // ── T5_13 : message Livree→autre mentionne module Retour ─────────

        [Test]
        public void T5_13_Message_LivreeVersAutre_Mentionne_ModuleRetour()
        {
            var (idCommande, idPiece) = CreerPiece();
            Payer(idCommande, PieceEnBase(idPiece).MontantCouture);
            _commandeService.ChangerStatutPiece(idPiece, "En cours",  IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Terminee", IdBoss, "Mamadou DIALLO");
            _commandeService.ChangerStatutPiece(idPiece, "Livree",   IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(idPiece, "A faire", IdBoss, "Mamadou DIALLO",
                    motifLivraisonNonSoldee: "test"));
            Assert.That(ex!.Message, Does.Contain("Retour").IgnoreCase,
                "Le message doit mentionner le module Retour");
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
                MontantCouture = 10000m + _pieceCounter,
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
