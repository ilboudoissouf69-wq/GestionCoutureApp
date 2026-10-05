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

        // ── ChangerStatutPiece ────────────────────────────────────────────

        [Test]
        public void ChangerStatutPiece_Boss_Peut_Changer_Statut()
        {
            var (_, piece) = CreerCommande(10000m);

            _commandeService.ChangerStatutPiece(
                piece.IdPieceCommande, "En cours", IdBoss, "Mamadou DIALLO");

            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("En cours"));
        }

        [Test]
        public void ChangerStatutPiece_Secretaire_Peut_Changer_Statut()
        {
            var (_, piece) = CreerCommande(10000m);

            _commandeService.ChangerStatutPiece(
                piece.IdPieceCommande, "En cours", IdSecretaire, "Marie FALL");

            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("En cours"));
        }

        [Test]
        public void ChangerStatutPiece_Couturier_Acces_Refuse()
        {
            var (_, piece) = CreerCommande(10000m);
            // Passer la pièce en "En cours" via Boss pour avoir un statut de départ non-A faire
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "En cours", IdBoss, "Mamadou DIALLO");

            Assert.Throws<UnauthorizedAccessException>(() =>
                _commandeService.ChangerStatutPiece(
                    piece.IdPieceCommande, "Terminee", IdCouturier, "Issa CISSE"));

            // Le statut doit être inchangé (toujours "En cours")
            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("En cours"));
        }

        [Test]
        public void ChangerStatutPiece_Livree_NonSoldee_Refuse_Secretaire()
        {
            var (_, piece) = CreerCommande(10000m);
            // Commande non soldée (aucun paiement) — on met En cours pour avoir un état cohérent
            _commandeService.ChangerStatutPiece(piece.IdPieceCommande, "En cours", IdBoss, "Mamadou DIALLO");

            var ex = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ChangerStatutPiece(
                    piece.IdPieceCommande, "Livree", IdSecretaire, "Marie FALL"));

            Assert.That(ex!.PeutForcer, Is.False);
            // Statut inchangé
            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.Not.EqualTo("Livree"));
        }

        [Test]
        public void ChangerStatutPiece_Livree_NonSoldee_Boss_Peut_Forcer_Avec_Motif()
        {
            var (_, piece) = CreerCommande(10000m);

            // Sans motif → lève PeutForcer = true
            var ex = Assert.Throws<LivraisonNonSoldeeException>(() =>
                _commandeService.ChangerStatutPiece(
                    piece.IdPieceCommande, "Livree", IdBoss, "Mamadou DIALLO"));
            Assert.That(ex!.PeutForcer, Is.True);

            // Avec motif → réussit
            _commandeService.ChangerStatutPiece(
                piece.IdPieceCommande, "Livree", IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Client paie demain, accord verbal");

            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("Livree"));
        }

        [Test]
        public void ChangerStatutPiece_PieceCommissionnee_Bloquee()
        {
            // Créer une commande et verrouiller la pièce avec une commission fictive
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            var piece = new PieceCommande
            {
                TypeVetement = "Chemise", MontantCouture = 12000m,
                IdCouturier = IdCouturier, Statut = "A faire"
            };
            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            using (var ctx = _factory.CreateDbContext())
            {
                var commission = new Commission
                {
                    IdEmploye = IdCouturier, NomEmployeSnapshot = "Issa CISSE",
                    DateDebutPeriode = DateTime.Today.AddMonths(-1), DateFinPeriode = DateTime.Today,
                    BaseCalcul = "Total", Pourcentage = 10m,
                    BaseMontant = 12000m, MontantCommission = 1200m,
                    NbCommandes = 1, DateCalcul = DateTime.Now,
                    IdOperateur = IdBoss, NomOperateur = "Mamadou DIALLO", EstAnnulee = false
                };
                ctx.Commissions.Add(commission);
                ctx.SaveChanges();

                var pieceEnBase = ctx.PiecesCommande.First(p => p.IdCommande == commande.IdCommande);
                pieceEnBase.IdCommission = commission.IdCommission;
                ctx.SaveChanges();
            }

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.ChangerStatutPiece(
                    piece.IdPieceCommande, "En cours", IdBoss, "Mamadou DIALLO"));
            Assert.That(ex!.Message, Does.Contain("commission"));
        }

        // ── Statut forcé à "A faire" à la création ────────────────────────

        [Test]
        public void Creation_Force_Statut_A_Faire()
        {
            var commande = new Commande { IdClient = IdClient, DateFin = DateTime.Now.AddDays(7) };
            // On tente de créer avec un statut différent
            var piece = new PieceCommande
            {
                TypeVetement = "Veste", MontantCouture = 15000m,
                IdCouturier = IdCouturier,
                Statut = "En cours"   // essai de contournement
            };

            _commandeService.Ajouter(commande, piece, new List<Mesure>(), IdBoss, "Mamadou DIALLO");

            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("A faire"),
                "La création doit toujours forcer le statut à 'A faire'.");
        }

        [Test]
        public void AjouterPiece_Force_Statut_A_Faire()
        {
            var (idCommande, _) = CreerCommande(10000m);

            var nouvellePiece = new PieceCommande
            {
                TypeVetement = "Pantalon", MontantCouture = 8000m,
                Statut = "Terminee"   // essai de contournement
            };
            _commandeService.AjouterPiece(idCommande, nouvellePiece, new List<Mesure>(), roleBoss: true);

            Assert.That(PieceEnBase(nouvellePiece.IdPieceCommande).Statut, Is.EqualTo("A faire"),
                "AjouterPiece doit toujours forcer le statut à 'A faire'.");
        }

        // ── ForcerStatutToutesPieces — refus livraison non soldée ─────────

        [Test]
        public void ForcerStatutToutesPieces_Livree_Soldee_Autorise()
        {
            var (idCommande, piece) = CreerCommande(10000m);
            Payer(idCommande, 10000m);

            Assert.DoesNotThrow(() =>
                _commandeService.ForcerStatutToutesPieces(
                    idCommande, "Livree", IdBoss, "Mamadou DIALLO"));

            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("Livree"));
        }

        [Test]
        public void ForcerStatutToutesPieces_Livree_NonSoldee_Boss_Avec_Motif_Autorise()
        {
            var (idCommande, piece) = CreerCommande(10000m);
            // Aucun paiement

            _commandeService.ForcerStatutToutesPieces(
                idCommande, "Livree", IdBoss, "Mamadou DIALLO",
                motifLivraisonNonSoldee: "Client vient payer en main propre");

            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("Livree"));
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

        // ── CommandeService.Modifier — droits Secrétaire ─────────────────

        [Test]
        public void Modifier_Secretaire_NePeutPas_Changer_Client()
        {
            var (idCommande, piece) = CreerCommande(10000m);

            // Fabriquer une commande avec un autre client
            var commandeModif = new Commande
            {
                IdCommande = idCommande,
                IdClient   = IdClient + 99,  // client différent
                DateFin    = DateTime.Now.AddDays(10),
                HeureDebut = TimeSpan.Zero
            };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande  = piece.IdPieceCommande,
                TypeVetement     = piece.TypeVetement,
                MontantCouture   = piece.MontantCouture,
                Statut           = piece.Statut
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.Modifier(commandeModif, pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("client").IgnoreCase);
        }

        [Test]
        public void Modifier_Secretaire_NePeutPas_Changer_Prix()
        {
            var (idCommande, piece) = CreerCommande(10000m);

            var commandeModif = new Commande
            {
                IdCommande = idCommande,
                IdClient   = IdClient,
                DateFin    = DateTime.Now.AddDays(10),
                HeureDebut = TimeSpan.Zero
            };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = piece.TypeVetement,
                MontantCouture  = 7500m,   // prix modifié — interdit Secrétaire
                Statut          = piece.Statut
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.Modifier(commandeModif, pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("prix").IgnoreCase.Or.Contain("montant").IgnoreCase);
            // Prix inchangé en base
            Assert.That(PieceEnBase(piece.IdPieceCommande).MontantCouture, Is.EqualTo(10000m));
        }

        [Test]
        public void Modifier_Secretaire_NePeutPas_Changer_TypeVetement()
        {
            var (idCommande, piece) = CreerCommande(10000m);

            var commandeModif = new Commande
            {
                IdCommande = idCommande, IdClient = IdClient,
                DateFin = DateTime.Now.AddDays(10), HeureDebut = TimeSpan.Zero
            };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = "Boubou",   // changement — interdit
                MontantCouture  = piece.MontantCouture,
                Statut          = piece.Statut
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _commandeService.Modifier(commandeModif, pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"));
            Assert.That(ex!.Message, Does.Contain("type").IgnoreCase.Or.Contain("vêtement").IgnoreCase);
        }

        [Test]
        public void Modifier_Secretaire_Peut_Changer_DateLivraison_Et_Couturier()
        {
            var (idCommande, piece) = CreerCommande(10000m);
            var nouvelleDate = DateTime.Now.AddDays(14);

            var commandeModif = new Commande
            {
                IdCommande = idCommande, IdClient = IdClient,
                DateFin    = nouvelleDate,
                HeureDebut = TimeSpan.FromHours(9)
            };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = piece.TypeVetement,
                MontantCouture  = piece.MontantCouture,
                IdCouturier     = IdCouturier,
                Statut          = "En cours"
            };

            Assert.DoesNotThrow(() =>
                _commandeService.Modifier(commandeModif, pieceModif, new List<Mesure>(),
                    IdSecretaire, "Marie FALL"));

            // Vérifier en base
            using var ctx = _factory.CreateDbContext();
            var cmd = ctx.Commandes.First(c => c.IdCommande == idCommande);
            Assert.That(cmd.DateFin.Date, Is.EqualTo(nouvelleDate.Date));
            Assert.That(PieceEnBase(piece.IdPieceCommande).Statut, Is.EqualTo("En cours"));
        }

        [Test]
        public void Modifier_Couturier_Acces_Refuse()
        {
            var (idCommande, piece) = CreerCommande(10000m);
            var commandeModif = new Commande
            {
                IdCommande = idCommande, IdClient = IdClient,
                DateFin = DateTime.Now.AddDays(10), HeureDebut = TimeSpan.Zero
            };
            var pieceModif = new PieceCommande
            {
                IdPieceCommande = piece.IdPieceCommande,
                TypeVetement    = piece.TypeVetement,
                MontantCouture  = piece.MontantCouture,
                Statut          = piece.Statut
            };

            Assert.Throws<UnauthorizedAccessException>(() =>
                _commandeService.Modifier(commandeModif, pieceModif, new List<Mesure>(),
                    IdCouturier, "Issa CISSE"));
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
