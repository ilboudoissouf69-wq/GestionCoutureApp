using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Tests;

[TestFixture]
public class CommandesEncaissablesSqliteTests
{
    private sealed class ContexteFactory : IDbContextFactory<ApplicationDbContext>
    {
        private readonly DbContextOptions<ApplicationDbContext> _options;
        public ContexteFactory(DbContextOptions<ApplicationDbContext> options) => _options = options;
        public ApplicationDbContext CreateDbContext() => new(_options);
    }

    [Test]
    public void ObtenirCommandesEncaissables_SQLiteCalculeEnMemoireEtFiltreLesCommandes()
    {
        var (context, connection) = TestDbHelper.CreerContexteSqliteEnMemoire();
        using (connection)
        {
            int idClient;
            using (context)
            {
                var client = new Client { Prenom = "Aminata", Nom = "Traoré", Telephone = "70123456" };
                context.Clients.Add(client);
                context.SaveChanges();
                idClient = client.IdClient;

                var ouverte = AjouterCommande(context, idClient);
                context.PiecesCommande.AddRange(
                    new PieceCommande { IdCommande = ouverte.IdCommande, TypeVetement = "Robe", MontantCouture = 12000m },
                    new PieceCommande { IdCommande = ouverte.IdCommande, TypeVetement = "Pantalon", MontantCouture = 8000m });
                context.MaterielsSupplements.Add(new MaterielSupplement
                {
                    IdCommande = ouverte.IdCommande,
                    Designation = "Tissu",
                    Quantite = 2,
                    PrixUnitaire = 3000m,
                    IdOperateur = 1,
                    NomOperateur = "Test"
                });
                context.Paiements.AddRange(
                    CreerPaiement(ouverte.IdCommande, 5000m, annule: false),
                    CreerPaiement(ouverte.IdCommande, 4000m, annule: true));

                var soldee = AjouterCommande(context, idClient);
                context.PiecesCommande.Add(new PieceCommande
                {
                    IdCommande = soldee.IdCommande,
                    TypeVetement = "Chemise",
                    MontantCouture = 7000m
                });
                context.Paiements.Add(CreerPaiement(soldee.IdCommande, 7000m, annule: false));

                var annulee = AjouterCommande(context, idClient, estAnnulee: true);
                context.PiecesCommande.Add(new PieceCommande
                {
                    IdCommande = annulee.IdCommande,
                    TypeVetement = "Jupe",
                    MontantCouture = 9000m
                });

                var supprimee = AjouterCommande(context, idClient, estSupprimee: true);
                context.PiecesCommande.Add(new PieceCommande
                {
                    IdCommande = supprimee.IdCommande,
                    TypeVetement = "Veste",
                    MontantCouture = 11000m
                });
                context.SaveChanges();
            }

            var options = new DbContextOptionsBuilder<ApplicationDbContext>()
                .UseSqlite(connection)
                .Options;
            var service = new CommandesEncaissablesService(new ContexteFactory(options));

            var resultat = service.ObtenirCommandesEncaissables();

            Assert.That(resultat, Has.Count.EqualTo(1));
            Assert.That(resultat[0].NomClient, Is.EqualTo("Aminata Traoré"));
            Assert.That(resultat[0].MontantTotalAvecMateriaux, Is.EqualTo(26000m));
            Assert.That(resultat[0].ResteAPayer, Is.EqualTo(21000m));
        }
    }

    private static Commande AjouterCommande(
        ApplicationDbContext context,
        int idClient,
        bool estSupprimee = false,
        bool estAnnulee = false)
    {
        var commande = new Commande
        {
            IdClient = idClient,
            DateDebut = DateTime.Today,
            DateFin = DateTime.Today.AddDays(7),
            IdOperateurCreation = 1,
            NomOperateurCreation = "Test",
            EstSupprimee = estSupprimee,
            EstAnnulee = estAnnulee
        };
        context.Commandes.Add(commande);
        context.SaveChanges();
        return commande;
    }

    private static Paiement CreerPaiement(int idCommande, decimal montant, bool annule)
        => new()
        {
            IdCommande = idCommande,
            MontantPaye = montant,
            ModePaiement = "Espèces",
            RecuNumero = $"TEST-{Guid.NewGuid():N}",
            IdOperateur = 1,
            NomOperateur = "Test",
            EstAnnule = annule
        };
}
