using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class CommandeService : ICommandeService
    {
        private readonly ApplicationDbContext _context;

        public CommandeService(ApplicationDbContext context)
        {
            _context = context;
        }

        public List<Commande> ObtenirTous()
        {
            return _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Couturier)
                .Include(c => c.Paiements)
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        public Commande? ObtenirParId(int id)
        {
            return _context.Commandes
                .Include(c => c.Mesures)
                .FirstOrDefault(c => c.IdCommande == id);
        }

        public void Ajouter(Commande commande, List<Mesure> mesures)
        {
            commande.DateDebut = DateTime.Now;
            _context.Commandes.Add(commande);
            _context.SaveChanges();

            foreach (var mesure in mesures)
            {
                mesure.IdCommande = commande.IdCommande;
                _context.Mesures.Add(mesure);
            }
            _context.SaveChanges();
        }

        public void Modifier(Commande commande, List<Mesure> mesures)
        {
            var existant = _context.Commandes
                .Include(c => c.Mesures)
                .FirstOrDefault(c => c.IdCommande == commande.IdCommande);

            if (existant != null)
            {
                existant.IdClient = commande.IdClient;
                existant.IdCouturier = commande.IdCouturier;
                existant.TypeVetement = commande.TypeVetement;
                existant.DescriptionPrecision = commande.DescriptionPrecision;
                existant.DateFin = commande.DateFin;
                existant.Statut = commande.Statut;
                existant.MontantTotal = commande.MontantTotal;

                _context.Mesures.RemoveRange(existant.Mesures);
                foreach (var mesure in mesures)
                {
                    mesure.IdCommande = commande.IdCommande;
                    _context.Mesures.Add(mesure);
                }
                _context.SaveChanges();
            }
        }

        public void Supprimer(int id)
        {
            var commande = _context.Commandes
                .Include(c => c.Paiements)
                .FirstOrDefault(c => c.IdCommande == id);

            if (commande == null) return;

            // Une commande ayant déjà reçu un paiement ne doit jamais être supprimée :
            // cela effacerait silencieusement l'historique financier (voir PaiementService,
            // conçu pour ne jamais supprimer un paiement, seulement l'annuler avec motif).
            if (commande.Paiements.Any())
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : des paiements y sont rattachés. " +
                    "Annulez d'abord les paiements concernés (avec motif), ou changez le statut " +
                    "de la commande à \"Annulée\" plutôt que de la supprimer.");
            }

            _context.Commandes.Remove(commande);
            _context.SaveChanges();
        }

        public List<Commande> Rechercher(string motCle)
        {
            return _context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Couturier)
                .Include(c => c.Paiements)
                .Where(c => c.Client != null && (
                         c.Client.Nom.Contains(motCle)
                         || c.Client.Prenom.Contains(motCle)
                         || c.TypeVetement.Contains(motCle)
                         || c.Statut.Contains(motCle)))
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        public List<Mesure> ObtenirMesures(int idCommande)
        {
            return _context.Mesures
                .Where(m => m.IdCommande == idCommande)
                .ToList();
        }
    }
}
