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

        // CORRECTIF (audit — Décision 3.1) : remplace l'ancienne suppression
        // physique. Une dépense enregistrée par erreur n'est plus supprimée,
        // elle est annulée avec motif obligatoire et trace de qui/quand —
        // même mécanisme que PaiementService.Annuler et CommissionService.Annuler.
        // De l'argent réel ne doit jamais disparaître silencieusement de
        // l'historique, quel que soit le module de l'application.
        public void Annuler(int idDepense, string motif, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            var depense = context.Depenses.Find(idDepense)
                ?? throw new InvalidOperationException("Dépense introuvable.");

            if (depense.EstAnnulee)
                throw new InvalidOperationException("Cette dépense est déjà annulée.");

            depense.EstAnnulee = true;
            depense.MotifAnnulation = motif.Trim();
            depense.DateAnnulation = DateTime.Now;
            depense.NomAnnulateur = nomAnnulateur;

            context.SaveChanges();
        }

        // CORRECTIF (audit) : total du tableau de bord — exclut désormais les
        // dépenses annulées, exactement comme ResteAPayer/MontantEncaisse
        // excluent déjà les paiements annulés. Sans ce filtre, une dépense
        // annulée continuerait à réduire à tort le bénéfice net affiché au Boss.
        public decimal TotalParPeriode(DateTime debut, DateTime fin)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Depenses
                .Where(d => !d.EstAnnulee
                         && d.DateDepense.Date >= debut.Date
                         && d.DateDepense.Date <= fin.Date)
                .AsEnumerable()
                .Sum(d => d.Montant);
        }
    }
}
