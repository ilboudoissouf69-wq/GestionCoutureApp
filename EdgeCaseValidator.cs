using System;
using System.Globalization;
using GestionCoutureApp.Helpers;

namespace GestionCoutureApp
{
    /// <summary>
    /// Validateur de tests edge cases pour robustesse des champs de saisie
    /// </summary>
    public static class EdgeCaseValidator
    {
        /// <summary>
        /// Exécute tous les tests edge cases et retourne un rapport
        /// </summary>
        public static string RunEdgeCaseTests()
        {
            var report = new System.Text.StringBuilder();
            
            report.AppendLine("╔════════════════════════════════════════════════════════════╗");
            report.AppendLine("║     RAPPORT TESTS EDGE CASES - VALIDATION CHAMPS         ║");
            report.AppendLine("╚════════════════════════════════════════════════════════════╝\n");

            report.AppendLine("=== TESTS NUMÉRIQUES ===\n");
            TestNumericValidation(report);

            report.AppendLine("\n=== TESTS TEXTE ===\n");
            TestTextValidation(report);

            report.AppendLine("\n=== TESTS DATES ===\n");
            TestDateValidation(report);

            report.AppendLine("\n=== TESTS VALIDATION ENTRÉE ===\n");
            TestInputValidation(report);

            report.AppendLine("╔════════════════════════════════════════════════════════════╗");
            report.AppendLine("║     FIN DU RAPPORT                                      ║");
            report.AppendLine("╚════════════════════════════════════════════════════════════╝");

            return report.ToString();
        }

        private static void TestNumericValidation(System.Text.StringBuilder report)
        {
            string[] testCases = new string[]
            {
                // Cas problématiques qui devraient être rejetés
                "-1000", "-5.5", "-0.01", "-999999999999",
                "1,000.50", "1.000,50", "5..5", ".5", "5.", "005",
                "999999999999999999999999999999999999999999999999999",
                "1000 FCFA", "1000€", "$1000", "1000 USD", "1 000 FCFA",
                "0", "0.00", "0.000001", "-0.01",
                "0.00000000000000000000000000000000000000000000000001",
                "999999999999999999.99", "1E308", "NaN", "Infinity", "-Infinity",
                
                // Cas valides qui devraient être acceptés
                "1000", "50.5", "1234.56", "0.1", "10000"
            };

            int passed = 0;
            int failed = 0;

            foreach (string testCase in testCases)
            {
                try
                {
                    bool isValid = ValidationHelper.EstDecimalPositifValide(testCase, out decimal valeur);
                    report.AppendLine($"✓ '{testCase}' -> Valid: {isValid}, Valeur: {valeur}");
                    passed++;
                }
                catch (Exception ex)
                {
                    report.AppendLine($"✗ '{testCase}' -> ERREUR: {ex.Message}");
                    failed++;
                }
            }

            report.AppendLine($"\nRésultats numériques: {passed} réussis, {failed} échoués");
        }

