using System.Globalization;
using GestionCoutureApp.Models;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Tests;

[TestFixture]
public class TicketsCommandeLogiqueTests
{
    [TestCase(0, ModeImpression.EtiquetteEtBonClient)]
    [TestCase(125, ModeImpression.EtiquetteSeule)]
    [TestCase(-10, ModeImpression.EtiquetteEtBonClient)]
    public void ModeParDefaut_TraiteMontantEncaisse(decimal encaisse, ModeImpression attendu)
        => Assert.That(TicketsCommandeLogique.ModeParDefaut(encaisse), Is.EqualTo(attendu));

    [Test]
    public void Decoupage_UnePiece_GardeUneEtiquetteSansLibellePiece()
    {
        var resultat = TicketsCommandeLogique.DecouperEtiquettes(1, true);
        Assert.That(resultat, Is.EqualTo(new string?[] { null }));
    }

    [Test]
    public void Decoupage_TroisPieces_CreeUnTicketParPieceAvecNumerotation()
    {
        var resultat = TicketsCommandeLogique.DecouperEtiquettes(3, true);
        Assert.That(resultat, Is.EqualTo(new string?[] { "PIÈCE 1/3", "PIÈCE 2/3", "PIÈCE 3/3" }));
    }

    [Test]
    public void Decoupage_CaseDecochee_RegroupeLesPieces()
    {
        var resultat = TicketsCommandeLogique.DecouperEtiquettes(3, false);
        Assert.That(resultat, Is.EqualTo(new string?[] { null }));
    }

    [TestCase(15000, "SOLDE À ENCAISSER : 15 000 FCFA", false)]
    [TestCase(0, "SOLDÉ ✓", true)]
    [TestCase(-1, "SOLDÉ ✓", true)]
    public void LibelleSolde_FormateDeManiereIndependanteDeLaCulture(decimal reste, string texte, bool estSolde)
    {
        var culture = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("en-US");
            var resultat = TicketsCommandeLogique.LibelleSolde(reste);
            Assert.That(resultat.Texte, Is.EqualTo(texte));
            Assert.That(resultat.EstSolde, Is.EqualTo(estSolde));
        }
        finally { CultureInfo.CurrentCulture = culture; }
    }

    [Test]
    public void ResumePieces_AfficheLeDetailQuandPlusieursTypes()
    {
        var pieces = new[]
        {
            new PieceCommande { TypeVetement = "Pantalon" },
            new PieceCommande { TypeVetement = "Pantalon" },
            new PieceCommande { TypeVetement = "Robe" }
        };
        Assert.That(TicketsCommandeLogique.ResumePieces(pieces), Is.EqualTo("3 pièces : 2 Pantalon, 1 Robe"));
        Assert.That(TicketsCommandeLogique.ResumePieces(new[] { pieces[0] }), Is.EqualTo("1 pièce"));
    }

    [TestCase("Couturier", false)]
    [TestCase("Boss", true)]
    [TestCase("Secretaire", true)]
    public void VisibiliteSelonRole_ControleLesElementsClient(string role, bool visibles)
    {
        var resultat = TicketsCommandeLogique.VisibiliteSelonRole(role);
        Assert.That(resultat.OngletBonClientVisible, Is.EqualTo(visibles));
        Assert.That(resultat.ModeBonClientSeulVisible, Is.EqualTo(visibles));
        Assert.That(resultat.ModeEtiquetteEtBonVisible, Is.EqualTo(visibles));
    }
}
