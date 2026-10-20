// Services/ICommandeService.cs
// Interface du service Commande.
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface ICommandeService
    {
        // Événement déclenché après toute modification d'une commande.
        // Permet aux vues ouvertes (PaiementsView, etc.) de se rafraîchir automatiquement.
        event EventHandler<CommandeChangedEventArgs>? CommandeChanged;

        List<Commande> ObtenirTous();
        Commande? ObtenirParId(int id);

        // Récupère les commandes avec pagination
        Task<PagedResult<Commande>> ObtenirPageAsync(int page, int pageSize);
        
        // Version légère pour affichage tableau (sans toutes les données incluses)
        Task<PagedResult<Commande>> ObtenirPageLightAsync(int page, int pageSize);

        /// <summary>
        /// Version avec tous les filtres : recherche, statut, couturier,
        /// période de dépôt (dateDebut/dateFin sur DateDebut).
        /// <paramref name="inclureSupprimees"/> : réservé au Boss (« Afficher les supprimées »).
        /// </summary>
        Task<PagedResult<Commande>> ObtenirPageLightAsync(
            int page, int pageSize,
            string? motCle,
            string? statut,
            int? idCouturier,
            DateTime? dateDebut,
            DateTime? dateFin,
            bool inclureSupprimees = false);

        /// <summary>
        /// Crée une nouvelle commande avec sa première pièce et ses mesures.
        /// <paramref name="idOperateur"/> et <paramref name="nomOperateur"/> sont
        /// automatiquement renseignés depuis <c>AuthService.UtilisateurConnecte</c>
        /// par l'appelant — aucune ressaisie de mot de passe, zéro friction.
        /// </summary>
        /// <exception cref="DoublonCommandeException">
        /// Levée si une commande quasi-identique (même client + type + montant) a été
        /// créée par le même opérateur dans les 60 dernières secondes.
        /// </exception>
        /// <para>
        /// Commande, pièce, mesures et <paramref name="materiaux"/> sont enregistrés
        /// en une seule transaction : tout ou rien.
        /// </para>
        /// <para>
        /// <paramref name="piecesSupplementaires"/> : pièces 2..n saisies dans la modale de
        /// création — elles font partie de la création (pas de motif exigé), contrairement
        /// à <see cref="AjouterPiece"/> qui sert aux ajouts APRÈS création.
        /// </para>
        void Ajouter(Commande commande, PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, List<MaterielSupplement>? materiaux = null,
            List<(PieceCommande Piece, List<Mesure> Mesures, List<MaterielSupplement>? Materiaux)>? piecesSupplementaires = null);

        /// <summary>
        /// Modifie les informations de niveau commande (dates, heures) et optionnellement
        /// la pièce associée. Boss ou Secrétaire uniquement.<br/>
        /// La Secrétaire ne peut modifier que : date livraison, heures, description, couturier, statut.
        /// Elle NE PEUT PAS modifier : montant/prix, type de vêtement, client.
        /// Le service lève <see cref="InvalidOperationException"/> si elle tente de modifier
        /// un champ réservé au Boss, même en contournant l'écran.
        /// </summary>
        /// <param name="idOperateur">Id de l'opérateur (Boss ou Secrétaire).</param>
        /// <param name="nomOperateur">Nom pour l'audit.</param>
        /// <summary>
        /// Modifie une commande et sa première pièce en une seule transaction (méthode legacy mono-pièce).
        /// Pour les commandes multi-pièces, utiliser <see cref="ModifierPiece"/> +
        /// <see cref="ModifierInfosCommande"/> séparément afin d'éviter de corrompre les pièces
        /// avec une pièce fantôme vide quand aucune pièce n'est sélectionnée.
        /// </summary>
        [Obsolete("Utiliser ModifierPiece() + ModifierInfosCommande() séparément pour éviter la corruption de pièces vides.")]
        void Modifier(Commande commande, PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, string? motifModification = null);

        /// <summary>
        /// Modifie UNIQUEMENT les métadonnées de la commande (client, dates, heures)
        /// sans toucher aux pièces. À utiliser depuis la modale multi-pièces.
        /// </summary>
        void ModifierInfosCommande(int idCommande, int idClient, DateTime dateFin,
            TimeSpan heureDebut, TimeSpan? heureFin, int idOperateur, string nomOperateur);

        // Suppression logique — BOSS UNIQUEMENT (Étape 2), motif obligatoire, audit atomique (Étape 10).
        Task SupprimerAsync(int id, int idOperateur, string nomOperateur, string motif);

        // ===== Étape 2 — Annulation rapide par la Secrétaire =====

        /// <summary>
        /// La Secrétaire peut-elle ANNULER cette commande ? Conditions cumulatives :
        /// elle l'a créée, moins de N minutes depuis DateCreation (Parametres
        /// « delai_annulation_secretaire_minutes », défaut 10), aucun paiement lié
        /// (même annulé), toutes les pièces « A faire », reçu/ticket jamais imprimé.
        /// </summary>
        (bool Autorise, string Raison) PeutAnnulerParSecretaire(int idCommande, int idOperateur);

        /// <summary>
        /// Annule la commande (retirée de l'activité comme une suppression, mais
        /// marquée EstAnnulee), auditée COMMANDE_ANNULEE. Lève InvalidOperationException
        /// (« … Demandez au Boss. ») si une condition n'est pas remplie.
        /// </summary>
        Task AnnulerParSecretaireAsync(int idCommande, int idOperateur, string nomOperateur, string? motif = null);

        /// <summary>
        /// Renseigne Commande.DateImpressionRecu à la PREMIÈRE impression du reçu ou
        /// de la fiche atelier. Les réimpressions ne changent pas la date.
        /// </summary>
        void MarquerRecuImprime(int idCommande);

        [Obsolete("Utilisez SupprimerAsync avec traçabilité complète")]
        void Supprimer(int id);
        
        List<Commande> Rechercher(string motCle);
        
        // Cherche des commandes avec pagination
        Task<PagedResult<Commande>> RechercherPageAsync(string motCle, int page, int pageSize);
        
        // Version légère de recherche pour affichage tableau
        Task<PagedResult<Commande>> RechercherPageLightAsync(string motCle, int page, int pageSize);

        List<Mesure> ObtenirMesuresPiece(int idPieceCommande);

        // ===== Point 1 — Commandes multi-pièces (Étape 1b-ii) =====

        /// <summary>
        /// Ajoute une pièce supplémentaire à une commande existante.
        /// Lève InvalidOperationException si un paiement a déjà été encaissé
        /// (sauf si roleBoss=true, avec motif obligatoire).
        /// Étape 2 : si <paramref name="idOperateur"/> est fourni, son rôle réel en base
        /// remplace <paramref name="roleBoss"/>. Secrétaire avant paiement : motif obligatoire
        /// (passé dans <paramref name="motifException"/>), interdit après impression du reçu.
        /// </summary>
        void AjouterPiece(int idCommande, PieceCommande piece, List<Mesure> mesures,
            bool roleBoss, string? motifException = null,
            List<MaterielSupplement>? materiaux = null, int idOperateur = 0, string nomOperateur = "");

        /// <summary>
        /// Modifie une pièce existante identifiée par son IdPieceCommande.
        /// <para>
        /// Boss ou Secrétaire uniquement. Seul le Boss peut changer le prix
        /// (tracé dans le journal d'audit). Passer en "Livree" une commande non
        /// soldée lève <see cref="LivraisonNonSoldeeException"/> — sauf Boss avec
        /// <paramref name="motifLivraisonNonSoldee"/>.
        /// </para>
        /// </summary>
        /// <para>
        /// Étape 2 — Secrétaire : prix modifiable avant paiement uniquement, avec
        /// <paramref name="motifModification"/> obligatoire, et plus du tout après impression
        /// du reçu. Pièce Terminée/Livrée : lecture seule pour tous (seul un changement de
        /// statut via le noyau reste possible).
        /// </para>
        void ModifierPiece(PieceCommande piece, List<Mesure> mesures,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null,
            string? motifModification = null);

        /// <summary>
        /// Supprime une pièce d'une commande.
        /// Boss uniquement (UnauthorizedAccessException sinon), tracé dans l'audit.
        /// Lève InvalidOperationException si un paiement existe sur la commande.
        /// </summary>
        /// <para>Étape 2 — Secrétaire : <paramref name="motif"/> obligatoire, interdit après impression.</para>
        void SupprimerPiece(int idPieceCommande, int idOperateur, string nomOperateur, string? motif = null);

        /// <summary>
        /// Duplique une pièce existante (sans les mesures — la secrétaire
        /// ajustera si besoin). Renvoie la nouvelle pièce créée.
        /// </summary>
        /// Mêmes règles qu'un ajout de pièce (motif Secrétaire, audit PIECE_AJOUTEE).
        PieceCommande DupliquerPiece(int idPieceCommandeSource, int idOperateur,
            string nomOperateur, string? motif = null);

        /// <summary>
        /// Force le statut de toutes les pièces d'une commande.
        /// <para>
        /// Accessible aux rôles <b>Boss</b> et <b>Secrétaire</b> (action sans enjeu
        /// financier). Le couple <paramref name="idOperateur"/>/<paramref name="nomOperateur"/>
        /// est tracé dans les logs pour conserver un historique de qui change les statuts.
        /// </para>
        /// </summary>
        /// <para>
        /// "Livree" sur une commande non soldée : même règle que <see cref="ModifierPiece"/>.
        /// </para>
        void ForcerStatutToutesPieces(int idCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null);

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

        // ===== StatutView — Vue plate de toutes les pièces =====

        /// <summary>
        /// Renvoie une page paginée de pièces avec leurs données de contexte
        /// (commande, client, couturier). Filtre optionnel par statut.
        /// <para>
        /// <paramref name="statut"/> null = toutes les pièces (onglet "Tous").
        /// Valeurs acceptées : "A faire", "En cours", "Terminee", "Livree", "Retard".
        /// "Retard" est un filtre virtuel calculé : pièces dont <c>Commande.DateFin</c>
        /// est passée et dont le statut est "A faire" ou "En cours".
        /// </para>
        /// <para>
        /// Le tri par défaut place les retards en premier (DateFin croissante pour les
        /// retards, puis DateFin croissante pour les autres).
        /// </para>
        /// </summary>
        Task<PagedResult<PieceCommande>> ObtenirPagePiecesAsync(
            string? statut, int page, int pageSize, string? recherche = null);

        // ===== StatutView V2 — Vue par commande =====

        /// <summary>
        /// Renvoie une page paginée de commandes pour l'écran Statut, avec toutes
        /// les navigations nécessaires (Pieces+Couturier, Client, Paiements,
        /// MaterielSupplements). Évite le N+1. Filtre optionnel par statut global
        /// de la commande (calculé depuis ses pièces).
        /// <para>
        /// Valeurs de filtre : null = toutes, "A faire", "En cours", "Terminee",
        /// "Livree", "Retard" (commandes dont DateFin est dépassée et dont au moins
        /// une pièce est "A faire" ou "En cours").
        /// </para>
        /// </summary>
        Task<PagedResult<Commande>> ObtenirPageCommandesStatutAsync(
            string? statut, int page, int pageSize, string? recherche = null);

        /// <summary>
        /// Change le statut d'une seule pièce sans toucher aux mesures ni au prix.
        /// <para>
        /// Boss et Secrétaire uniquement. Le couturier est bloqué côté service.
        /// Lève <see cref="LivraisonNonSoldeeException"/> si la commande n'est pas
        /// soldée et que le nouveau statut est "Livree" (sauf Boss avec motif).
        /// Les pièces verrouillées par une commission ne peuvent pas changer de statut.
        /// </para>
        /// </summary>
        void ChangerStatutPiece(int idPieceCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motifLivraisonNonSoldee = null);

        /// <summary>
        /// POINT D'ENTRÉE UNIQUE pour tout changement de statut d'une pièce.
        /// Flux strict A faire→En cours→Terminee→Livree, sauts interdits,
        /// Livree→autre interdit (passer par module Retour), rétrogradation Boss+motif.
        /// Pour une rétrogradation de pièce commissionnée, passer commissionService
        /// afin d'annuler et rétrograder dans une seule transaction atomique.
        /// </summary>
        void TransitionnerStatutPiece(int idPieceCommande, string nouveauStatut,
            int idOperateur, string nomOperateur, string? motif = null,
            CommissionService? commissionService = null);

        // TÂCHE 5 — File "À attribuer"
        /// <summary>
        /// Nombre de pièces actives (A faire ou En cours) sans couturier assigné.
        /// Utilisé pour le badge Dashboard et le compteur de menu.
        /// </summary>
        int CompterPiecesAAttribuer();

        /// <summary>
        /// Retourne la page de commandes ayant au moins une pièce sans couturier
        /// (statut A faire ou En cours). Utilisé par la file "À attribuer".
        /// </summary>
        Task<PagedResult<Commande>> ObtenirCommandesAAttribuerAsync(int page, int pageSize);

        // TÂCHE 6 — Suggestion couturier
        /// <summary>
        /// Suggère le couturier actif le moins chargé (le moins de pièces
        /// En attente/En cours). En cas d'égalité : celui qui a le moins terminé
        /// récemment (DateTerminee la plus ancienne). Les employés dont le
        /// statut est "Indisponible" sont exclus.
        /// Retourne null si aucun couturier actif disponible.
        /// </summary>
        Employe? SuggererCouturier();
    }
}