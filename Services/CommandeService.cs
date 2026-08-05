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
            return context.Commandes
                .Include(c => c.Paiements)
                .Include(c => c.Client)
                .Include(c => c.Pieces).ThenInclude(p => p.Mesures)
                .Include(c => c.Pieces).ThenInclude(p => p.Couturier)
                .FirstOrDefault(c => c.IdCommande == id);
        }

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

            // Mise à jour de la pièce existante
            var pieceExistante = existante.Pieces.FirstOrDefault();

            if (pieceExistante != null)
            {
                // Verrouillage commission
                if (pieceExistante.IdCommission.HasValue && pieceExistante.MontantCouture != piece.MontantCouture)
                {
                    throw new InvalidOperationException(
                        "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                        "une commission déjà calculée et enregistrée. Annulez d'abord cette commission " +
                        "(avec motif) si le montant doit vraiment être corrigé.");
                }

                // CORRECTIF (audit) : BUG — cette garde ne comparait que le montant de
                // LA PREMIÈRE pièce à l'encaissé total de la commande. Si la commande a
                // déjà plusieurs pièces (AjouterPiece est utilisable dès aujourd'hui,
                // voir CommandesView.BtnAjouterPiece_Click), cette méthode Modifier() ne
                // touche que Pieces.FirstOrDefault() et pouvait donc soit bloquer à tort
                // une modification valide, soit — plus grave — laisser passer une baisse
                // qui fait descendre le TOTAL de la commande sous l'encaissé, parce que
                // les autres pièces n'étaient jamais comptées. ModifierPiece() calculait
                // déjà ça correctement (totalAutresPieces) ; on applique la même logique
                // ici pour que les deux chemins de modification soient cohérents.
                decimal dejaEncaisse = existante.Paiements.Where(p => !p.EstAnnule).Sum(p => p.MontantPaye);
                decimal totalAutresPieces = existante.Pieces
                    .Where(p => p.IdPieceCommande != pieceExistante.IdPieceCommande)
                    .Sum(p => p.MontantCouture);

                if (totalAutresPieces + piece.MontantCouture < dejaEncaisse)
                {
                    throw new InvalidOperationException(
                        $"Le montant total de la commande ({(totalAutresPieces + piece.MontantCouture):N0} FCFA) " +
                        $"ne peut pas être inférieur au montant déjà encaissé ({dejaEncaisse:N0} FCFA).");
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

            if (commande.Paiements.Any())
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : des paiements y sont rattachés. " +
                    "Annulez d'abord les paiements concernés (avec motif), ou changez le statut " +
                    "de la commande à \"Annulée\" plutôt que de la supprimer.");
            }

            if (commande.Pieces.Any(p => p.IdCommission.HasValue))
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette commande : au moins une de ses pièces est " +
                    "rattachée à une commission déjà enregistrée.");
            }

            context.Commandes.Remove(commande);
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

        public List<Mesure> ObtenirMesuresPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Mesures
                .Where(m => m.IdPieceCommande == idPieceCommande)
                .ToList();
        }

        // ==================================================================
        // Point 1 — Commandes multi-pièces (Étape 1b-ii)
        // ==================================================================

        public bool PeutAjouterPiece(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .FirstOrDefault(c => c.IdCommande == idCommande);
            if (commande == null) return false;
            // Aucun paiement encaissé = on peut ajouter librement
            return !commande.Paiements.Any(p => !p.EstAnnule);
        }

        public void AjouterPiece(int idCommande, PieceCommande piece, List<Mesure> mesures,
            bool roleBoss, string? motifException = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var commande = context.Commandes
                .Include(c => c.Paiements)
                .FirstOrDefault(c => c.IdCommande == idCommande)
                ?? throw new InvalidOperationException("Commande introuvable.");

            bool aPaiements = commande.Paiements.Any(p => !p.EstAnnule);

            if (aPaiements)
            {
                if (!roleBoss)
                {
                    throw new InvalidOperationException(
                        "Impossible d'ajouter une pièce : un acompte a déjà été encaissé sur cette commande. " +
                        "Seul le Boss peut ajouter une pièce avec motif obligatoire.");
                }
                if (string.IsNullOrWhiteSpace(motifException))
                {
                    throw new InvalidOperationException(
                        "L'ajout d'une pièce après encaissement nécessite un motif obligatoire (Boss).");
                }
            }

            piece.IdCommande = idCommande;
            if (string.IsNullOrWhiteSpace(piece.Statut))
                piece.Statut = "A faire";

            // CORRECTIF (audit) : conserver le motif avec la pièce, pas seulement
            // le vérifier au passage. Sans ça, rien ne prouve après coup pourquoi
            // cette pièce a été ajoutée après un encaissement — la "traçabilité"
            // promise par le cahier n'existait que dans un message de dialogue
            // qui disparaissait dès qu'on cliquait "OK".
            if (aPaiements)
                piece.MotifAjoutApresEncaissement = motifException!.Trim();

            context.PiecesCommande.Add(piece);
            context.SaveChanges();

            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = piece.IdPieceCommande;
                mesure.IdCommande = idCommande;
                context.Mesures.Add(mesure);
            }
            context.SaveChanges();
        }

        public void ModifierPiece(PieceCommande piece, List<Mesure> mesures)
        {
            using var context = _contextFactory.CreateDbContext();
            var pieceExistante = context.PiecesCommande
                .Include(p => p.Mesures)
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .FirstOrDefault(p => p.IdPieceCommande == piece.IdPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            // Verrouillage commission
            if (pieceExistante.IdCommission.HasValue && pieceExistante.MontantCouture != piece.MontantCouture)
            {
                throw new InvalidOperationException(
                    "Impossible de modifier le montant de cette pièce : elle est rattachée à " +
                    "une commission déjà calculée. Annulez d'abord cette commission.");
            }

            // Garde financière : montant ne peut pas descendre sous l'encaissé total
            var commande = pieceExistante.Commande;
            if (commande != null)
            {
                decimal dejaEncaisse = commande.Paiements
                    .Where(p => !p.EstAnnule)
                    .AsEnumerable()
                    .Sum(p => p.MontantPaye);

                // Total actuel de toutes les pièces (sauf celle modifiée)
                decimal totalAutresPieces = commande.Pieces
                    .Where(p => p.IdPieceCommande != piece.IdPieceCommande)
                    .AsEnumerable()
                    .Sum(p => p.MontantCouture);

                if (totalAutresPieces + piece.MontantCouture < dejaEncaisse)
                {
                    throw new InvalidOperationException(
                        $"Le montant de la pièce ({piece.MontantCouture:N0} FCFA) ferait descendre " +
                        $"le total de la commande sous le montant déjà encaissé ({dejaEncaisse:N0} FCFA).");
                }
            }

            pieceExistante.TypeVetement = piece.TypeVetement;
            pieceExistante.IdCouturier = piece.IdCouturier;
            pieceExistante.MontantCouture = piece.MontantCouture;
            pieceExistante.DescriptionPrecision = piece.DescriptionPrecision;
            pieceExistante.CheminPhoto = piece.CheminPhoto;
            if (!string.IsNullOrWhiteSpace(piece.Statut))
                pieceExistante.Statut = piece.Statut;

            // Remplacement des mesures
            context.Mesures.RemoveRange(pieceExistante.Mesures);
            foreach (var mesure in mesures)
            {
                mesure.IdPieceCommande = pieceExistante.IdPieceCommande;
                mesure.IdCommande = pieceExistante.IdCommande;
                context.Mesures.Add(mesure);
            }

            context.SaveChanges();
        }

        public void SupprimerPiece(int idPieceCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            var piece = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .Include(p => p.Mesures)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            if (piece.IdCommission.HasValue)
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette pièce : elle est rattachée à une commission.");
            }

            var commande = piece.Commande;
            if (commande != null && commande.Paiements.Any(p => !p.EstAnnule))
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer cette pièce : des paiements ont été encaissés sur cette commande.");
            }

            // Vérifier qu'il reste au moins une pièce si la commande en a plusieurs
            int nbPieces = context.PiecesCommande
                .Count(p => p.IdCommande == piece.IdCommande);
            if (nbPieces <= 1)
            {
                throw new InvalidOperationException(
                    "Impossible de supprimer la dernière pièce d'une commande. " +
                    "Supprimez la commande entière si nécessaire.");
            }

            context.Mesures.RemoveRange(piece.Mesures);
            context.PiecesCommande.Remove(piece);
            context.SaveChanges();
        }

        public PieceCommande DupliquerPiece(int idPieceCommandeSource)
        {
            using var context = _contextFactory.CreateDbContext();
            var source = context.PiecesCommande
                .Include(p => p.Commande)
                    .ThenInclude(c => c!.Paiements)
                .FirstOrDefault(p => p.IdPieceCommande == idPieceCommandeSource)
                ?? throw new InvalidOperationException("Pièce source introuvable.");

            if (source.Commande != null && source.Commande.Paiements.Any(p => !p.EstAnnule))
            {
                throw new InvalidOperationException(
                    "Impossible de dupliquer : des paiements ont été encaissés sur cette commande.");
            }

            var nouvellePiece = new PieceCommande
            {
                IdCommande = source.IdCommande,
                TypeVetement = source.TypeVetement,
                DescriptionPrecision = source.DescriptionPrecision,
                CheminPhoto = source.CheminPhoto,
                IdCouturier = source.IdCouturier,
                MontantCouture = source.MontantCouture,
                Statut = "A faire"
            };

            context.PiecesCommande.Add(nouvellePiece);
            context.SaveChanges();

            // Dupliquer aussi les mesures
            var mesuresSource = context.Mesures
                .Where(m => m.IdPieceCommande == idPieceCommandeSource)
                .ToList();

            foreach (var m in mesuresSource)
            {
                context.Mesures.Add(new Mesure
                {
                    IdPieceCommande = nouvellePiece.IdPieceCommande,
                    IdCommande = source.IdCommande,
                    NomMesure = m.NomMesure,
                    Valeur = m.Valeur
                });
            }
            context.SaveChanges();

            // Recharger avec navigation pour renvoyer un objet complet
            context.Entry(nouvellePiece).Reference(p => p.Couturier).Load();
            return nouvellePiece;
        }

        public void ForcerStatutToutesPieces(int idCommande, string nouveauStatut)
        {
            using var context = _contextFactory.CreateDbContext();
            var pieces = context.PiecesCommande
                .Where(p => p.IdCommande == idCommande)
                .ToList();

            if (pieces.Count == 0) return;

            // Vérifier qu'aucune pièce n'est verrouillée par une commission
            // (on ne force pas le statut d'une pièce commissionnée)
            var verrouillees = pieces.Where(p => p.IdCommission.HasValue).ToList();
            if (verrouillees.Any())
            {
                throw new InvalidOperationException(
                    $"{verrouillees.Count} pièce(s) sont rattachées à une commission et ne peuvent " +
                    "pas voir leur statut modifié par un forçage en cascade.");
            }

            foreach (var piece in pieces)
            {
                piece.Statut = nouveauStatut;
            }
            context.SaveChanges();
        }

        public List<PieceCommande> ObtenirPiecesCommande(int idCommande)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.PiecesCommande
                .Include(p => p.Couturier)
                .Include(p => p.Mesures)
                .Where(p => p.IdCommande == idCommande)
                .ToList();
        }

        public List<PieceCommande> ObtenirPiecesAnterieuresClient(int idClient, string typeVetement, int? exclureIdCommande = null)
        {
            using var context = _contextFactory.CreateDbContext();
            var query = context.PiecesCommande
                .Include(p => p.Mesures)
                .Include(p => p.Commande)
                .Where(p => p.Commande != null
                    && p.Commande.IdClient == idClient
                    && p.TypeVetement == typeVetement
                    && (p.Statut == "Terminee" || p.Statut == "Livree"));

            if (exclureIdCommande.HasValue)
                query = query.Where(p => p.IdCommande != exclureIdCommande.Value);

            return query
                .OrderByDescending(p => p.IdPieceCommande)
                .Take(10)
                .ToList();
        }
    }
}
