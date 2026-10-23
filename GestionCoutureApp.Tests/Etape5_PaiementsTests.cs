// =======================================================================
// ÉTAPE 5 — Tests Paiements
//
// C3 : ChargerCommandes (PaiementsView) — équivalence de la liste retournée
//      entre ancienne logique (ObtenirTous + filtre) et nouvelle (projection légère).
//      Les formules ResteAPayer doivent être identiques.
//
// C7 : PaiementService.Ajouter sur commande annulée/supprimée
//      PREUVE : avant correction, le paiement était accepté.
//      CORRECTION : InvalidOperationException avec message en français.
// =======================================================================

using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    public class Etape5_PaiementsTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private PaiementService _paiementService = null!;
        private CommandeService _commandeService = null!;

        private class Etape5Factory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public Etape5Factory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }

        [SetUp]
        public void Init()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("etape5_" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _factory = new Etape5Factory(opts);
            _paiementService = new PaiementService(
                _factory, NullLogger<PaiementService>.Instance);
            _commandeService = new CommandeService(
                _factory, NullLogger<CommandeService>.Instance, TestDbHelper.NoOpAudit);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static Employe CreerBoss(ApplicationDbContext ctx)
        {
            var e = new Employe
            {
                Prenom = "Boss", Nom = "Test", Identifiant = "b_" + Guid.NewGuid(),
                Role = "Boss", Statut = "Actif", MotDePasse = "hash"
            };
            ctx.Employes.Add(e); ctx.SaveChanges(); return e;
        }

        private static Client CreerClient(ApplicationDbContext ctx)
        {
            var c = new Client { Prenom = "Jean", Nom = "Dupont", Telephone = "0600" };
            ctx.Clients.Add(c); ctx.SaveChanges(); return c;
        }

        private static (Commande, PieceCommande) CreerCommandeAvecPiece(
            ApplicationDbContext ctx, Employe boss, Client client,
            decimal montantCouture = 10000m, decimal montantMateriau = 0m,
            bool supprimee = false, bool annulee = false,
            decimal paiementInitial = 0m)
        {
            var cmd = new Commande
            {
                IdClient = client.IdClient,
                DateDebut = DateTime.Today,
                DateFin = DateTime.Today.AddDays(7),
                IdOperateurCreation = boss.IdEmploye,
                NomOperateurCreation = "Boss",
                EstSupprimee = supprimee,
                EstAnnulee   = annulee
            };
            ctx.Commandes.Add(cmd);
            ctx.SaveChanges();

            var piece = new PieceCommande
            {
                IdCommande = cmd.IdCommande,
                TypeVetement = "Robe",
                MontantCouture = montantCouture,
                Statut = "A faire"
            };
            ctx.PiecesCommande.Add(piece);

            if (montantMateriau > 0)
            {
                ctx.MaterielsSupplements.Add(new MaterielSupplement
                {
                    IdCommande   = cmd.IdCommande,
                    Designation  = "Tissu",
                    Quantite     = 1,
                    PrixUnitaire = montantMateriau,
                    IdOperateur  = boss.IdEmploye,
                    NomOperateur = "Boss"
                });
            }

            if (paiementInitial > 0)
            {
                ctx.Paiements.Add(new Paiement
                {
                    IdCommande   = cmd.IdCommande,
                    MontantPaye  = paiementInitial,
                    ModePaiement = "Espèces",
                    DatePaiement = DateTime.Today,
                    IdOperateur  = boss.IdEmploye,
                    NomOperateur = "Boss",
                    RecuNumero   = "REC-TEST-" + Guid.NewGuid().ToString("N")[..6]
                });
            }

            ctx.SaveChanges();
            return (cmd, piece);
        }

        // ================================================================
        // C3 — Équivalence ResteAPayer : ancienne vs nouvelle projection
        // ================================================================

        [Test]
        public void C3_ResteAPayer_Equivalent_SansPaiement()
        {
            // Ancienne formule : commande.ResteAPayer = MontantTotalAvecMateriaux - SommePaiementsValides
            // Nouvelle formule (projection) : SUM(Pieces) + SUM(Materiaux) - SUM(Paiements valides)
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            CreerCommandeAvecPiece(ctx, boss, client, montantCouture: 12000m, montantMateriau: 3000m);

            // Ancienne logique (charge complète)
            var cmdComplète = ctx.Commandes
                .Include(c => c.Pieces)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Paiements)
                .First(c => !c.EstSupprimee);
            decimal resteAncien = cmdComplète.ResteAPayer;

            // Nouvelle logique (projection légère)
            var proj = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new
                {
                    MontantCouture   = c.Pieces.Sum(p => (decimal?)p.MontantCouture) ?? 0m,
                    MontantMateriaux = c.MaterielSupplements.Sum(m => (decimal?)(m.Quantite * m.PrixUnitaire)) ?? 0m,
                    PaiementsValides = c.Paiements.Where(p => !p.EstAnnule).Select(p => p.MontantPaye).ToList()
                })
                .ToList()
                .Select(c => new
                {
                    Total      = c.MontantCouture + c.MontantMateriaux,
                    Encaisse   = c.PaiementsValides.Sum(),
                })
                .First();

            decimal resteNouveau = proj.Total - proj.Encaisse;

            Assert.That(resteNouveau, Is.EqualTo(resteAncien),
                "C3 : la projection légère doit produire le même ResteAPayer que l'ancienne logique");
        }

        [Test]
        public void C3_ResteAPayer_Equivalent_AvecPaiementPartiel()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            // Commande 15 000 FCFA (couture + matériaux), acompte 6 000
            CreerCommandeAvecPiece(ctx, boss, client,
                montantCouture: 12000m, montantMateriau: 3000m, paiementInitial: 6000m);

            var cmdComplète = ctx.Commandes
                .Include(c => c.Pieces)
                .Include(c => c.MaterielSupplements)
                .Include(c => c.Paiements)
                .First(c => !c.EstSupprimee);
            decimal resteAncien = cmdComplète.ResteAPayer; // doit être 9 000

            var proj = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new
                {
                    MontantCouture   = c.Pieces.Sum(p => (decimal?)p.MontantCouture) ?? 0m,
                    MontantMateriaux = c.MaterielSupplements.Sum(m => (decimal?)(m.Quantite * m.PrixUnitaire)) ?? 0m,
                    PaiementsValides = c.Paiements.Where(p => !p.EstAnnule).Select(p => p.MontantPaye).ToList()
                })
                .ToList()
                .Select(c => c.MontantCouture + c.MontantMateriaux - c.PaiementsValides.Sum())
                .First();

            Assert.That(proj, Is.EqualTo(resteAncien),
                "C3 : avec un acompte, la projection doit donner le même reste");
            Assert.That(resteAncien, Is.EqualTo(9000m));
        }

        [Test]
        public void C3_FiltreSolde_ExclueSoldee()
        {
            // Une commande entièrement soldée ne doit PAS figurer dans la liste
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            // Soldée : paiement = montant total
            CreerCommandeAvecPiece(ctx, boss, client,
                montantCouture: 5000m, paiementInitial: 5000m);
            // Non soldée
            CreerCommandeAvecPiece(ctx, boss, client,
                montantCouture: 8000m, paiementInitial: 3000m);

            var encaissables = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new
                {
                    c.IdCommande,
                    MontantCouture   = c.Pieces.Sum(p => (decimal?)p.MontantCouture) ?? 0m,
                    MontantMateriaux = c.MaterielSupplements.Sum(m => (decimal?)(m.Quantite * m.PrixUnitaire)) ?? 0m,
                    PaiementsValides = c.Paiements.Where(p => !p.EstAnnule).Select(p => p.MontantPaye).ToList()
                })
                .ToList()
                .Where(c => (c.MontantCouture + c.MontantMateriaux - c.PaiementsValides.Sum()) > 0.01m)
                .ToList();

            Assert.That(encaissables, Has.Count.EqualTo(1),
                "C3 : seule la commande non soldée doit figurer dans la liste");
        }

        [Test]
        public void C3_FiltreSolde_ExclueSupprimee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            // Supprimée avec un reste : ne doit PAS figurer
            CreerCommandeAvecPiece(ctx, boss, client, montantCouture: 5000m, supprimee: true);
            // Active avec un reste : doit figurer
            CreerCommandeAvecPiece(ctx, boss, client, montantCouture: 8000m);

            var encaissables = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new
                {
                    c.IdCommande,
                    MontantCouture   = c.Pieces.Sum(p => (decimal?)p.MontantCouture) ?? 0m,
                    MontantMateriaux = c.MaterielSupplements.Sum(m => (decimal?)(m.Quantite * m.PrixUnitaire)) ?? 0m,
                    PaiementsValides = c.Paiements.Where(p => !p.EstAnnule).Select(p => p.MontantPaye).ToList()
                })
                .ToList()
                .Where(c => (c.MontantCouture + c.MontantMateriaux - c.PaiementsValides.Sum()) > 0.01m)
                .ToList();

            Assert.That(encaissables, Has.Count.EqualTo(1));
        }

        // ================================================================
        // C7 — PaiementService.Ajouter sur commande annulée ou supprimée
        // ================================================================

        /// <summary>PREUVE DU BUG C7 : avant correction, le paiement passait.</summary>
        [Test]
        public void C7_Ajouter_RefuseSurCommandeAnnulee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeAvecPiece(ctx, boss, client, annulee: true);

            var paiement = new Paiement
            {
                IdCommande   = cmd.IdCommande,
                MontantPaye  = 5000m,
                ModePaiement = "Espèces"
            };

            // CORRECTION : doit lever InvalidOperationException
            var ex = Assert.Throws<InvalidOperationException>(() =>
                _paiementService.Ajouter(paiement, boss.IdEmploye, "Boss Test"));

            Assert.That(ex.Message, Does.Contain("annulée"),
                "C7 : le message d'erreur doit mentionner 'annulée'");
        }

        [Test]
        public void C7_Ajouter_RefuseSurCommandeSupprimee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeAvecPiece(ctx, boss, client, supprimee: true);

            var paiement = new Paiement
            {
                IdCommande   = cmd.IdCommande,
                MontantPaye  = 5000m,
                ModePaiement = "Espèces"
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _paiementService.Ajouter(paiement, boss.IdEmploye, "Boss Test"));

            Assert.That(ex.Message, Does.Contain("supprimée"),
                "C7 : le message d'erreur doit mentionner 'supprimée'");
        }

        [Test]
        public void C7_Ajouter_AccepteSurCommandeActive()
        {
            // Vérification de non-régression : une commande active passe normalement
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeAvecPiece(ctx, boss, client, montantCouture: 10000m);

            var paiement = new Paiement
            {
                IdCommande   = cmd.IdCommande,
                MontantPaye  = 5000m,
                ModePaiement = "Espèces"
            };

            // Ne doit PAS lever d'exception
            Assert.DoesNotThrow(() =>
                _paiementService.Ajouter(paiement, boss.IdEmploye, "Boss Test"),
                "C7 : une commande active doit accepter un paiement");
        }

        [Test]
        public void C7_Ajouter_MessageErreur_EnFrancais()
        {
            // Le message doit être en français, comme demandé dans les règles
            using var ctx = _factory.CreateDbContext();
            var boss = CreerBoss(ctx); var client = CreerClient(ctx);
            var (cmd, _) = CreerCommandeAvecPiece(ctx, boss, client, annulee: true);

            var paiement = new Paiement
            {
                IdCommande = cmd.IdCommande, MontantPaye = 1m, ModePaiement = "Espèces"
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _paiementService.Ajouter(paiement, boss.IdEmploye, "Boss Test"));

            // Message entièrement en français
            Assert.That(ex.Message, Does.StartWith("Impossible"),
                "C7 : le message doit commencer par 'Impossible'");
        }
    }
}
