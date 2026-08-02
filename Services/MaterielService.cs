using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class MaterielService : IMaterielService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public MaterielService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<MaterielSupplement> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.MaterielsSupplements
                .Include(m => m.PieceCommande)
                .Include(m => m.Commande)
                    .ThenInclude(c => c.Client)
                .OrderByDescending(m => m.IdMateriel)
                .ToList();
        }

        public List<MaterielSupplement> ObtenirParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.MaterielsSupplements
                .Include(m => m.PieceCommande)
                .Where(m => m.IdCommande == idCommande)
                .OrderByDescending(m => m.IdMateriel)
                .ToList();
        }

        public List<MaterielSupplement> ObtenirParPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.MaterielsSupplements
                .Where(m => m.IdPieceCommande == idPieceCommande)
                .OrderByDescending(m => m.IdMateriel)
                .ToList();
        }

        public void Ajouter(MaterielSupplement materiel)
        {
            using var context = _contextFactory.CreateDbContext();
            context.MaterielsSupplements.Add(materiel);
            context.SaveChanges();
        }

        public void Modifier(MaterielSupplement materiel)
        {
            using var context = _contextFactory.CreateDbContext();
            var existant = context.MaterielsSupplements
                .FirstOrDefault(m => m.IdMateriel == materiel.IdMateriel);

            if (existant == null) return;

            existant.Designation = materiel.Designation;
            existant.Quantite = materiel.Quantite;
            existant.PrixUnitaire = materiel.PrixUnitaire;
            if (materiel.IdPieceCommande.HasValue)
                existant.IdPieceCommande = materiel.IdPieceCommande;

            context.SaveChanges();
        }

        public void Supprimer(int idMateriel)
        {
            using var context = _contextFactory.CreateDbContext();
            var materiel = context.MaterielsSupplements
                .Include(m => m.Commande)
                    .ThenInclude(c => c!.Paiements)
                .FirstOrDefault(m => m.IdMateriel == idMateriel);

            if (materiel == null) return;

            // CORRECTIF (audit) : aucune garde ne protégeait une ligne de
            // matériel déjà facturée au client. Le montant total encaissable
            // d'une pièce (Point 2) inclut couture + matériel — supprimer une
            // ligne de matériel après encaissement changerait silencieusement
            // le total d'une commande déjà payée, exactement le genre de
            // dérive que CommandeService.Modifier interdit déjà côté couture.
            bool aPaiements = materiel.Commande?.Paiements.Any(p => !p.EstAnnule) ?? false;
            if (aPaiements)
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette ligne de matériel : des paiements ont déjà " +
                    "été encaissés sur cette commande. Le total facturé au client ne doit pas " +
                    "changer après encaissement.");
            }

            context.MaterielsSupplements.Remove(materiel);
            context.SaveChanges();
        }

        public decimal TotalParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.MaterielsSupplements
                .Where(m => m.IdCommande == idCommande)
                .AsEnumerable()
                .Sum(m => m.Montant);
        }

        public decimal TotalParPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.MaterielsSupplements
                .Where(m => m.IdPieceCommande == idPieceCommande)
                .AsEnumerable()
                .Sum(m => m.Montant);
        }
    }
}