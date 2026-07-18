using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class PaiementService : IPaiementService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        // Verrou statique pour eviter les numeros de recu en doublon
        // en cas d'enregistrements simultanees
        private static readonly object _verrou = new object();

        public PaiementService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        // ----------------------------------------------------------------
        // Lecture
        // ----------------------------------------------------------------

        public List<Paiement> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Paiements
                .Include(p => p.Commande)
                .ThenInclude(c => c!.Client)
                .OrderByDescending(p => p.DatePaiement)
                .ToList();
        }

        public List<Paiement> ObtenirParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Paiements
                .Where(p => p.IdCommande == idCommande)
                .OrderBy(p => p.DatePaiement)
                .ToList();
        }

        // ----------------------------------------------------------------
        // Calculs financiers
        // ----------------------------------------------------------------

        // Total de TOUS les paiements (y compris annules) — pour audit
        public double TotalPayeParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Paiements
                .Where(p => p.IdCommande == idCommande)
                .Sum(p => (double?)p.MontantPaye) ?? 0;
        }

        // Total uniquement des paiements VALIDES — pour calculer le vrai reste
        public double TotalValideParCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return TotalValideParCommande(context, idCommande);
        }

        private static double TotalValideParCommande(ApplicationDbContext context, int idCommande)
        {
            return context.Paiements
                .Where(p => p.IdCommande == idCommande && !p.EstAnnule)
                .Sum(p => (double?)p.MontantPaye) ?? 0;
        }

        // ----------------------------------------------------------------
        // Enregistrement d'un paiement
        // ----------------------------------------------------------------

        public void Ajouter(Paiement paiement, int idOperateur, string nomOperateur)
        {
            lock (_verrou)
            {
                using var context = _contextFactory.CreateDbContext();

                // Recharge le solde en temps reel pour eviter les races conditions
                double totalValide = TotalValideParCommande(context, paiement.IdCommande);

                var commande = context.Commandes.Find(paiement.IdCommande)
                    ?? throw new InvalidOperationException("Commande introuvable.");

                double resteReel = commande.MontantTotal - totalValide;

                if (paiement.MontantPaye <= 0)
                    throw new InvalidOperationException("Le montant doit etre positif.");

                if (paiement.MontantPaye > resteReel + 0.01) // tolerance centimes
                    throw new InvalidOperationException(
                        $"Montant ({paiement.MontantPaye:N0}) depasse le reste reel ({resteReel:N0} FCFA).");

                // Snapshots financiers au moment de l'enregistrement
                paiement.MontantTotalCommande = commande.MontantTotal;
                paiement.ResteAvantPaiement = resteReel;

                // Tracabilite operateur
                paiement.IdOperateur = idOperateur;
                paiement.NomOperateur = nomOperateur;

                // Horodatage precis
                paiement.DatePaiement = DateTime.Now;

                // Numero de recu unique anti-doublon
                paiement.RecuNumero = GenererNumeroRecu(context);
                paiement.EstAnnule = false;

                context.Paiements.Add(paiement);
                context.SaveChanges();
            }
        }

        // ----------------------------------------------------------------
        // Annulation d'un paiement (jamais de suppression)
        // ----------------------------------------------------------------

        public void Annuler(int idPaiement, string motif, string nomAnnulateur)
        {
            using var context = _contextFactory.CreateDbContext();

            var paiement = context.Paiements.Find(idPaiement)
                ?? throw new InvalidOperationException("Paiement introuvable.");

            if (paiement.EstAnnule)
                throw new InvalidOperationException("Ce paiement est deja annule.");

            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            paiement.EstAnnule = true;
            paiement.MotifsAnnulation = motif.Trim();
            paiement.DateAnnulation = DateTime.Now;
            paiement.NomAnnulateur = nomAnnulateur;

            context.SaveChanges();
        }

        // ----------------------------------------------------------------
        // Generation du numero de recu — protegee contre les doublons
        // ----------------------------------------------------------------

        public string GenererNumeroRecu()
        {
            using var context = _contextFactory.CreateDbContext();
            return GenererNumeroRecu(context);
        }

        private static string GenererNumeroRecu(ApplicationDbContext context)
        {
            // Format : REC-AAAAMMJJ-XXXX (sequence du jour, repart a 0001 chaque jour)
            string dateStr = DateTime.Now.ToString("yyyyMMdd");

            int nombreDuJour = context.Paiements
                .Count(p => p.DatePaiement.Date == DateTime.Today);

            string numero = $"REC-{dateStr}-{(nombreDuJour + 1):D4}";

            // Securite anti-doublon : si le numero existe deja, incremente
            int tentative = 1;
            while (context.Paiements.Any(p => p.RecuNumero == numero))
            {
                tentative++;
                numero = $"REC-{dateStr}-{(nombreDuJour + tentative):D4}";
            }

            return numero;
        }
    }
}
