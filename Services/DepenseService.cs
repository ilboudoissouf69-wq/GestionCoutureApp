using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class DepenseService : IDepenseService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public DepenseService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<Depense> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Depenses
                .OrderByDescending(d => d.DateDepense)
                .ToList();
        }

        public void Ajouter(Depense depense)
        {
            using var context = _contextFactory.CreateDbContext();
            context.Depenses.Add(depense);
            context.SaveChanges();
        }

        public void Supprimer(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            var depense = context.Depenses.Find(id);
            if (depense != null)
            {
                context.Depenses.Remove(depense);
                context.SaveChanges();
            }
        }

        public decimal TotalParPeriode(DateTime debut, DateTime fin)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Depenses
                .Where(d => d.DateDepense.Date >= debut.Date
                         && d.DateDepense.Date <= fin.Date)
                .AsEnumerable()
                .Sum(d => d.Montant);
        }
    }
}