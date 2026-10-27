using GestionCoutureApp.Data;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    /// <summary>Charge et calcule les commandes qui peuvent encore recevoir un paiement.</summary>
    public sealed class CommandesEncaissablesService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public CommandesEncaissablesService(IDbContextFactory<ApplicationDbContext> contextFactory)
            => _contextFactory = contextFactory;

        public List<CommandeEncaissable> ObtenirCommandesEncaissables()
        {
            using var context = _contextFactory.CreateDbContext();

            var commandes = context.Commandes
                .AsNoTracking()
                .Where(c => !c.EstSupprimee && !c.EstAnnulee)
                .Select(c => new
                {
                    c.IdCommande,
                    ClientPrenom = c.Client != null ? c.Client.Prenom : "",
                    ClientNom = c.Client != null ? c.Client.Nom : "",
                    MontantsCouture = c.Pieces
                        .Select(p => p.MontantCouture)
                        .ToList(),
                    LignesMateriaux = c.MaterielSupplements
                        .Select(m => new { m.Quantite, m.PrixUnitaire })
                        .ToList(),
                    PaiementsValides = c.Paiements
                        .Where(p => !p.EstAnnule)
                        .Select(p => p.MontantPaye)
                        .ToList()
                })
                .ToList();

            return commandes
                .Select(c =>
                {
                    decimal montantCouture = c.MontantsCouture.Sum();
                    decimal montantMateriaux = c.LignesMateriaux.Sum(m => m.Quantite * m.PrixUnitaire);
                    decimal montantTotal = montantCouture + montantMateriaux;
                    decimal resteAPayer = montantTotal - c.PaiementsValides.Sum();
                    return new CommandeEncaissable(
                        c.IdCommande,
                        $"{c.ClientPrenom} {c.ClientNom}".Trim(),
                        montantTotal,
                        resteAPayer);
                })
                .Where(c => c.ResteAPayer > 0.01m)
                .ToList();
        }
    }

    public sealed record CommandeEncaissable(
        int IdCommande,
        string NomClient,
        decimal MontantTotalAvecMateriaux,
        decimal ResteAPayer);
}