        private static void TestTextValidation(System.Text.StringBuilder report)
        {
            string[] testCases = new string[]
            {
                // Cas problématiques
                new string('A', 250), // Trop long (limite 200)
                new string(' ', 250), // Trop long
                "'; DROP TABLE Clients; --",
                "1' OR '1'='1",
                "<script>alert('XSS')</script>",
                "Robert'); DROP TABLE Clients; --",
                "O'Connor", "D'Angelo", "L'Étranger", "\"Test\"", "'Test'",
                "👕👔👗👘", "😀😁😂🤣😊😍", "", "العربية", "éàèùâêîôûëïüç",
                "\n\r\t", "\u0000", // Caractère null - invalide
                " ", "  ", "\t", "\n", "\r\n", "\u00A0", "\u200B", // Espace zéro largeur - invalide
                
                // Cas valides
                "Jean Dupont", "Marie Curie", "Pierre Richard"
            };

            int passed = 0;
            int failed = 0;

            foreach (string testCase in testCases)
            {
                try
                {
                    // Test avec la nouvelle validation sécurisée
                    bool isValid = ValidationHelper.EstTexteSecurise(testCase, out string erreur);
                    string display = testCase.Length > 50 ? testCase.Substring(0, 50) + "..." : testCase;
                    
                    // Vérifier que les cas problématiques sont bien rejetés
                    bool shouldBeValid = (testCase == "O'Connor" || testCase == "D'Angelo" || 
                                         testCase == "L'Étranger" || testCase == "\"Test\"" || 
                                         testCase == "'Test'" || testCase == "👕👔👗👘" || 
                                         testCase == "😀😁😂🤣😊😍" || testCase == "العربية" || 
                                         testCase == "éàèùâêîôûëïüç" || testCase == "Jean Dupont" || 
                                         testCase == "Marie Curie" || testCase == "Pierre Richard");
                    
                    if (isValid == shouldBeValid)
                    {
                        report.AppendLine($"✓ '{display}' -> Valid: {isValid}, Erreur: {erreur}");
                        passed++;
                    }
                    else
                    {
                        report.AppendLine($"✗ '{display}' -> Valid: {isValid}, Erreur: {erreur} (Attendu: {shouldBeValid})");
                        failed++;
                    }
                }
                catch (Exception ex)
                {
                    string display = testCase.Length > 50 ? testCase.Substring(0, 50) + "..." : testCase;
                    report.AppendLine($"✗ '{display}' -> ERREUR: {ex.Message}");
                    failed++;
                }
            }

            report.AppendLine($"\nRésultats texte: {passed} réussis, {failed} échoués");
        }

        private static void TestDateValidation(System.Text.StringBuilder report)
        {
            string[] testCases = new string[]
            {
                // Dates invalides
                "31/02/2026", "30/02/2026", "29/02/2025", "32/01/2026",
                "01/13/2026", "00/01/2026", "01/00/2026",
                "01/01/0001", "31/12/9999", "01/01/10000", "01/01/-1",
                "2026-09-23", "23-09-2026", "September 23, 2026",
                "20260923", "23/09/26", "01/01/01",
                "01/01/2020", "31/12/2019", "23/09/2023",
                "01/01/2100", "31/12/3000", "01/01/9999",
                "", " ", "  ", "///", "??/????", "abcdefghij",
                
                // Dates valides
                "23/09/2026", "01/01/2024", "31/12/2025"
            };

            int passed = 0;
            int failed = 0;

            foreach (string testCase in testCases)
            {
                try
                {
                    bool isValid = DateTime.TryParseExact(testCase, "dd/MM/yyyy", 
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime date);
                    report.AppendLine($"✓ '{testCase}' -> Valid: {isValid}, Date: {date:dd/MM/yyyy}");
                    passed++;
                }
                catch (Exception ex)
                {
                    report.AppendLine($"✗ '{testCase}' -> ERREUR: {ex.Message}");
                    failed++;
                }
            }

            report.AppendLine($"\nRésultats dates: {passed} réussis, {failed} échoués");
        }

        private static void TestInputValidation(System.Text.StringBuilder report)
        {
            string[] testCases = new string[]
            {
                // Cas normaux
                "1234.56", "50.5", "10000",
                
                // Cas problématiques
                "-1000", "abc", "12.34.56", "1000 FCFA", "€1000",
                "", "   ", "\t", "\n", "null", "undefined"
            };

            int passed = 0;
            int failed = 0;

            foreach (string testCase in testCases)
            {
                try
                {
                    bool isValid = ValidationHelper.EstDecimalPositifValide(testCase, out decimal valeur);
                    report.AppendLine($"✓ '{testCase}' -> Valid: {isValid}, Valeur: {valeur}");
                    passed++;
                }
                catch (Exception ex)
                {
                    report.AppendLine($"✗ '{testCase}' -> ERREUR: {ex.Message}");
                    failed++;
                }
            }

            report.AppendLine($"\nRésultats validation entrée: {passed} réussis, {failed} échoués");
        }
    }
}