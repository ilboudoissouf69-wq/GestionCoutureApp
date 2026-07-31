// Services/ICommandeService.cs
// Interface du service Commande.
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface ICommandeService
    {
        List<Commande> ObtenirTous();
        Commande? ObtenirParId(int id);

        void Ajouter(Commande commande, PieceCommande piece, List<Mesure> mesures);
        void Modifier(Commande commande, PieceCommande piece, List<Mesure> mesures);

        void Supprimer(int id);
        List<Commande> Rechercher(string motCle);

        List<Mesure> ObtenirMesuresPiece(int idPieceCommande);

        // ===== Point 1 — Commandes multi-pièces (Étape 1b-ii) =====

        /// <summary>
        /// Ajoute une pièce supplémentaire à une commande existante.
        /// Lève InvalidOperationException si un paiement a déjà été encaissé
        /// (sauf si roleBoss=true, avec motif obligatoire).
        /// </summary>
        void AjouterPiece(int idCommande, PieceCommande piece, List<Mesure> mesures,
            bool roleBoss, string? motifException = null);

        /// <summary>
        /// Modifie une pièce existante identifiée par son IdPieceCommande.
        /// </summary>
        void ModifierPiece(PieceCommande piece, List<Mesure> mesures);

        /// <summary>
        /// Supprime une pièce d'une commande.
        /// Lève InvalidOperationException si un paiement existe sur la commande.
        /// </summary>
        void SupprimerPiece(int idPieceCommande);

        /// <summary>
        /// Duplique une pièce existante (sans les mesures — la secrétaire
        /// ajustera si besoin). Renvoie la nouvelle pièce créée.
        /// </summary>
        PieceCommande DupliquerPiece(int idPieceCommandeSource);

        /// <summary>
        /// Force le statut de toutes les pièces d'une commande.
        /// </summary>
        void ForcerStatutToutesPieces(int idCommande, string nouveauStatut);

        /// <summary>
        /// Vérifie si une commande accepte encore l'ajout de pièces
        /// (aucun paiement encaissé, ou role Boss avec motif).
        /// </summary>
        bool PeutAjouterPiece(int idCommande);

        /// <summary>
        /// Renvoie les pièces d'une commande avec leurs mesures et couturier.
        /// </summary>
        List<PieceCommande> ObtenirPiecesCommande(int idCommande);

        /// <summary>
        /// Renvoie les pièces précédentes d'un client pour un type de vêtement donné
        /// (pour la réutilisation des mesures).
        /// </summary>
        List<PieceCommande> ObtenirPiecesAnterieuresClient(int idClient, string typeVetement, int? exclureIdCommande = null);
    }
}