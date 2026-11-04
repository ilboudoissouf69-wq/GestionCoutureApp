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
            return ConstruireQuery(context).AsEnumerable()
                .Select(c => new CommandeEncaissable(c.IdCommande, c.NomClient,
                    Convert.ToDecimal(c.MontantTotal), Convert.ToDecimal(c.ResteAPayer)))
                .ToList();
        }

        public async Task<PagedResult<CommandeEncaissable>> ObtenirPageAsync(
            int page, int pageSize, string? recherche = null,
            CancellationToken cancellationToken = default)
        {
            await using var context = await _contextFactory.CreateDbContextAsync(cancellationToken);
            page = Math.Max(1, page);
            pageSize = Math.Clamp(pageSize, 1, 50);
            var query = ConstruireQuery(context);
            if (!string.IsNullOrWhiteSpace(recherche))
            {
                var terme = recherche.Trim();
                string cle = GestionCoutureApp.Helpers.TexteHelper.NormaliserPourRecherche(terme);
                bool numero = terme.StartsWith("CMD", StringComparison.OrdinalIgnoreCase)
                    && int.TryParse(new string(terme.Where(char.IsDigit).ToArray()), out var idCommande);
                int idExact = numero ? int.Parse(new string(terme.Where(char.IsDigit).ToArray())) : 0;
                query = query.Where(c =>
                    c.NomRecherche.StartsWith(cle)
                    || c.Telephone.StartsWith(terme)
                    || (numero && c.IdCommande == idExact));
            }

            int total = await query.CountAsync(cancellationToken);
            var items = await query.OrderBy(c => c.IdCommande)
                .Skip((page - 1) * pageSize).Take(pageSize)
                .Select(c => new CommandeEncaissable(c.IdCommande, c.NomClient,
                    Convert.ToDecimal(c.MontantTotal), Convert.ToDecimal(c.ResteAPayer)))
                .ToListAsync(cancellationToken);
            return new PagedResult<CommandeEncaissable>
            { Items = items, TotalCount = total, Page = page, PageSize = pageSize };
        }

        private static IQueryable<CommandeEncaissableProjection> ConstruireQuery(ApplicationDbContext context)
        {
            var pieces = context.PiecesCommande.AsNoTracking()
                .GroupBy(p => p.IdCommande)
                .Select(g => new { IdCommande = g.Key, Total = g.Sum(p => (double)p.MontantCouture) });
            var materiaux = context.MaterielsSupplements.AsNoTracking()
                .GroupBy(m => m.IdCommande)
                .Select(g => new { IdCommande = g.Key, Total = g.Sum(m => m.Quantite * (double)m.PrixUnitaire) });
            var paiements = context.Paiements.AsNoTracking().Where(p => !p.EstAnnule)
                .GroupBy(p => p.IdCommande)
                .Select(g => new { IdCommande = g.Key, Total = g.Sum(p => (double)p.MontantPaye) });

            var query =
                from commande in context.Commandes.AsNoTracking()
                where !commande.EstSupprimee && !commande.EstAnnulee
                join client in context.Clients.AsNoTracking() on commande.IdClient equals client.IdClient
                join pieceTotal in pieces on commande.IdCommande equals pieceTotal.IdCommande into piecesJointes
                from pieceTotal in piecesJointes.DefaultIfEmpty()
                join materielTotal in materiaux on commande.IdCommande equals materielTotal.IdCommande into materiauxJointes
                from materielTotal in materiauxJointes.DefaultIfEmpty()
                join paiementTotal in paiements on commande.IdCommande equals paiementTotal.IdCommande into paiementsJointes
                from paiementTotal in paiementsJointes.DefaultIfEmpty()
                let couture = (double?)pieceTotal.Total ?? 0d
                let materiel = (double?)materielTotal.Total ?? 0d
                let montant = couture + materiel
                let encaisse = (double?)paiementTotal.Total ?? 0d
                where montant - encaisse > 0.01d
                select new CommandeEncaissableProjection
                {
                    IdCommande = commande.IdCommande,
                    NomClient = client.Prenom + " " + client.Nom,
                    NomRecherche = client.NomRecherche,
                    Telephone = client.Telephone ?? "",
                    MontantTotal = montant,
                    ResteAPayer = montant - encaisse
                };
            return query;
        }
    }

    internal sealed class CommandeEncaissableProjection
    {
        public int IdCommande { get; init; }
        public string NomClient { get; init; } = "";
        public string NomRecherche { get; init; } = "";
        public string Telephone { get; init; } = "";
        public double MontantTotal { get; init; }
        public double ResteAPayer { get; init; }
    }

    public sealed record CommandeEncaissable(
        int IdCommande,
        string NomClient,
        decimal MontantTotalAvecMateriaux,
        decimal ResteAPayer);
}
