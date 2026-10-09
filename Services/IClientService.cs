// Services/IClientService.cs
// Interface du service Client.
using GestionCoutureApp.Models;


namespace GestionCoutureApp.Services
{
    public interface IClientService
    {
        // Récupère tous les clients de la base
        List<Client> ObtenirTous();

        // ✅ PAGINATION : Récupère les clients avec pagination
        Task<PagedResult<Client>> ObtenirPageAsync(int page, int pageSize);

        /// <summary>
        /// Ajoute un nouveau client.
        /// </summary>
        /// <exception cref="DuplicatClientException">
        /// Levée si un client avec le même nom+prénom (normalisés, sans accents/casse)
        /// ET le même numéro de téléphone existe déjà. L'UI peut intercepter cette
        /// exception pour proposer la fiche existante au lieu de créer un doublon.
        /// </exception>
        void Ajouter(Client client);

        // Met à jour un client existant
        void Modifier(Client client);

        // Supprime un client par son Id
        void Supprimer(int id);

        // Cherche des clients par nom ou téléphone
        List<Client> Rechercher(string motCle);

        // ✅ PAGINATION : Cherche des clients avec pagination
        Task<PagedResult<Client>> RechercherPageAsync(string motCle, int page, int pageSize);

        /// <summary>
        /// Rapport de détection des doublons existants en base (clients ayant le même
        /// nom+prénom normalisé, avec ou sans téléphone identique).
        /// Utilisé par le Boss pour décider de fusions manuelles.
        /// </summary>
        List<GroupeDoublonsClient> RechercherDoublons();

        // ── Méthodes de LECTURE pour la vue Clients ──────────────────────

        /// <summary>
        /// Retourne en une seule requête GROUP BY le nombre de commandes non supprimées
        /// et la date de la dernière commande pour chaque client de la page.
        /// Pas de N+1 : une seule requête pour tous les ids fournis.
        /// </summary>
        Dictionary<int, StatsCommandesClient> ObtenirStatistiquesCommandes(
            IEnumerable<int> idsClients);

        /// <summary>
        /// Retourne l'historique des <paramref name="max"/> dernières commandes non
        /// supprimées du client, triées du plus récent au plus ancien.
        /// </summary>
        List<HistoriqueCommandeClient> ObtenirHistoriqueCommandes(int idClient, int max = 20);

        /// <summary>
        /// Retourne les mesures de la pièce la plus récente du client,
        /// ou null si aucune mesure n'existe.
        /// </summary>
        DernieresMesuresClient? ObtenirDernieresMesures(int idClient);
    }

    /// <summary>
    /// Groupe de clients potentiellement en doublon, renvoyé par
    /// <see cref="IClientService.RechercherDoublons"/>.
    /// </summary>
    public class GroupeDoublonsClient
    {
        /// <summary>Nom+Prénom normalisés (clé de regroupement).</summary>
        public string CleNormalise { get; set; } = string.Empty;

        /// <summary>Liste des fiches qui partagent cette clé.</summary>
        public List<Client> Clients { get; set; } = new();
    }
    
    /// <summary>
    /// Résultat paginé générique
    /// </summary>
    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int TotalCount { get; set; }
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);
        public bool HasPrevious => Page > 1;
        public bool HasNext => Page < TotalPages;
    }

    // ── DTOs de lecture pour la vue Clients ────────────────────────────────

    /// <summary>
    /// Statistiques (nb commandes + date dernière commande) pour un client.
    /// Retourné par <see cref="IClientService.ObtenirStatistiquesCommandes"/>.
    /// </summary>
    public class StatsCommandesClient
    {
        public int       IdClient        { get; set; }
        public int       NbCommandes     { get; set; }
        public DateTime? DerniereCommande { get; set; }
    }

    /// <summary>
    /// Ligne d'historique de commande affichée dans la fiche client.
    /// Retourné par <see cref="IClientService.ObtenirHistoriqueCommandes"/>.
    /// </summary>
    public class HistoriqueCommandeClient
    {
        public int     IdCommande   { get; set; }
        public string  ResumePieces { get; set; } = string.Empty;
        public DateTime DateDebut   { get; set; }
        public string  StatutAffiche { get; set; } = string.Empty;
        public decimal ResteAPayer  { get; set; }
    }

    /// <summary>
    /// Mesures de la pièce la plus récente d'un client.
    /// Retourné par <see cref="IClientService.ObtenirDernieresMesures"/>.
    /// </summary>
    public class DernieresMesuresClient
    {
        /// <summary>Ex. : « Pantalon — 12/08/2026 »</summary>
        public string LabelSource { get; set; } = string.Empty;
        public List<Models.Mesure> Mesures { get; set; } = new();
    }
}