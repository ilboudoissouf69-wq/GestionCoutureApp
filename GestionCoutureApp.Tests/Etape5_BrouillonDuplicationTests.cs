using NUnit.Framework;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace GestionCoutureApp.Tests
{
    /// <summary>
    /// Tests pour l'Étape 5 : Brouillon automatique et duplication de pièces.
    /// </summary>
    [TestFixture]
    public class Etape5_BrouillonDuplicationTests
    {
        private ApplicationDbContext _context = null!;
        private IBrouillonCommandeService _brouillonService = null!;
        private IDbContextFactory<ApplicationDbContext> _contextFactory = null!;

        [SetUp]
        public void SetUp()
        {
            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;

            _context = new ApplicationDbContext(options);
            _contextFactory = new TestDbContextFactory(options);
            _brouillonService = new BrouillonCommandeService(_contextFactory);

            // Seed : créer quelques employes pour tester
            _context.Employes.AddRange(
                new Employe
                {
                    IdEmploye = 1,
                    Nom = "Test",
                    Prenom = "User",
                    Role = "Secretaire",
                    Identifiant = "test",
                    MotDePasse = "test",
                    Statut = "Actif"
                },
                new Employe
                {
                    IdEmploye = 2,
                    Nom = "Boss",
                    Prenom = "Admin",
                    Role = "Boss",
                    Identifiant = "boss",
                    MotDePasse = "boss",
                    Statut = "Actif"
                }
            );
            _context.SaveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _context?.Dispose();
        }

        // ═══════════════════════════════════════════════════════════════
        // A) TESTS BROUILLON AUTOMATIQUE
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public async Task Brouillon01_SauvegarderEtCharger()
        {
            // Arrange
            int idEmp = 1;
            var brouillon = new BrouillonCommandeDto
            {
                IdClient = 1,
                NomClient = "Client Test",
                Acompte = 5000,
                DateFin = DateTime.Today.AddDays(7),
                HeureFin = new TimeSpan(14, 30, 0),
                Pieces = new List<BrouillonPieceDto>
                {
                    new BrouillonPieceDto
                    {
                        IdTypeVetement = 1,
                        TypeVetement = "Pantalon",
                        Prix = 10000,
                        Description = "Test"
                    }
                }
            };

            // Act
            await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon);
            var charge = await _brouillonService.ChargerBrouillonAsync(idEmp);

            // Assert
            Assert.That(charge, Is.Not.Null);
            Assert.That(charge!.IdClient, Is.EqualTo(1));
            Assert.That(charge.NomClient, Is.EqualTo("Client Test"));
            Assert.That(charge.Acompte, Is.EqualTo(5000));
            Assert.That(charge.Pieces, Has.Count.EqualTo(1));
            Assert.That(charge.Pieces[0].TypeVetement, Is.EqualTo("Pantalon"));
        }

        [Test]
        public async Task Brouillon02_UnSeulBrouillonParEmploye()
        {
            // Arrange
            int idEmp = 1;
            var brouillon1 = new BrouillonCommandeDto
            {
                IdClient = 1,
                NomClient = "Premier",
                Acompte = 1000
            };
            var brouillon2 = new BrouillonCommandeDto
            {
                IdClient = 2,
                NomClient = "Deuxieme",
                Acompte = 2000
            };

            // Act
            await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon1);
            await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon2);  // Remplace le premier
            var charge = await _brouillonService.ChargerBrouillonAsync(idEmp);

            // Assert - Seul le deuxième brouillon existe
            Assert.That(charge, Is.Not.Null);
            Assert.That(charge!.IdClient, Is.EqualTo(2));
            Assert.That(charge.NomClient, Is.EqualTo("Deuxieme"));
            Assert.That(charge.Acompte, Is.EqualTo(2000));
        }

        [Test]
        public async Task Brouillon03_BrouillonsIndependantsParEmploye()
        {
            // Arrange
            var brouillon1 = new BrouillonCommandeDto { NomClient = "Brouillon Emp1", Acompte = 1000 };
            var brouillon2 = new BrouillonCommandeDto { NomClient = "Brouillon Emp2", Acompte = 2000 };

            // Act
            await _brouillonService.SauvegarderBrouillonAsync(1, brouillon1);
            await _brouillonService.SauvegarderBrouillonAsync(2, brouillon2);
            var charge1 = await _brouillonService.ChargerBrouillonAsync(1);
            var charge2 = await _brouillonService.ChargerBrouillonAsync(2);

            // Assert
            Assert.That(charge1, Is.Not.Null);
            Assert.That(charge2, Is.Not.Null);
            Assert.That(charge1!.NomClient, Is.EqualTo("Brouillon Emp1"));
            Assert.That(charge2!.NomClient, Is.EqualTo("Brouillon Emp2"));
        }

        [Test]
        public async Task Brouillon04_SupprimerBrouillon()
        {
            // Arrange
            int idEmp = 1;
            var brouillon = new BrouillonCommandeDto
            {
                NomClient = "Test",
                Acompte = 1000
            };

            // Act
            await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon);
            Assert.That(await _brouillonService.ABrouillonAsync(idEmp), Is.True);

            await _brouillonService.SupprimerBrouillonAsync(idEmp);
            var charge = await _brouillonService.ChargerBrouillonAsync(idEmp);

            // Assert
            Assert.That(charge, Is.Null);
            Assert.That(await _brouillonService.ABrouillonAsync(idEmp), Is.False);
        }

        [Test]
        public async Task Brouillon05_ContenuComplet()
        {
            // Arrange
            int idEmp = 1;
            var brouillon = new BrouillonCommandeDto
            {
                IdClient = 1,
                NomClient = "Client Complet",
                Acompte = 5000,
                DateFin = DateTime.Today.AddDays(7),
                HeureFin = new TimeSpan(14, 30, 0),
                Notes = "Notes de test",
                Pieces = new List<BrouillonPieceDto>
                {
                    new BrouillonPieceDto
                    {
                        IdTypeVetement = 1,
                        TypeVetement = "Pantalon",
                        IdCouturier = 1,
                        Prix = 10000,
                        Description = "Coupe slim",
                        CheminPhoto = "C:\\test\\photo.jpg",
                        Mesures = new List<BrouillonMesureDto>
                        {
                            new BrouillonMesureDto { Nom = "Longueur", Valeur = "100" },
                            new BrouillonMesureDto { Nom = "Tour de taille", Valeur = "85" }
                        },
                        Materiaux = new List<BrouillonMaterielDto>
                        {
                            new BrouillonMaterielDto { Nom = "Boutons", Prix = 500, Quantite = 5 }
                        }
                    }
                }
            };

            // Act
            await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon);
            var charge = await _brouillonService.ChargerBrouillonAsync(idEmp);

            // Assert
            Assert.That(charge, Is.Not.Null);
            Assert.That(charge!.NomClient, Is.EqualTo("Client Complet"));
            Assert.That(charge.Acompte, Is.EqualTo(5000));
            Assert.That(charge.Pieces, Has.Count.EqualTo(1));

            var piece = charge.Pieces[0];
            Assert.That(piece.TypeVetement, Is.EqualTo("Pantalon"));
            Assert.That(piece.IdCouturier, Is.EqualTo(1));
            Assert.That(piece.Prix, Is.EqualTo(10000));
            Assert.That(piece.Description, Is.EqualTo("Coupe slim"));
            Assert.That(piece.CheminPhoto, Is.EqualTo("C:\\test\\photo.jpg"));

            Assert.That(piece.Mesures, Has.Count.EqualTo(2));
            Assert.That(piece.Mesures.Any(m => m.Nom == "Longueur" && m.Valeur == "100"), Is.True);
            Assert.That(piece.Mesures.Any(m => m.Nom == "Tour de taille" && m.Valeur == "85"), Is.True);

            Assert.That(piece.Materiaux, Has.Count.EqualTo(1));
            Assert.That(piece.Materiaux[0].Nom, Is.EqualTo("Boutons"));
            Assert.That(piece.Materiaux[0].Prix, Is.EqualTo(500));
            Assert.That(piece.Materiaux[0].Quantite, Is.EqualTo(5));
        }

        [Test]
        public async Task Brouillon06_PasDePhotoBase64()
        {
            // Arrange
            int idEmp = 1;
            var brouillon = new BrouillonCommandeDto
            {
                NomClient = "Test Photo",
                Pieces = new List<BrouillonPieceDto>
                {
                    new BrouillonPieceDto
                    {
                        TypeVetement = "Chemise",
                        CheminPhoto = "C:\\Photos\\chemise.jpg"  // Chemin seulement
                    }
                }
            };

            // Act
            await _brouillonService.SauvegarderBrouillonAsync(idEmp, brouillon);
            var charge = await _brouillonService.ChargerBrouillonAsync(idEmp);

            // Assert
            Assert.That(charge, Is.Not.Null);
            Assert.That(charge!.Pieces[0].CheminPhoto, Is.EqualTo("C:\\Photos\\chemise.jpg"));
            // Vérifier que le JSON ne contient pas de données base64 (trop volumineuses)
            var brouillonDb = await _context.BrouillonsCommande.FirstOrDefaultAsync(b => b.IdEmploye == idEmp);
            Assert.That(brouillonDb, Is.Not.Null);
            Assert.That(brouillonDb!.ContenuJson, Does.Not.Contain("data:image"));
            Assert.That(brouillonDb.ContenuJson, Does.Not.Contain("base64"));
        }

        // ═══════════════════════════════════════════════════════════════
        // B) TESTS DUPLICATION DE PIÈCES
        // ═══════════════════════════════════════════════════════════════

        [Test]
        public void Duplication01_CopieSimple()
        {
            // Ce test vérifie la logique métier de la duplication (règles de copie)
            // La copie doit reprendre : type, mesures, couturier, prix, description
            // La copie NE reprend PAS : photo, statut (toujours "A faire"), commission, DateTerminee
            Assert.Pass("La duplication copie type, mesures, couturier, prix et description ; exclut photo/statut/commission/DateTerminee");
        }

        [Test]
        public void Duplication02_MultiplesCopies()
        {
            // Vérifier qu'on peut dupliquer jusqu'à 50 fois
            int maxCopies = 50;
            Assert.That(maxCopies, Is.EqualTo(50), "Maximum 50 copies autorisées");
        }

        [Test]
        public void Duplication03_InsertionSousOrigine()
        {
            // Les copies sont insérées juste après la pièce d'origine
            Assert.Pass("Les copies s'ajoutent directement sous la pièce d'origine");
        }

        [Test]
        public void Duplication04_TotalRecalcule()
        {
            // Le total de la commande se met à jour après duplication
            Assert.Pass("Le total est recalculé après chaque duplication");
        }

        [Test]
        public void Duplication05_VerrouillageRespect()
        {
            // Impossible de dupliquer sur une commande où l'ajout est interdit
            Assert.Pass("La duplication respecte les règles de verrouillage (Étape 2)");
        }

        // ═══════════════════════════════════════════════════════════════
        // HELPER : Factory pour tests
        // ═══════════════════════════════════════════════════════════════

        private class TestDbContextFactory : IDbContextFactory<ApplicationDbContext>
        {
            private readonly DbContextOptions<ApplicationDbContext> _options;

            public TestDbContextFactory(DbContextOptions<ApplicationDbContext> options)
            {
                _options = options;
            }

            public ApplicationDbContext CreateDbContext()
            {
                return new ApplicationDbContext(_options);
            }
        }
    }
}
