// =======================================================================
// ÉTAPE 3 — Tests KPI Dashboard
//
// D2  : ChargerCartesStats — équivalence ancienne logique vs nouvelle
//       (projection légère au lieu de chargement complet)
//
// D3  : ChargerStatsCouturiers — PREUVE du bug CommandeEstValide
//       (commandes annulées/supprimées ne doivent PAS peser sur la charge)
//       + test de la correction
//
// D7  : Bornes de dates — borne exclusive < début du lendemain
//       (éviter d'inclure 00:00:01 du lendemain)
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
    public class Etape3_DashboardKpiTests
    {
        private IDbContextFactory<ApplicationDbContext> _factory = null!;

        private class Dash3Factory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _opts;
            public Dash3Factory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
            public ApplicationDbContext CreateDbContext() => new ApplicationDbContext(_opts);
        }

        [SetUp]
        public void Init()
        {
            var opts = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase("dash3_" + Guid.NewGuid())
                .ConfigureWarnings(w => w.Ignore(InMemoryEventId.TransactionIgnoredWarning))
                .Options;
            _factory = new Dash3Factory(opts);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static Employe CreerEmploye(ApplicationDbContext ctx,
            string role = "Boss", string statut = "Actif")
        {
            var e = new Employe
            {
                Prenom = "Emp", Nom = "Test", Identifiant = "e_" + Guid.NewGuid(),
                Role = role, Statut = statut, MotDePasse = "hash"
            };
            ctx.Employes.Add(e);
            ctx.SaveChanges();
            return e;
        }

        private static Client CreerClient(ApplicationDbContext ctx)
        {
            var c = new Client { Prenom = "Jean", Nom = "Dupont", Telephone = "0600" };
            ctx.Clients.Add(c);
            ctx.SaveChanges();
            return c;
        }

        private static (Commande, PieceCommande) CreerCommande(
            ApplicationDbContext ctx, Employe boss, Client client,
            string statut = "A faire", bool supprimee = false, bool annulee = false,
            DateTime? dateFin = null, int? idCouturier = null)
        {
            var cmd = new Commande
            {
                IdClient             = client.IdClient,
                DateDebut            = DateTime.Today,
                DateFin              = dateFin ?? DateTime.Today.AddDays(7),
                IdOperateurCreation  = boss.IdEmploye,
                NomOperateurCreation = "Boss",
                EstSupprimee         = supprimee,
                EstAnnulee           = annulee
            };
            ctx.Commandes.Add(cmd);
            ctx.SaveChanges();

            var piece = new PieceCommande
            {
                IdCommande     = cmd.IdCommande,
                TypeVetement   = "Robe",
                MontantCouture = 10000m,
                Statut         = statut,
                IdCouturier    = idCouturier
            };
            ctx.PiecesCommande.Add(piece);
            ctx.SaveChanges();
            return (cmd, piece);
        }

        // ================================================================
        // D2 — Équivalence KPI commandes en cours et retards
        // ================================================================

        [Test]
        public void D2_EnCours_ExclutCommandesLivrees()
        {
            // Arrange : 3 actives (A faire, En cours, Terminée), 1 livrée
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerEmploye(ctx);
            var client = CreerClient(ctx);
            CreerCommande(ctx, boss, client, "A faire");
            CreerCommande(ctx, boss, client, "En cours");
            CreerCommande(ctx, boss, client, "Terminee");
            CreerCommande(ctx, boss, client, "Livree");

            // Simulation de la logique du DTO (ancienne + nouvelle identiques)
            var commandesLight = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new
                {
                    c.DateFin,
                    Statuts = c.Pieces.Select(p => p.Statut).ToList()
                })
                .ToList();

            int enCours = commandesLight.Count(c =>
                c.Statuts.Count == 0 || c.Statuts.Any(s => s != "Livree"));

            // Assert : 3 en cours (pas la livrée)
            Assert.That(enCours, Is.EqualTo(3));
        }

        [Test]
        public void D2_Retards_IncluteCommandeEnRetard()
        {
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerEmploye(ctx);
            var client = CreerClient(ctx);
            // Commande en retard : DateFin passée, statut non livré
            CreerCommande(ctx, boss, client, "En cours", dateFin: DateTime.Today.AddDays(-2));
            // Commande dans les délais
            CreerCommande(ctx, boss, client, "A faire",  dateFin: DateTime.Today.AddDays(5));

            var commandesLight = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new { c.DateFin, Statuts = c.Pieces.Select(p => p.Statut).ToList() })
                .ToList();

            var aujourdhui = DateTime.Today;
            int retards = commandesLight.Count(c =>
                (c.Statuts.Count == 0 || c.Statuts.Any(s => s != "Livree")) &&
                c.DateFin != default && c.DateFin.Date < aujourdhui);

            Assert.That(retards, Is.EqualTo(1));
        }

        [Test]
        public void D2_EnCours_ExclutCommandesSupprimees()
        {
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerEmploye(ctx);
            var client = CreerClient(ctx);
            CreerCommande(ctx, boss, client, "En cours", supprimee: true);
            CreerCommande(ctx, boss, client, "A faire");

            var commandesLight = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new { c.DateFin, Statuts = c.Pieces.Select(p => p.Statut).ToList() })
                .ToList();

            int enCours = commandesLight.Count(c =>
                c.Statuts.Count == 0 || c.Statuts.Any(s => s != "Livree"));

            Assert.That(enCours, Is.EqualTo(1), "La commande supprimée ne doit pas être comptée");
        }

        [Test]
        public void D2_EnCours_ExclutCommandesAnnulees()
        {
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerEmploye(ctx);
            var client = CreerClient(ctx);
            CreerCommande(ctx, boss, client, "En cours", annulee: true);
            CreerCommande(ctx, boss, client, "A faire");

            var commandesLight = ctx.Commandes.AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new { c.DateFin, Statuts = c.Pieces.Select(p => p.Statut).ToList() })
                .ToList();

            int enCours = commandesLight.Count(c =>
                c.Statuts.Count == 0 || c.Statuts.Any(s => s != "Livree"));

            Assert.That(enCours, Is.EqualTo(1), "La commande annulée ne doit pas être comptée");
        }

        // ================================================================
        // D3 — PREUVE du bug (ancienne logique) + test de la correction
        // ================================================================

        /// <summary>
        /// PREUVE DU BUG D3 (ancienne logique) :
        /// Une pièce appartenant à une commande ANNULÉE était comptée dans la
        /// charge du couturier parce que CommandeEstValide = p.Commande != null
        /// (sans vérifier EstSupprimee / EstAnnulee).
        /// </summary>
        [Test]
        public void D3_Bug_AncienneLogique_ComptePiecesCommandeAnnulee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss     = CreerEmploye(ctx);
            var client   = CreerClient(ctx);
            var couturier = CreerEmploye(ctx, "Couturier");

            // Commande ANNULÉE avec une pièce assignée au couturier
            CreerCommande(ctx, boss, client, "A faire", annulee: true,
                idCouturier: couturier.IdEmploye);

            // ── Ancienne logique (avec le bug) ──
            var piecesAncienne = ctx.PiecesCommande.AsNoTracking()
                .Where(p => p.IdCouturier.HasValue)
                .Select(p => new
                {
                    p.IdCouturier,
                    p.Statut,
                    // BUG : CommandeEstValide ne vérifie pas EstSupprimee / EstAnnulee
                    CommandeEstValide = p.Commande != null
                })
                .ToList();

            int chargeAvecBug = piecesAncienne
                .Where(p => p.IdCouturier == couturier.IdEmploye
                         && p.Statut != "Livree"
                         && p.CommandeEstValide)
                .Count();

            // PREUVE : l'ancienne logique compte 1 pièce pour la commande annulée
            Assert.That(chargeAvecBug, Is.EqualTo(1),
                "PREUVE BUG D3 : l'ancienne logique comptait la pièce d'une commande annulée");
        }

        /// <summary>
        /// Test de la CORRECTION D3 :
        /// CommandeEstValide doit exclure les commandes annulées ET supprimées.
        /// </summary>
        [Test]
        public void D3_Correction_ExcluePiecesCommandeAnnulee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss     = CreerEmploye(ctx);
            var client   = CreerClient(ctx);
            var couturier = CreerEmploye(ctx, "Couturier");

            // Commande ANNULÉE avec une pièce
            CreerCommande(ctx, boss, client, "A faire", annulee: true,
                idCouturier: couturier.IdEmploye);
            // Commande ACTIVE avec une pièce
            CreerCommande(ctx, boss, client, "En cours",
                idCouturier: couturier.IdEmploye);

            // ── Nouvelle logique corrigée ──
            var piecesCorrigees = ctx.PiecesCommande.AsNoTracking()
                .Where(p => p.IdCouturier.HasValue)
                .Select(p => new
                {
                    p.IdCouturier,
                    p.Statut,
                    // CORRECTION : exclure les commandes annulées ET supprimées
                    CommandeEstValide = p.Commande != null
                                        && !p.Commande.EstSupprimee
                                        && !p.Commande.EstAnnulee
                })
                .ToList();

            int chargeCorrigee = piecesCorrigees
                .Where(p => p.IdCouturier == couturier.IdEmploye
                         && p.Statut != "Livree"
                         && p.CommandeEstValide)
                .Count();

            // CORRECTION : seule la commande active est comptée
            Assert.That(chargeCorrigee, Is.EqualTo(1),
                "Après correction D3 : seule la pièce de la commande active doit être comptée");
        }

        [Test]
        public void D3_Correction_ExcluePiecesCommandeSupprimee()
        {
            using var ctx = _factory.CreateDbContext();
            var boss     = CreerEmploye(ctx);
            var client   = CreerClient(ctx);
            var couturier = CreerEmploye(ctx, "Couturier");

            // Commande SUPPRIMÉE avec une pièce
            CreerCommande(ctx, boss, client, "En cours", supprimee: true,
                idCouturier: couturier.IdEmploye);
            // Commande ACTIVE avec une pièce
            CreerCommande(ctx, boss, client, "A faire",
                idCouturier: couturier.IdEmploye);

            var piecesCorrigees = ctx.PiecesCommande.AsNoTracking()
                .Where(p => p.IdCouturier.HasValue)
                .Select(p => new
                {
                    p.IdCouturier,
                    p.Statut,
                    CommandeEstValide = p.Commande != null
                                        && !p.Commande.EstSupprimee
                                        && !p.Commande.EstAnnulee
                })
                .ToList();

            int chargeCorrigee = piecesCorrigees
                .Where(p => p.IdCouturier == couturier.IdEmploye
                         && p.Statut != "Livree"
                         && p.CommandeEstValide)
                .Count();

            Assert.That(chargeCorrigee, Is.EqualTo(1),
                "La pièce d'une commande supprimée ne doit pas peser sur la charge");
        }

        [Test]
        public void D3_NbRetards_ExcluePiecesCommandeAnnulee()
        {
            // Vérifie aussi que les retards n'incluent pas les commandes annulées
            using var ctx = _factory.CreateDbContext();
            var boss     = CreerEmploye(ctx);
            var client   = CreerClient(ctx);
            var couturier = CreerEmploye(ctx, "Couturier");
            var hier = DateTime.Today.AddDays(-1);

            // Commande ANNULÉE en retard
            CreerCommande(ctx, boss, client, "En cours", annulee: true,
                dateFin: hier, idCouturier: couturier.IdEmploye);
            // Commande ACTIVE en retard
            CreerCommande(ctx, boss, client, "A faire",
                dateFin: hier, idCouturier: couturier.IdEmploye);

            var pieces = ctx.PiecesCommande.AsNoTracking()
                .Where(p => p.IdCouturier == couturier.IdEmploye)
                .Select(p => new
                {
                    p.IdCommande,
                    p.Statut,
                    CommandeDateFin   = p.Commande != null ? p.Commande.DateFin : (DateTime?)null,
                    CommandeEstValide = p.Commande != null
                                        && !p.Commande.EstSupprimee
                                        && !p.Commande.EstAnnulee
                })
                .ToList();

            int nbRetards = pieces
                .Where(p => p.Statut != "Livree"
                         && p.CommandeEstValide
                         && p.CommandeDateFin.HasValue
                         && p.CommandeDateFin.Value.Date < DateTime.Today)
                .Select(p => p.IdCommande)
                .Distinct()
                .Count();

            Assert.That(nbRetards, Is.EqualTo(1),
                "Seule la commande active en retard doit compter dans les retards du couturier");
        }

        // ================================================================
        // D7 — Bornes de dates exclusives
        // ================================================================

        [Test]
        public void D7_BorneMois_ExcluePaiementPremierJourMoisSuivant()
        {
            // Vérifie que le premier paiement du mois suivant n'est PAS inclus
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerEmploye(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommande(ctx, boss, client);

            var debutMois     = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);
            var debutMoisSuiv = debutMois.AddMonths(1);

            // Paiement à exactement 00:00:00 du 1er du mois suivant
            ctx.Paiements.Add(new Paiement
            {
                IdCommande   = cmd.IdCommande,
                MontantPaye  = 5000m,
                ModePaiement = "Espèces",
                DatePaiement = debutMoisSuiv,   // minuit du mois suivant
                IdOperateur  = boss.IdEmploye,
                NomOperateur = "Boss",
                RecuNumero   = "REC-TEST-D7-01"
            });
            ctx.SaveChanges();

            // Nouvelle logique (borne exclusive < debutMoisSuiv)
            decimal caNouveauLogique = ctx.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= debutMois && p.DatePaiement < debutMoisSuiv && !p.EstAnnule)
                .Select(p => p.MontantPaye)
                .AsEnumerable()
                .Sum();

            // Ancienne logique (borne inclusive <= finMois.AddDays(1))
            var finMois = debutMoisSuiv.AddDays(-1);
            decimal caAncienneLogique = ctx.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= debutMois && p.DatePaiement <= finMois.AddDays(1) && !p.EstAnnule)
                .Select(p => p.MontantPaye)
                .AsEnumerable()
                .Sum();

            // La nouvelle logique exclut ce paiement (00:00:00 du lendemain)
            Assert.That(caNouveauLogique, Is.EqualTo(0m),
                "D7 : le paiement à minuit du mois suivant ne doit PAS être inclus dans le CA du mois");

            // L'ancienne logique l'incluait (c'est le bug)
            Assert.That(caAncienneLogique, Is.EqualTo(5000m),
                "PREUVE BUG D7 : l'ancienne logique incluait le paiement de minuit du lendemain");
        }

        [Test]
        public void D7_BorneGraphique_ExcluePaiementMidnightDemain()
        {
            using var ctx = _factory.CreateDbContext();
            var boss   = CreerEmploye(ctx);
            var client = CreerClient(ctx);
            var (cmd, _) = CreerCommande(ctx, boss, client);

            var borneHaute = DateTime.Today.AddDays(1); // début de demain = minuit demain

            // Paiement exactement à minuit demain
            ctx.Paiements.Add(new Paiement
            {
                IdCommande   = cmd.IdCommande,
                MontantPaye  = 3000m,
                ModePaiement = "Mobile Money",
                DatePaiement = borneHaute,   // 00:00:00 demain
                IdOperateur  = boss.IdEmploye,
                NomOperateur = "Boss",
                RecuNumero   = "REC-TEST-D7-02"
            });
            ctx.SaveChanges();

            var debut7j = DateTime.Today.AddDays(-6);

            // Nouvelle logique (borne exclusive < borneHaute)
            decimal caNouveau = ctx.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= debut7j && p.DatePaiement < borneHaute && !p.EstAnnule)
                .Select(p => p.MontantPaye).AsEnumerable().Sum();

            // Ancienne logique (borne inclusive <= fin7j.AddDays(1))
            var fin7j = DateTime.Today;
            decimal caAncien = ctx.Paiements
                .AsNoTracking()
                .Where(p => p.DatePaiement >= debut7j && p.DatePaiement <= fin7j.AddDays(1) && !p.EstAnnule)
                .Select(p => p.MontantPaye).AsEnumerable().Sum();

            Assert.That(caNouveau, Is.EqualTo(0m),
                "D7 : le paiement à minuit demain ne doit PAS figurer dans le graphique des 7 jours");
            Assert.That(caAncien, Is.EqualTo(3000m),
                "PREUVE BUG D7 : l'ancienne logique incluait le paiement de minuit demain");
        }
    }
}
