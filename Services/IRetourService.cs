using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IRetourService
    {
        // ── Lecture ───────────────────────────────────────────────────────
        List<Retour> ObtenirTous();
        Retour? ObtenirParId(int id);

        /// <summary>Page paginée avec filtres optionnels (motCle cherche aussi sur NumeroRetour et Telephone).</summary>
        Task<PagedResult<Retour>> ObtenirPageAsync(
            int page, int pageSize,
            string? motCle = null,
            string? statut = null,
            DateTime? dateDebut = null,
            DateTime? dateFin = null);

        List<Retour> Rechercher(string motCle);
        List<Retour> FiltrerParStatut(string statut);

        // ── Écriture ──────────────────────────────────────────────────────
        /// <summary>Crée un retour et génère son NumeroRetour en transaction atomique.</summary>
        void Ajouter(Retour retour);
        void Modifier(Retour retour);

        // ── Transitions d'état ────────────────────────────────────────────
        void DemarrerReprise(int idRetour, int idOperateur, string nomOperateur);

        /// <summary>
        /// Passe à "Termine". Incrémente les compteurs couturiers (imputation + reprise).
        /// </summary>
        void Resoudre(int idRetour, int idOperateur, string nomOperateur);

        /// <summary>
        /// Passe à "Rendu". Interdit si la reprise est payante et qu'il reste un solde.
        /// </summary>
        void MarquerRendu(int idRetour, int idOperateur, string nomOperateur);

        /// <summary>
        /// Retour arrière : Boss uniquement, motif obligatoire.
        /// Si le retour était "Termine", décrémente les compteurs.
        /// </summary>
        void ReculerStatut(int idRetour, string motif, int idOperateur, string nomOperateur, string roleOperateur);

        /// <summary>
        /// Annule un retour (jamais de suppression physique).
        /// Si le retour était "Termine", décrémente les compteurs.
        /// </summary>
        void Annuler(int idRetour, string motif, string nomAnnulateur);

        // ── Statistiques ─────────────────────────────────────────────────
        List<StatistiqueRetourCouturier> StatistiquesParCouturier(DateTime dateDebut, DateTime dateFin);

        // ── Impression (audit réimpression) ──────────────────────────────
        /// <summary>Enregistre la (ré)impression d'un ticket dans le journal d'audit.</summary>
        Task EnregistrerImpressionAsync(int idRetour, string typeTicket, int idOperateur, string nomOperateur, string roleOperateur);
    }

    /// <summary>Statistiques retours pour un couturier sur une période.</summary>
    public class StatistiqueRetourCouturier
    {
        public int IdCouturier { get; set; }
        public string NomCouturier { get; set; } = string.Empty;

        /// <summary>Retours imputés : ce couturier est responsable du défaut (IdCouturier).</summary>
        public int RetoursImputes { get; set; }

        /// <summary>Reprises effectuées : ce couturier a fait la reprise (IdCouturierReprise ou IdCouturier si null).</summary>
        public int ReprisesEffectuees { get; set; }

        /// <summary>Parmi les retours imputés, ceux qui sont Terminés ou Rendus.</summary>
        public int NombreResolus { get; set; }

        /// <summary>Parmi les retours imputés, ceux qui sont encore en cours.</summary>
        public int NombreEnCours { get; set; }

        /// <summary>Pièces livrées/terminées sur la période (pour le taux).</summary>
        public int PiecesLivrees { get; set; }

        /// <summary>
        /// Taux de retour = RetoursImputes / PiecesLivrees * 100.
        /// 0 si PiecesLivrees == 0.
        /// </summary>
        public double TauxRetour => PiecesLivrees > 0
            ? Math.Round(100.0 * RetoursImputes / PiecesLivrees, 1)
            : 0.0;

        // Aliases de compatibilité avec l'ancien code qui utilisait NombreRetours
        public int NombreRetours => RetoursImputes;

        // Date de la dernière résolution (pour le tri)
        public DateTime? DateDerniereResolution { get; set; }
    }
}
