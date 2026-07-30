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
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        public Commande? ObtenirParId(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            // Include(Pieces) + sous-Include(Mesures/Couturier) indispensable :
            // Commande.ResteAPayer / MontantEncaisse / MontantTotalCalcule /
            // TypeVetementAffiche / StatutGlobal sont des propriétés calculées
            // qui lisent Pieces et Paiements en mémoire. Sans ces Include, EF
            // Core ne lève aucune erreur : les collections restent simplement
            // vides, et ces propriétés renvoient silencieusement des valeurs
            // fausses (même piège que celui déjà corrigé pour Paiements en
            // Étape 0 — voir le commentaire original resté au-dessus des
            // propriétés calculées).
            return context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .FirstOrDefault(c => c.IdCommande == id);
        }

        // ÉTAPE 1b-i (Point 1 — Commandes multi-pièces) : "piece" porte
        // maintenant TypeVetement/IdCouturier/MontantCouture. CommandesView
        // n'en construit qu'UNE SEULE pour l'instant (garanti jusqu'à l'UI
        // multi-pièces de l'Étape 1b-ii) — mais le modèle de données, lui,
        // est déjà le modèle final.
        public void Ajouter(Commande commande, PieceCommande piece, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            commande.DateDebut = DateTime.Now;
            context.Commandes.Add(commande);
            context.SaveChanges(); // génère IdCommande

            piece.IdCommande = commande.IdCommande;
            if (string.IsNullOrWhiteSpace(piece.Statut))
                piece.Statut = "A faire";
            context.PiecesCommande.Add(piece);
            context.SaveChanges(); // génère IdPieceCommande

            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = piece.IdPieceCommande;
                // La colonne IdCommande de Mesure reste NOT NULL en base pour
                // l'instant (héritage d'avant l'Étape 1) : on la renseigne
                // donc encore en parallèle d'IdPieceCommande. L'Étape 1c la
                // rendra optionnelle puis la retirera une fois plus aucune
                // vue ne s'appuyant dessus.
                mesure.IdCommande = commande.IdCommande;
                context.Mesures.Add(mesure);
            }
            context.SaveChanges();
        }

        public void Modifier(Commande commande, PieceCommande piece, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            var existante = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .FirstOrDefault(c => c.IdCommande == commande.IdCommande);

            if (existante == null) return;

            // ÉTAPE 1b-i : tant qu'une commande n'a qu'une seule pièce, on la
            // retrouve directement ainsi. L'UI multi-pièces (Étape 1b-ii)
            // devra remplacer ceci par une mise à jour ciblée par
            // IdPieceCommande plutôt que "la première pièce trouvée".
            var pieceExistante = existante.Pieces.FirstOrDefault();

            if (pieceExistante != null)
            {
                // CORRECTIF conservé (incohérence métier) : une pièce déjà
                // incluse dans une commission calculée et enregistrée voit
                // son montant figé dans l'historique de cette commission.
                if (pieceExistante.IdCommission.HasValue && pieceExistante.MontantCouture != piece.MontantCouture)
                {
                    throw new InvalidOperationException(
                        "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                        "une commission déjà calculée et enregistrée. Annulez d'abord cette commission " +
                        "(avec motif) si le montant doit vraiment être corrigé.");
                }

                // CORRECTIF conservé (incohérence financière) : le montant ne
                // peut pas descendre sous ce qui a déjà été encaissé.
                decimal dejaEncaisse = existante.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                if (piece.MontantCouture < dejaEncaisse)
                {
                    throw new InvalidOperationException(
                        $"Le montant total ({piece.MontantCouture:N0} FCFA) ne peut pas être inférieur " +
                        $"au montant déjà encaissé sur cette commande ({dejaEncaisse:N0} FCFA).");
                }

                pieceExistante.TypeVetement = piece.TypeVetement;
                pieceExistante.IdCouturier = piece.IdCouturier;
                pieceExistante.MontantCouture = piece.MontantCouture;
                pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
                pieceExistante.CheminPhoto = piece.CheminPhoto;
                if (!string.IsNullOrWhiteSpace(piece.Statut))
                    pieceExistante.Statut = piece.Statut;
                context.Mesures.RemoveRange(pieceExistante.Mesures);
                foreach (var mesure in mesures)
                {
                    mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                    mesure.IdCommande = existante.IdCommande;
                    context.Mesures.Add(mesure);
                }
            }
            else
            {
                // Cas limite : une commande sans aucune pièce (ne devrait
                // plus se produire une fois cette étape en place, mais on
                // couvre le cas plutôt que de silencieusement ignorer la
                // pièce fournie).
                piece.IdCommande = existante.IdCommande;
                if (string.IsNullOrWhiteSpace(piece.Statut))
                    piece.Statut = "A faire";
                context.PiecesCommande.Add(piece);
                context.SaveChanges();

                foreach (var mesure in mesures)
                {
                    mesure.IdPieceCommande = piece.IdPieceCommande;
                    mesure.IdCommande = existante.IdCommande;
                    context.Mesures.Add(mesure);
                }
            }

            existante.DateFin = commande.DateFin;
            existante.HeureDebut = commande.HeureDebut;
            existante.HeureFin = commande.HeureFin;

            context.SaveChanges();
        }

        public void Supprimer(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Pieces)
                .FirstOrDefault(c => c.IdCommande == id);

            if (commande == null) return;

            // Une commande ayant déjà reçu un paiement ne doit jamais être supprimée :
            // cela effacerait silencieusement l'historique financier.
            if (commande.Paiements.Any())
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : des paiements y sont rattachés. " +
                    "Annulez d'abord les paiements concernés (avec motif), ou changez le statut " +
                    "de la commande à \"Annulée\" plutôt que de la supprimer.");
            }

            // ÉTAPE 1b-i : le verrouillage commission se vérifie maintenant
            // au niveau de chaque pièce, plus au niveau de la commande.
            if (commande.Pieces.Any(p => p.IdCommission.HasValue))
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : au moins une de ses pièces est " +
                    "rattachée à une commission déjà enregistrée.");
            }

            context.Commandes.Remove(commande); // cascade vers Pieces (voir DbContext)
            context.SaveChanges();
        }

        public List<Commande> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Commandes
                .Include(c => c.Client)
                .Include(c => c.Paiements)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Where(c => c.Client != null && (
                         c.Client.Nom.Contains(motCle)
                         || c.Client.Prenom.Contains(motCle)
                         || c.Pieces.Any(p => p.TypeVetement.Contains(motCle))
                         || c.Pieces.Any(p => p.Statut.Contains(motCle))))
                .OrderByDescending(c => c.DateDebut)
                .ToList();
        }

        // Renommée depuis ObtenirMesures(idCommande) : lit maintenant les
        // mesures d'une PIÈCE précise, pas d'une commande entière — le
        // renommage est volontaire pour qu'un appel resté sur l'ancienne
        // signature soit détecté à la compilation plutôt que de
        // silencieusement renvoyer une liste vide.
        public List<Mesure> ObtenirMesuresPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Mesures
                .Where(m => m.IdPieceCommande == idPieceCommande)
                .ToList();
        }
    }
}
