using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class PaiementService : IPaiementService
    {
        private readonly ApplicationDbContext _context;

        // Verrou statique pour eviter les numeros de recu en doublon
        // en cas d'enregistrements simultanees
        private static readonly object _verrou = new object();

        public PaiementService(ApplicationDbContext context)
        {
            _context = context;
        }

        // ----------------------------------------------------------------
        // Lecture
        // ----------------------------------------------------------------

        public List<Paiement> ObtenirTous()
        {
            return _context.Paiements
                .Include(p => p.Commande)
                .ThenInclude(c => c!.Client)
                .OrderByDescending(p => p.DatePaiement)
                .ToList();
        }

        public List<Paiement> ObtenirParCommande(int idCommande)
        {
            return _context.Paiements
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
            return _context.Paiements
                .Where(p => p.IdCommande == idCommande)
                .Sum(p => (double?)p.MontantPaye) ?? 0;
        }

        // Total uniquement des paiements VALIDES — pour calculer le vrai reste
        public double TotalValideParCommande(int idCommande)
        {
            return _context.Paiements
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
                // Recharge le solde en temps reel pour eviter les races conditions
                double totalValide = TotalValideParCommande(paiement.IdCommande);

                var commande = _context.Commandes.Find(paiement.IdCommande)
                    ?? throw new InvalidOperationException("Commande introuvable.");

                double resteReel = commande.MontantTotal - totalValide;

                if (paiement.MontantPaye <= 0)
                    throw new InvalidOperationException("Le montant doit etre positif.");

                if (paiement.MontantPaye > resteReel + 0.01) // tolerance centimes
                    throw new InvalidOperationException(
                        $"Montant ({paiement.MontantPaye:N0}) depasse le reste reel ({resteReel:N0} FCFA).");

                // Snapshots financiers au moment de l'enregistrement
                paiement.MontantTotalCommande = commande.MontantTotal;
                paiement.ResteAvantPaiement   = resteReel;

                // Tracabilite operateur
                paiement.IdOperateur   = idOperateur;
                paiement.NomOperateur  = nomOperateur;

                // Horodatage precis
                paiement.DatePaiement  = DateTime.Now;

                // Numero de recu unique anti-doublon
                paiement.RecuNumero    = GenererNumeroRecu();
                paiement.EstAnnule     = false;

                _context.Paiements.Add(paiement);
                _context.SaveChanges();
            }
        }

        // ----------------------------------------------------------------
        // Annulation d'un paiement (jamais de suppression)
        // ----------------------------------------------------------------

        public void Annuler(int idPaiement, string motif, string nomAnnulateur)
        {
            var paiement = _context.Paiements.Find(idPaiement)
                ?? throw new InvalidOperationException("Paiement introuvable.");

            if (paiement.EstAnnule)
                throw new InvalidOperationException("Ce paiement est deja annule.");

            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            paiement.EstAnnule        = true;
            paiement.MotifsAnnulation = motif.Trim();
            paiement.DateAnnulation   = DateTime.Now;
            paiement.NomAnnulateur    = nomAnnulateur;

            _context.SaveChanges();
        }

        // ----------------------------------------------------------------
        // Generation du numero de recu — protegee contre les doublons
        // ----------------------------------------------------------------

        public string GenererNumeroRecu()
        {
            // Format : REC-AAAAMMJJ-XXXX (sequence du jour, repart a 0001 chaque jour)
            string dateStr = DateTime.Now.ToString("yyyyMMdd");

            int nombreDuJour = _context.Paiements
                .Count(p => p.DatePaiement.Date == DateTime.Today);

            string numero = $"REC-{dateStr}-{(nombreDuJour + 1):D4}";

            // Securite anti-doublon : si le numero existe deja, incremente
            int tentative = 1;
            while (_context.Paiements.Any(p => p.RecuNumero == numero))
            {
                tentative++;
                numero = $"REC-{dateStr}-{(nombreDuJour + tentative):D4}";
            }

            return numero;
        }
    }
}
