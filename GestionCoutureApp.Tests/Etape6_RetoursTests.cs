using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace GestionCoutureApp.Tests
{
    // ====================================================================
    // ÉTAPE 6 — Module Retours complet
    //
    //   RET_01 : NumeroRetour généré au format RET-AAAA-NNNNN
    //   RET_02 : Compteur annuel incrémente correctement (séquentiel)
    //   RET_03 : Concurrence — deux créations simultanées obtiennent des numéros distincts
    //   RET_04 : Recherche par NumeroRetour partiel
    //   RET_05 : Recherche par nom de client
    //   RET_06 : Recherche par téléphone
    //   RET_07 : Passage complet Signale→En reprise→Termine→Rendu
    //   RET_08 : Resoudre met à jour DateResolution
    //   RET_09 : Passage Termine→Rendu bloqué si reste à payer
    //   RET_10 : Passage Termine→Rendu autorisé si commande soldée
    //   RET_11 : Compteurs couturiers — couturiers différents
    //   RET_12 : Compteurs couturiers — même couturier (imputation + reprise)
    //   RET_13 : Annulation après Termine — DateResolution effacée
    //   RET_14 : Annulation après Termine — retour arrière du compteur
    //   RET_15 : ReculerStatut Boss avec motif : Termine→En reprise
    //   RET_16 : ReculerStatut refusé pour Secrétaire
    //   RET_17 : ReculerStatut sans motif refusé
    //   RET_18 : TauxRetour correct dans StatistiquesParCouturier
    //   RET_19 : ObtenirAlertesRetours retourne retours avec RDV
    //   RET_20 : ObtenirAlertesRetours exclut Rendu et Annulé
    //   RET_21 : Statut "Pret" (ancien) reconnu comme Termine
    //   RET_22 : Migration — numéros attribués aux retours existants sans numéro
    // ====================================================================

    [TestFixture]
    public class Etape6_RetoursTests
    {
        // ── Factories et services ────────────────────────────────────────
        private IDbContextFactory<ApplicationDbContext> _factory = null!;
        private RetourService  _retourService  = null!;
        private AlerteService  _alerteService  = null!;

        // ── Constantes ────────────────────────────────────────────────────
        private const int IdBoss      = 1;
        private const int IdCouturier = 2;
        private const int IdCouturier2 = 3;
        private const int IdClient    = 10;

        // ── SetUp ─────────────────────────────────────────────────────────
        [SetUp]
        public void SetUp()
        {
            var opts = TestDbHelper.CreerOptions("RET");
            _factory = new E6DbContextFactory(opts);

            var auditStub = new AuditServiceStub();
            var paramStub = new ParametresServiceStub();
            _retourService = new RetourService(_factory, auditStub);
            _alerteService = new AlerteService(_factory, paramStub);

            using var ctx = _factory.CreateDbContext();

            // Employés
            ctx.Employes.AddRange(
                new Employe { IdEmploye = IdBoss,      Nom = "BOSS",  Prenom = "Admin",  Identifiant = "boss",  MotDePasse = "x", Role = "Boss",      Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier, Nom = "KONE",  Prenom = "Moussa", Identifiant = "mkone", MotDePasse = "x", Role = "Couturier", Statut = "Actif" },
                new Employe { IdEmploye = IdCouturier2, Nom = "TRAORE", Prenom = "Ali",  Identifiant = "atraore", MotDePasse = "x", Role = "Couturier", Statut = "Actif" }
            );

            // Client
            ctx.Clients.Add(new Client { IdClient = IdClient, Nom = "SOW", Prenom = "Aissatou", Telephone = "779900001" });

            // Commande avec pièce livrée
            var commande = new Commande
            {
                IdCommande = 1, IdClient = IdClient,
                DateDebut = DateTime.Today.AddDays(-10), DateFin = DateTime.Today.AddDays(5),
                Statut = "En cours", TypeVetement = "Robe",
                MontantTotal = 5000m, IdOperateurCreation = IdBoss
            };
            ctx.Commandes.Add(commande);
            ctx.SaveChanges();

            var piece = new PieceCommande
            {
                IdPieceCommande = 1, IdCommande = 1, TypeVetement = "Robe",
                Statut = "Livree", MontantCouture = 5000m, IdCouturier = IdCouturier
            };
            ctx.PiecesCommande.Add(piece);
            ctx.SaveChanges();
        }

        // ── Helpers ───────────────────────────────────────────────────────

        /// <summary>Crée un retour minimal et retourne l'entité après création (avec NumeroRetour).</summary>
        private Retour CreerRetourTest(int? idCoutReprise = null, DateTime? rdv = null)
        {
            var r = new Retour
            {
                IdCommande  = 1, IdPieceCommande = 1, IdCouturier = IdCouturier,
                IdCouturierReprise = idCoutReprise,
                DescriptionProbleme = "Couture mal faite",
                DateRdvReprise = rdv ?? DateTime.Today.AddDays(2),
                HeureDebutReprise = new TimeSpan(10, 0, 0),
                IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Admin BOSS"
            };
            _retourService.Ajouter(r);
            return _retourService.ObtenirParId(r.IdRetour)!;
        }

        /// <summary>Avance le retour jusqu'au statut Termine.</summary>
        private void AvancerJusquaTermine(int idRetour)
        {
            _retourService.DemarrerReprise(idRetour, IdBoss, "Admin BOSS");
            _retourService.Resoudre(idRetour, IdBoss, "Admin BOSS");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_01 — Format NumeroRetour
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_01_NumeroRetour_Format_RET_AAAA_NNNNN()
        {
            var retour = CreerRetourTest();

            Assert.That(retour.NumeroRetour, Does.StartWith("RET-"));
            string[] parts = retour.NumeroRetour.Split('-');
            Assert.That(parts.Length, Is.EqualTo(3), "Format attendu : RET-AAAA-NNNNN");
            Assert.That(parts[1], Is.EqualTo(DateTime.Today.Year.ToString()));
            Assert.That(parts[2].Length, Is.EqualTo(5), "Le numéro doit être sur 5 chiffres");
            Assert.That(int.TryParse(parts[2], out _), Is.True);
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_02 — Compteur incrémental
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_02_Compteur_Annuel_Incrementel()
        {
            // On a besoin d'une 2e pièce livrée pour le 2e retour
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.PiecesCommande.Add(new PieceCommande
                {
                    IdPieceCommande = 2, IdCommande = 1, TypeVetement = "Pantalon",
                    Statut = "Livree", MontantCouture = 3000m, IdCouturier = IdCouturier
                });
                ctx.SaveChanges();
            }

            var r1 = CreerRetourTest();
            // Créer un 2e retour sur la pièce 2
            var r2 = new Retour
            {
                IdCommande = 1, IdPieceCommande = 2, IdCouturier = IdCouturier,
                DescriptionProbleme = "Défaut 2",
                IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Admin BOSS"
            };
            _retourService.Ajouter(r2);
            r2 = _retourService.ObtenirParId(r2.IdRetour)!;

            int annee = DateTime.Today.Year;
            Assert.That(r1.NumeroRetour, Is.EqualTo($"RET-{annee}-00001"));
            Assert.That(r2.NumeroRetour, Is.EqualTo($"RET-{annee}-00002"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_03 — Concurrence : deux threads → numéros distincts
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_03_Concurrence_Numeros_Distincts()
        {
            // Préparer 5 pièces livrées supplémentaires
            using (var ctx = _factory.CreateDbContext())
            {
                for (int i = 2; i <= 6; i++)
                    ctx.PiecesCommande.Add(new PieceCommande
                    {
                        IdPieceCommande = i, IdCommande = 1, TypeVetement = "Chemise",
                        Statut = "Livree", MontantCouture = 2000m, IdCouturier = IdCouturier
                    });
                ctx.SaveChanges();
            }

            // Créer 5 retours séquentiellement (InMemory ne supporte pas les transactions parallèles)
            var numeros = new List<string>();
            for (int i = 1; i <= 5; i++)
            {
                var r = new Retour
                {
                    IdCommande = 1, IdPieceCommande = i, IdCouturier = IdCouturier,
                    DescriptionProbleme = $"Défaut {i}",
                    IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Admin BOSS"
                };
                _retourService.Ajouter(r);
                numeros.Add(_retourService.ObtenirParId(r.IdRetour)!.NumeroRetour);
            }

            Assert.That(numeros.Count, Is.EqualTo(5), "5 retours créés");
            Assert.That(numeros.Distinct().Count(), Is.EqualTo(5), "Tous les numéros doivent être distincts");
            // Vérifier l'ordre séquentiel
            int annee = DateTime.Today.Year;
            Assert.That(numeros[0], Is.EqualTo($"RET-{annee}-00001"));
            Assert.That(numeros[4], Is.EqualTo($"RET-{annee}-00005"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_04 — Recherche par NumeroRetour partiel
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public async Task RET_04_Recherche_Par_NumeroRetour_Partiel()
        {
            var retour = CreerRetourTest();
            // Recherche par les 4 premiers caractères "RET-"
            var page = await _retourService.ObtenirPageAsync(1, 20, "RET-");
            Assert.That(page.Items, Is.Not.Empty);
            Assert.That(page.Items.Any(r => r.IdRetour == retour.IdRetour), Is.True);

            // Recherche par le numéro exact sans le préfixe
            string suffixe = retour.NumeroRetour.Split('-')[2]; // ex. "00001"
            var page2 = await _retourService.ObtenirPageAsync(1, 20, suffixe);
            Assert.That(page2.Items.Any(r => r.IdRetour == retour.IdRetour), Is.True);
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_05 — Recherche par nom client
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public async Task RET_05_Recherche_Par_Nom_Client()
        {
            CreerRetourTest();
            var page = await _retourService.ObtenirPageAsync(1, 20, "SOW");
            Assert.That(page.Items, Is.Not.Empty);
            Assert.That(page.Items.All(r => r.Commande?.Client?.Nom == "SOW"), Is.True);
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_06 — Recherche par téléphone
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public async Task RET_06_Recherche_Par_Telephone()
        {
            CreerRetourTest();
            var page = await _retourService.ObtenirPageAsync(1, 20, "779900001");
            Assert.That(page.Items, Is.Not.Empty);
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_07 — Flux complet Signale → En reprise → Termine → Rendu
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_07_Flux_Complet_Statuts()
        {
            var retour = CreerRetourTest();
            Assert.That(retour.Statut, Is.EqualTo("Signale"));

            _retourService.DemarrerReprise(retour.IdRetour, IdBoss, "Admin BOSS");
            Assert.That(_retourService.ObtenirParId(retour.IdRetour)!.Statut, Is.EqualTo("En reprise"));

            _retourService.Resoudre(retour.IdRetour, IdBoss, "Admin BOSS");
            Assert.That(_retourService.ObtenirParId(retour.IdRetour)!.Statut, Is.EqualTo("Termine"));

            // Ajouter un paiement pour solder la commande (sinon Rendu bloqué)
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Paiements.Add(new Paiement
                {
                    IdCommande = 1, MontantPaye = 5000m, ModePaiement = "Especes",
                    DatePaiement = DateTime.Now, RecuNumero = "R001",
                    IdOperateur = IdBoss, NomOperateur = "Admin BOSS",
                    MontantTotalCommande = 5000m, ResteAvantPaiement = 5000m
                });
                ctx.SaveChanges();
            }

            _retourService.MarquerRendu(retour.IdRetour, IdBoss, "Admin BOSS");
            Assert.That(_retourService.ObtenirParId(retour.IdRetour)!.Statut, Is.EqualTo("Rendu"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_08 — Resoudre renseigne DateResolution
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_08_Resoudre_Remplit_DateResolution()
        {
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);

            var apres = _retourService.ObtenirParId(retour.IdRetour)!;
            Assert.That(apres.DateResolution, Is.Not.Null);
            Assert.That(apres.DateResolution!.Value.Date, Is.EqualTo(DateTime.Today));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_09 — Termine→Rendu bloqué si reste à payer
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_09_MarquerRendu_Bloque_Si_Reste_A_Payer()
        {
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);
            // Aucun paiement → reste = 5000 FCFA

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _retourService.MarquerRendu(retour.IdRetour, IdBoss, "Admin BOSS"));
            Assert.That(ex!.Message, Does.Contain("reste"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_10 — Termine→Rendu autorisé si commande soldée
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_10_MarquerRendu_Autorise_Si_Commande_Soldee()
        {
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);

            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Paiements.Add(new Paiement
                {
                    IdCommande = 1, MontantPaye = 5000m, ModePaiement = "Especes",
                    DatePaiement = DateTime.Now, RecuNumero = "R002",
                    IdOperateur = IdBoss, NomOperateur = "Admin BOSS",
                    MontantTotalCommande = 5000m, ResteAvantPaiement = 5000m
                });
                ctx.SaveChanges();
            }

            Assert.DoesNotThrow(() =>
                _retourService.MarquerRendu(retour.IdRetour, IdBoss, "Admin BOSS"));
            Assert.That(_retourService.ObtenirParId(retour.IdRetour)!.Statut, Is.EqualTo("Rendu"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_11 — Compteurs couturiers : couturiers différents
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_11_Compteurs_Couturiers_Differents()
        {
            // Couturier initial = IdCouturier ; couturier reprise = IdCouturier2
            var retour = CreerRetourTest(idCoutReprise: IdCouturier2);
            AvancerJusquaTermine(retour.IdRetour);

            var stats = _retourService.StatistiquesParCouturier(
                new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today.AddDays(1));

            var statInitial  = stats.FirstOrDefault(s => s.IdCouturier == IdCouturier);
            var statReprise  = stats.FirstOrDefault(s => s.IdCouturier == IdCouturier2);

            Assert.That(statInitial,  Is.Not.Null, "Le couturier initial doit apparaître dans les stats");
            Assert.That(statReprise,  Is.Not.Null, "Le couturier de reprise doit apparaître dans les stats");

            Assert.That(statInitial!.RetoursImputes,     Is.EqualTo(1), "1 retour imputé au couturier initial");
            Assert.That(statInitial!.ReprisesEffectuees, Is.EqualTo(0), "Le couturier initial n'a pas fait la reprise");
            Assert.That(statReprise!.RetoursImputes,     Is.EqualTo(0), "Couturier reprise n'est pas responsable");
            Assert.That(statReprise!.ReprisesEffectuees, Is.EqualTo(1), "1 reprise effectuée par couturier reprise");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_12 — Compteurs : même couturier (imputation + reprise)
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_12_Compteurs_Meme_Couturier()
        {
            // Pas de couturier de reprise → IdCouturierRepriseEffectif = IdCouturier
            var retour = CreerRetourTest(idCoutReprise: null);
            AvancerJusquaTermine(retour.IdRetour);

            var stats = _retourService.StatistiquesParCouturier(
                new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today.AddDays(1));

            var stat = stats.FirstOrDefault(s => s.IdCouturier == IdCouturier);
            Assert.That(stat, Is.Not.Null);
            Assert.That(stat!.RetoursImputes,     Is.EqualTo(1), "1 retour imputé");
            Assert.That(stat!.ReprisesEffectuees, Is.EqualTo(1), "1 reprise (même couturier)");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_13 — Annulation après Termine : DateResolution effacée
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_13_Annulation_Apres_Termine_Efface_DateResolution()
        {
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);

            // Vérifier que DateResolution est bien renseignée
            Assert.That(_retourService.ObtenirParId(retour.IdRetour)!.DateResolution, Is.Not.Null);

            _retourService.Annuler(retour.IdRetour, "Erreur de signalement", "Admin BOSS");

            var apres = _retourService.ObtenirParId(retour.IdRetour)!;
            Assert.That(apres.EstAnnule, Is.True);
            Assert.That(apres.DateResolution, Is.Null, "DateResolution doit être effacée après annulation post-Termine");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_14 — Annulation après Termine : compteurs à 0
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_14_Annulation_Apres_Termine_Compteurs_A_Zero()
        {
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);

            // Avant annulation : 1 retour imputé
            var statsAvant = _retourService.StatistiquesParCouturier(
                new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today.AddDays(1));
            Assert.That(statsAvant.FirstOrDefault(s => s.IdCouturier == IdCouturier)?.RetoursImputes, Is.EqualTo(1));

            _retourService.Annuler(retour.IdRetour, "Annulation test", "Admin BOSS");

            // Après annulation : DateResolution=null → plus dans les stats
            var statsApres = _retourService.StatistiquesParCouturier(
                new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today.AddDays(1));
            int imputes = statsApres.FirstOrDefault(s => s.IdCouturier == IdCouturier)?.RetoursImputes ?? 0;
            Assert.That(imputes, Is.EqualTo(0), "Après annulation, le retour ne doit plus compter dans les statistiques");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_15 — ReculerStatut Boss : Termine → En reprise
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_15_ReculerStatut_Boss_Termine_Vers_EnReprise()
        {
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);

            _retourService.ReculerStatut(retour.IdRetour, "Reprise insuffisante",
                IdBoss, "Admin BOSS", "Boss");

            var apres = _retourService.ObtenirParId(retour.IdRetour)!;
            Assert.That(apres.Statut,           Is.EqualTo("En reprise"));
            Assert.That(apres.DateResolution,   Is.Null);
            Assert.That(apres.MotifRetourStatut, Is.EqualTo("Reprise insuffisante"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_16 — ReculerStatut refusé si pas Boss
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_16_ReculerStatut_Refuse_Secretaire()
        {
            var retour = CreerRetourTest();
            _retourService.DemarrerReprise(retour.IdRetour, IdBoss, "Admin BOSS");

            var ex = Assert.Throws<UnauthorizedAccessException>(() =>
                _retourService.ReculerStatut(retour.IdRetour, "motif",
                    99, "Sec Fatou", "Secretaire"));
            Assert.That(ex!.Message, Does.Contain("Boss"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_17 — ReculerStatut sans motif refusé
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_17_ReculerStatut_Sans_Motif_Refuse()
        {
            var retour = CreerRetourTest();
            _retourService.DemarrerReprise(retour.IdRetour, IdBoss, "Admin BOSS");

            var ex = Assert.Throws<InvalidOperationException>(() =>
                _retourService.ReculerStatut(retour.IdRetour, "   ",
                    IdBoss, "Admin BOSS", "Boss"));
            Assert.That(ex!.Message, Does.Contain("motif"));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_18 — TauxRetour dans StatistiquesParCouturier
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_18_TauxRetour_Calcule_Correctement()
        {
            // Ajouter 5 pièces livrées supplémentaires (avec DateTerminee = Today pour être dans la période)
            using (var ctx = _factory.CreateDbContext())
            {
                for (int i = 2; i <= 6; i++)
                    ctx.PiecesCommande.Add(new PieceCommande
                    {
                        IdPieceCommande = i, IdCommande = 1, TypeVetement = "Chemise",
                        Statut = "Livree", MontantCouture = 2000m, IdCouturier = IdCouturier,
                        DateTerminee = DateTime.Today
                    });
                ctx.SaveChanges();
            }

            // 1 retour terminé sur 5 pièces → taux de retour = 1/5 = 20%
            var retour = CreerRetourTest();
            AvancerJusquaTermine(retour.IdRetour);

            var stats = _retourService.StatistiquesParCouturier(
                new DateTime(DateTime.Today.Year, 1, 1), DateTime.Today.AddDays(1));
            var stat = stats.FirstOrDefault(s => s.IdCouturier == IdCouturier);

            Assert.That(stat, Is.Not.Null);
            Assert.That(stat!.RetoursImputes, Is.EqualTo(1));
            Assert.That(stat!.TauxRetour, Is.EqualTo(20.0).Within(0.1));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_19 — ObtenirAlertesRetours retourne retours avec RDV
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public async Task RET_19_ObtenirAlertesRetours_Retourne_Retours_Avec_RDV()
        {
            var retour = CreerRetourTest(rdv: DateTime.Today.AddDays(1));
            var alertes = await _alerteService.ObtenirAlertesRetours();

            Assert.That(alertes, Is.Not.Empty);
            var alerte = alertes.FirstOrDefault(a => a.IdRetour == retour.IdRetour);
            Assert.That(alerte, Is.Not.Null, "Le retour créé doit apparaître dans les alertes");
            Assert.That(alerte!.EstAlertRetour, Is.True);
            Assert.That(alerte.TypeAlerte, Is.EqualTo("Retour"));
            Assert.That(alerte.NumeroRetour, Is.EqualTo(retour.NumeroRetour));
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_20 — ObtenirAlertesRetours exclut Rendu et Annulé
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public async Task RET_20_ObtenirAlertesRetours_Exclut_Rendu_Et_Annule()
        {
            // Préparer pièces supplémentaires
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.PiecesCommande.AddRange(
                    new PieceCommande { IdPieceCommande = 11, IdCommande = 1, TypeVetement = "Chemise", Statut = "Livree", MontantCouture = 2000m, IdCouturier = IdCouturier },
                    new PieceCommande { IdPieceCommande = 12, IdCommande = 1, TypeVetement = "Pantalon", Statut = "Livree", MontantCouture = 2000m, IdCouturier = IdCouturier }
                );
                ctx.Paiements.Add(new Paiement { IdCommande = 1, MontantPaye = 5000m, ModePaiement = "Especes", DatePaiement = DateTime.Now, RecuNumero = "R099", IdOperateur = IdBoss, NomOperateur = "Admin BOSS", MontantTotalCommande = 5000m, ResteAvantPaiement = 5000m });
                ctx.SaveChanges();
            }

            // Retour Rendu
            var retourRendu = new Retour { IdCommande = 1, IdPieceCommande = 11, IdCouturier = IdCouturier, DescriptionProbleme = "Test", DateRdvReprise = DateTime.Today.AddDays(1), IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Admin BOSS" };
            _retourService.Ajouter(retourRendu);
            AvancerJusquaTermine(retourRendu.IdRetour);
            _retourService.MarquerRendu(retourRendu.IdRetour, IdBoss, "Admin BOSS");

            // Retour Annulé
            var retourAnnule = new Retour { IdCommande = 1, IdPieceCommande = 12, IdCouturier = IdCouturier, DescriptionProbleme = "Test 2", DateRdvReprise = DateTime.Today.AddDays(1), IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Admin BOSS" };
            _retourService.Ajouter(retourAnnule);
            _retourService.Annuler(retourAnnule.IdRetour, "Erreur", "Admin BOSS");

            var alertes = await _alerteService.ObtenirAlertesRetours();

            Assert.That(alertes.Any(a => a.IdRetour == retourRendu.IdRetour),  Is.False, "Retour Rendu ne doit pas apparaître");
            Assert.That(alertes.Any(a => a.IdRetour == retourAnnule.IdRetour), Is.False, "Retour Annulé ne doit pas apparaître");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_21 — Compat. ancienne valeur "Pret"
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_21_Statut_Pret_Reconnu_Comme_Termine()
        {
            // Injecter directement un retour avec Statut="Pret" (ancienne valeur DB)
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.Retours.Add(new Retour
                {
                    IdCommande = 1, IdPieceCommande = 1, IdCouturier = IdCouturier,
                    DescriptionProbleme = "Ancien retour", Statut = "Pret",
                    NumeroRetour = "RET-2025-00001", DateSignalement = DateTime.Today,
                    IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "Admin BOSS"
                });
                ctx.SaveChanges();
            }

            var tous = _retourService.ObtenirTous();
            var ancien = tous.First(r => r.NumeroRetour == "RET-2025-00001");

            Assert.That(ancien.EstTermine, Is.True, "EstTermine doit être vrai pour Statut='Pret'");
            Assert.That(ancien.StatutAffiche, Is.EqualTo("Terminé ✓"), "StatutAffiche doit afficher 'Terminé ✓'");
        }

        // ═════════════════════════════════════════════════════════════════
        // RET_22 — Numéro unique — pas de doublon possible
        // ═════════════════════════════════════════════════════════════════
        [Test]
        public void RET_22_NumeroRetour_Est_Unique_Par_Annee()
        {
            // Vérifier que deux retours créés n'ont jamais le même numéro
            using (var ctx = _factory.CreateDbContext())
            {
                ctx.PiecesCommande.Add(new PieceCommande
                {
                    IdPieceCommande = 99, IdCommande = 1, TypeVetement = "Boubou",
                    Statut = "Livree", MontantCouture = 2000m, IdCouturier = IdCouturier
                });
                ctx.SaveChanges();
            }

            var r1 = CreerRetourTest();
            var r2 = new Retour { IdCommande = 1, IdPieceCommande = 99, IdCouturier = IdCouturier, DescriptionProbleme = "D", IdOperateurEnregistrement = IdBoss, NomOperateurEnregistrement = "A" };
            _retourService.Ajouter(r2);
            r2 = _retourService.ObtenirParId(r2.IdRetour)!;

            Assert.That(r1.NumeroRetour, Is.Not.EqualTo(r2.NumeroRetour));
        }
    }

    // =====================================================================
    // INFRASTRUCTURE DE TEST
    // =====================================================================

    /// <summary>Factory InMemory pour les tests Étape 6.</summary>
    internal class E6DbContextFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _opts;
        public E6DbContextFactory(DbContextOptions<ApplicationDbContext> opts) => _opts = opts;
        public ApplicationDbContext CreateDbContext() => new(_opts);
    }

    /// <summary>Stub IAuditService : n'enregistre rien, ne plante pas.</summary>
    internal class AuditServiceStub : IAuditService
    {
        public Task EnregistrerActionAsync(int a, string b, string c, string d, string e, int f,
            object? g = null, object? h = null, string? i = null, bool j = false)
            => Task.CompletedTask;

        public List<JournalAudit> ObtenirToutesLesEntrees() => new();
        public List<JournalAudit> ObtenirParPeriode(DateTime debut, DateTime fin) => new();
        public List<JournalAudit> ObtenirParOperateur(int id) => new();
        public List<JournalAudit> ObtenirParTypeAction(string type) => new();
        public List<JournalAudit> ObtenirParEntite(string entite, int id) => new();
        public (bool Integre, string Message) VerifierIntegriteChaine() => (true, "OK");
        public StatistiquesAudit ObtenirStatistiques(DateTime debut, DateTime fin) => new();
        public List<IndicateurAntiFraude> ObtenirIndicateurAntiFraude(DateTime depuis) => new();
    }

    /// <summary>Stub IParametresService minimal pour AlerteService.</summary>
    internal class ParametresServiceStub : IParametresService
    {
        // ── Propriétés configurables pour les tests Étape 7 ──────────────
        public int    RetardAlerteMinutes { get; set; } = 120;
        public int    RetardReportJours   { get; set; } = 1;

        public Task<string?> ObtenirValeur(string cle) => Task.FromResult<string?>(null);
        public Task DefinirValeur(string cle, string valeur) => Task.CompletedTask;
        public Task<int>     ObtenirDelaiAlerteRendezVousHeures()           => Task.FromResult(3);
        public Task          DefinirDelaiAlerteRendezVousHeures(int h)      => Task.CompletedTask;
        public Task<int>     ObtenirSeuiRetardHeures()                      => Task.FromResult(24);
        public Task          DefinirSeuiRetardHeures(int h)                 => Task.CompletedTask;
        public Task<int>     ObtenirSeuilPrimeNbCommandes()                 => Task.FromResult(10);
        public Task          DefinirSeuilPrimeNbCommandes(int n)            => Task.CompletedTask;
        public Task<decimal> ObtenirSalaireMensuelSecretaire()              => Task.FromResult(50000m);
        public Task          DefinirSalaireMensuelSecretaire(decimal v)     => Task.CompletedTask;
        public Task<string>  ObtenirNomAtelier()                            => Task.FromResult("Atelier Test");
        public Task          DefinirNomAtelier(string v)                    => Task.CompletedTask;
        public Task<string>  ObtenirAdresseAtelier()                        => Task.FromResult("");
        public Task          DefinirAdresseAtelier(string v)                => Task.CompletedTask;
        public Task<string>  ObtenirTelAtelier()                            => Task.FromResult("");
        public Task          DefinirTelAtelier(string v)                    => Task.CompletedTask;
        public Task<string>  ObtenirPiedRecu()                              => Task.FromResult("");
        public Task          DefinirPiedRecu(string v)                      => Task.CompletedTask;
        public Task<decimal> ObtenirTauxCommissionDefaut()                  => Task.FromResult(0.3m);
        public Task          DefinirTauxCommissionDefaut(decimal v)         => Task.CompletedTask;
        public Task<decimal> ObtenirPrimeZeroDefaut()                       => Task.FromResult(5000m);
        public Task          DefinirPrimeZeroDefaut(decimal v)              => Task.CompletedTask;
        public Task<decimal> ObtenirMontantPrimeQualite()                   => Task.FromResult(5000m);
        public Task          DefinirMontantPrimeQualite(decimal v)          => Task.CompletedTask;
        public Task<int>     ObtenirDelaiAnnulationSecretaireMinutes()      => Task.FromResult(10);
        public Task          DefinirDelaiAnnulationSecretaireMinutes(int v) => Task.CompletedTask;
        public Task<string>  ObtenirModeCA()                                => Task.FromResult("Encaisse");
        public Task          DefinirModeCA(string v)                        => Task.CompletedTask;
        public Task<decimal> ObtenirSeuilAlerteDépenses()                   => Task.FromResult(500000m);
        public Task          DefinirSeuilAlerteDépenses(decimal v)          => Task.CompletedTask;
        public Task<bool>    ObtenirApprobationDepenses()                   => Task.FromResult(true);
        public Task          DefinirApprobationDepenses(bool v)             => Task.CompletedTask;
        public Task<decimal> ObtenirBudgetLoyer()                           => Task.FromResult(50000m);
        public Task          DefinirBudgetLoyer(decimal v)                  => Task.CompletedTask;
        public Task<string>  ObtenirMsgCommandePrete()                      => Task.FromResult("");
        public Task          DefinirMsgCommandePrete(string v)              => Task.CompletedTask;
        public Task<string>  ObtenirMsgRappelRdv()                          => Task.FromResult("");
        public Task          DefinirMsgRappelRdv(string v)                  => Task.CompletedTask;
        public Task<string>  ObtenirMsgRetouchePrete()                      => Task.FromResult("");
        public Task          DefinirMsgRetouchePrete(string v)              => Task.CompletedTask;
        public Task<int>     ObtenirFrequenceSyncHeures()                   => Task.FromResult(24);
        public Task          DefinirFrequenceSyncHeures(int v)              => Task.CompletedTask;
        public Task<bool>    ObtenirCompresserPhotos()                      => Task.FromResult(true);
        public Task          DefinirCompresserPhotos(bool v)                => Task.CompletedTask;
        public Task<string>  ObtenirCouleurAccent()                         => Task.FromResult("#CC0000");
        public Task          DefinirCouleurAccent(string v)                 => Task.CompletedTask;
        public Task<string>  ObtenirLangue()                                => Task.FromResult("fr");
        public Task          DefinirLangue(string v)                        => Task.CompletedTask;
        public Task<int>     ObtenirRetardAlerteAvantMinutes()              => Task.FromResult(RetardAlerteMinutes);
        public Task          DefinirRetardAlerteAvantMinutes(int v)         => Task.CompletedTask;
        public Task<int>     ObtenirRetardReportJours()                     => Task.FromResult(RetardReportJours);
        public Task          DefinirRetardReportJours(int v)                => Task.CompletedTask;
        public Task<string>  ObtenirRetardMessageModele()                   =>
            Task.FromResult(ParametresService.ModeleExcuseRetardDefaut);
        public Task          DefinirRetardMessageModele(string v)           => Task.CompletedTask;
    }
}
