using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class CommandeService : ICommandeService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public CommandeService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<Commande> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Couturier)
                .Include(c => c.Paiements)
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        public Commande? ObtenirParId(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            // CORRECTIF : Include(Paiements) est indispensable. Commande.ResteAPayer
            // et Commande.MontantEncaisse sont des propriétés calculées qui lisent
            // la collection Paiements en mémoire. Sans cet Include, EF Core ne lève
            // aucune erreur : la collection reste simplement vide, et ces deux
            // propriétés renvoient silencieusement des valeurs fausses (reste à
            // payer = montant total, comme si rien n'avait été encaissé).
            return context.Commandes
                .Include(c => c.Mesures)
                .Include(c => c.Paiements)
                .Include(c => c.Client)
                .Include(c => c.Couturier)
                .FirstOrDefault(c => c.IdCommande == id);
        }

        public void Ajouter(Commande commande, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            commande.DateDebut = DateTime.Now;
            context.Commandes.Add(commande);
            context.SaveChanges();

            foreach (var mesure in mesures)
            {
                mesure.IdCommande = commande.IdCommande;
                context.Mesures.Add(mesure);
            }
            context.SaveChanges();
        }

        public void Modifier(Commande commande, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            var existant = context.Commandes
                .Include(c => c.Mesures)
                .Include(c => c.Paiements)
                .FirstOrDefault(c => c.IdCommande == commande.IdCommande);

            if (existant != null)
            {
                // CORRECTIF (incohérence métier) : une commande déjà incluse
                // dans une commission calculée et enregistrée (IdCommission
                // renseigné) voit son MontantTotal figé dans l'historique de
                // cette commission (Commission.BaseMontant). Avant ce
                // correctif, rien n'empêchait de rouvrir cette commande et de
                // changer son montant total : la commission déjà versée au
                // couturier devenait alors silencieusement incohérente avec
                // les commandes qu'elle est censée représenter, sans qu'aucune
                // erreur ne soit levée nulle part.
                if (existant.IdCommission.HasValue && existant.MontantTotal != commande.MontantTotal)
                {
                    throw new InvalidOperationException(
                        "Impossible de modifier le montant de cette commande : elle est rattachée à " +
                        "une commission déjà calculée et enregistrée. Annulez d'abord cette commission " +
                        "(avec motif) si le montant doit vraiment être corrigé.");
                }

                // CORRECTIF (incohérence financière) : sans ce garde-fou, on
                // pouvait enregistrer un MontantTotal inférieur à ce qui a
                // déjà été effectivement encaissé (paiements non annulés),
                // ce qui rend Commande.ResteAPayer négatif — une commande
                // "trop payée" qui n'a pourtant aucun sens métier et fausse
                // tous les totaux/rapports qui s'appuient sur ResteAPayer.
                decimal dejaEncaisse = existant.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                if (commande.MontantTotal < dejaEncaisse)
                {
                    throw new InvalidOperationException(
                        $"Le montant total ({commande.MontantTotal:N0} FCFA) ne peut pas être inférieur " +
                        $"au montant déjà encaissé sur cette commande ({dejaEncaisse:N0} FCFA).");
                }

                existant.IdClient            = commande.IdClient;
                existant.IdCouturier         = commande.IdCouturier;
                existant.TypeVetement        = commande.TypeVetement;
                existant.DescriptionPrecision = commande.DescriptionPrecision;
                existant.DateFin             = commande.DateFin;
                existant.HeureDebut          = commande.HeureDebut;
                existant.HeureFin            = commande.HeureFin;
                existant.Statut              = commande.Statut;
                existant.MontantTotal        = commande.MontantTotal;
                existant.CheminPhoto         = commande.CheminPhoto;

                context.Mesures.RemoveRange(existant.Mesures);
                foreach (var mesure in mesures)
                {
                    mesure.IdCommande = commande.IdCommande;
                    context.Mesures.Add(mesure);
                }
                context.SaveChanges();
            }
        }

        public void Supprimer(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
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

            // Une commande deja incluse dans une commission enregistree ne doit pas
            // etre supprimee non plus, sinon le calcul de commission deviendrait incoherent.
            if (commande.IdCommission.HasValue)
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : elle est rattachée à une commission déjà enregistrée.");
            }

            context.Commandes.Remove(commande);
            context.SaveChanges();
        }

        public List<Commande> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
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
            using var context = _contextFactory.CreateDbContext();
            return context.Mesures
                .Where(m => m.IdCommande == idCommande)
                .ToList();
        }
    }
}
