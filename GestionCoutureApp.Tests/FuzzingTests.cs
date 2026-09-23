using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using System.ComponentModel.DataAnnotations;

namespace GestionCoutureApp.Tests
{
    [TestFixture]
    public class FuzzingTests
    {
        private Random _random = new Random();

        [Test]
        public void Fuzzing_Client_100Iterations_NoUnhandledException()
        {
            // Arrange
            var exceptions = new List<Exception>();

            // Act - 100 itérations de fuzzing sur le modèle Client
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var client = GenerateRandomClient();
                    ValidateModel(client);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    Console.WriteLine($"Iteration {i}: Exception - {ex.Message}");
                }
            }

            // Assert - Vérifier qu'il n'y a pas d'exceptions non gérées
            if (exceptions.Any(ex => ex is not ValidationException))
            {
                Assert.Fail($"Unhandled exceptions detected: {exceptions.Count} errors. First error: {exceptions.First().Message}");
            }

            Console.WriteLine($"Fuzzing terminé: {100 - exceptions.Count}/100 objets valides, {exceptions.Count} erreurs de validation attendues.");
        }

        [Test]
        public void Fuzzing_Commande_100Iterations_NoUnhandledException()
        {
            // Arrange
            var exceptions = new List<Exception>();

            // Act - 100 itérations de fuzzing sur le modèle Commande
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var commande = GenerateRandomCommande();
                    ValidateModel(commande);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    Console.WriteLine($"Iteration {i}: Exception - {ex.Message}");
                }
            }

            // Assert - Vérifier qu'il n'y a pas d'exceptions non gérées
            if (exceptions.Any(ex => ex is not ValidationException))
            {
                Assert.Fail($"Unhandled exceptions detected: {exceptions.Count} errors. First error: {exceptions.First().Message}");
            }

            Console.WriteLine($"Fuzzing terminé: {100 - exceptions.Count}/100 objets valides, {exceptions.Count} erreurs de validation attendues.");
        }

        [Test]
        public void Fuzzing_PieceCommande_100Iterations_NoUnhandledException()
        {
            // Arrange
            var exceptions = new List<Exception>();

            // Act - 100 itérations de fuzzing sur le modèle PieceCommande
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var piece = GenerateRandomPieceCommande();
                    ValidateModel(piece);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    Console.WriteLine($"Iteration {i}: Exception - {ex.Message}");
                }
            }

            // Assert - Vérifier qu'il n'y a pas d'exceptions non gérées
            if (exceptions.Any(ex => ex is not ValidationException))
            {
                Assert.Fail($"Unhandled exceptions detected: {exceptions.Count} errors. First error: {exceptions.First().Message}");
            }

            Console.WriteLine($"Fuzzing terminé: {100 - exceptions.Count}/100 objets valides, {exceptions.Count} erreurs de validation attendues.");
        }

        [Test]
        public void Fuzzing_Mesure_100Iterations_NoUnhandledException()
        {
            // Arrange
            var exceptions = new List<Exception>();

            // Act - 100 itérations de fuzzing sur le modèle Mesure
            for (int i = 0; i < 100; i++)
            {
                try
                {
                    var mesure = GenerateRandomMesure();
                    ValidateModel(mesure);
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                    Console.WriteLine($"Iteration {i}: Exception - {ex.Message}");
                }
            }

            // Assert - Vérifier qu'il n'y a pas d'exceptions non gérées
            if (exceptions.Any(ex => ex is not ValidationException))
            {
                Assert.Fail($"Unhandled exceptions detected: {exceptions.Count} errors. First error: {exceptions.First().Message}");
            }

            Console.WriteLine($"Fuzzing terminé: {100 - exceptions.Count}/100 objets valides, {exceptions.Count} erreurs de validation attendues.");
        }

        private Client GenerateRandomClient()
        {
            return new Client
            {
                IdClient = _random.Next(int.MinValue, int.MaxValue),
                Nom = GetRandomString(),
                Prenom = GetRandomString(),
                Telephone = GetRandomString(0, 500)
            };
        }

        private Commande GenerateRandomCommande()
        {
            return new Commande
            {
                IdCommande = _random.Next(int.MinValue, int.MaxValue),
                IdClient = _random.Next(int.MinValue, int.MaxValue),
                TypeVetement = GetRandomString(),
                IdCouturier = _random.Next(-1000, 1000),
                MontantTotal = GetRandomDecimal(),
                IdCommission = _random.Next(-1000, 1000),
                DescriptionPrecision = GetRandomString(0, 10000),
                CheminPhoto = GetRandomString(),
                DateDebut = GetRandomDateTime(),
                DateFin = GetRandomDateTime(),
                Statut = GetRandomString(),
                HeureDebut = GetRandomTimeSpan(),
                HeureFin = GetRandomNullableTimeSpan(),
                Pieces = new List<PieceCommande>(),
                Mesures = new List<Mesure>(),
                Paiements = new List<Paiement>(),
                MaterielSupplements = new List<MaterielSupplement>()
            };
        }

        private PieceCommande GenerateRandomPieceCommande()
        {
            return new PieceCommande
            {
                IdPieceCommande = _random.Next(int.MinValue, int.MaxValue),
                IdCommande = _random.Next(int.MinValue, int.MaxValue),
                TypeVetement = GetRandomString(),
                DescriptionPrecision = GetRandomString(0, 10000),
                CheminPhoto = GetRandomString(),
                IdCouturier = _random.Next(-1000, 1000),
                MontantCouture = GetRandomDecimal(),
                Statut = GetRandomString(),
                IdCommission = _random.Next(-1000, 1000),
                MotifAjoutApresEncaissement = GetRandomString(),
                RendezVousException = GetRandomNullableDateTime(),
                Mesures = new List<Mesure>(),
                MaterielSupplements = new List<MaterielSupplement>()
            };
        }

        private Mesure GenerateRandomMesure()
        {
            return new Mesure
            {
                IdMesure = _random.Next(int.MinValue, int.MaxValue),
                IdCommande = _random.Next(int.MinValue, int.MaxValue),
                IdPieceCommande = _random.Next(-1000, 1000),
                NomMesure = GetRandomString(),
                Valeur = GetRandomString(0, 1000)
            };
        }

        private string GetRandomString(int minLength = 0, int maxLength = 200)
        {
            if (_random.Next(10) == 0) return null!; // 10% chance of null
            if (_random.Next(20) == 0) return string.Empty; // 5% chance of empty string

            int length = _random.Next(minLength, maxLength + 1);
            var chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789 !@#$%^&*()_+-=[]{}|;:,.<>?/éèêëàâäùûüôöîïç";
            return new string(Enumerable.Repeat(chars, length)
                .Select(s => s[_random.Next(s.Length)]).ToArray());
        }

        private decimal GetRandomDecimal()
        {
            // Cas extrêmes pour les montants
            int scenario = _random.Next(10);
            return scenario switch
            {
                0 => decimal.MinValue,
                1 => decimal.MaxValue,
                2 => -999999999.99m,
                3 => 0m,
                4 => (decimal)_random.NextDouble() * decimal.MaxValue,
                _ => (decimal)(_random.NextDouble() * 1000000 - 500000)
            };
        }

        private DateTime GetRandomDateTime()
        {
            int scenario = _random.Next(10);
            return scenario switch
            {
                0 => DateTime.MinValue,
                1 => DateTime.MaxValue,
                2 => DateTime.Now.AddDays(_random.Next(-36500, 36500)), // +/- 100 ans
                _ => DateTime.Now.AddDays(_random.Next(-365, 365))
            };
        }

        private DateTime? GetRandomNullableDateTime()
        {
            if (_random.Next(3) == 0) return null;
            return GetRandomDateTime();
        }

        private TimeSpan GetRandomTimeSpan()
        {
            int scenario = _random.Next(10);
            return scenario switch
            {
                0 => TimeSpan.MinValue,
                1 => TimeSpan.MaxValue,
                2 => TimeSpan.FromDays(_random.Next(-365, 365)),
                _ => TimeSpan.FromHours(_random.Next(0, 24))
            };
        }

        private TimeSpan? GetRandomNullableTimeSpan()
        {
            if (_random.Next(3) == 0) return null;
            return GetRandomTimeSpan();
        }

        private void ValidateModel(object model)
        {
            var validationContext = new ValidationContext(model, null, null);
            var validationResults = new List<ValidationResult>();
            var isValid = Validator.TryValidateObject(model, validationContext, validationResults, true);

            if (!isValid)
            {
                throw new ValidationException($"Validation failed: {string.Join(", ", validationResults.Select(vr => vr.ErrorMessage))}");
            }

            // Tester également les propriétés calculées pour détecter les exceptions
            try
            {
                if (model is Commande commande)
                {
                    // Accéder aux propriétés calculées pour tester qu'elles ne lèvent pas d'exception
                    _ = commande.MontantTotalCalcule;
                    _ = commande.MontantTotalAvecMateriaux;
                    _ = commande.TotalMateriaux;
                    _ = commande.TypeVetementAffiche;
                    _ = commande.IdCouturierUnique;
                    _ = commande.CouturierUnique;
                    _ = commande.StatutGlobal;
                    _ = commande.StatutGlobalAffiche;
                    _ = commande.ResteAPayer;
                    _ = commande.MontantEncaisse;
                }

                if (model is PieceCommande piece)
                {
                    _ = piece.LabelReutilisation;
                    _ = piece.StatutAffiche;
                }
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Computed property access failed: {ex.Message}", ex);
            }
        }
    }
}
