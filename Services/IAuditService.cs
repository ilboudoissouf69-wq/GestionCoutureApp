using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Interface pour le service d'audit immuable avec notifications externes
    /// </summary>
    public interface IAuditService
    {
        /// <summary>
        /// Enregistre une action dans le journal d'audit immuable
        /// </summary>
        /// <param name="idOperateur">ID de l'opérateur qui effectue l'action</param>
        /// <param name="nomOperateur">Nom complet de l'opérateur</param>
        /// <param name="roleOperateur">Rôle de l'opérateur (Boss, Secretaire, Couturier)</param>
        /// <param name="typeAction">Type d'action (PAIEMENT_ANNULE, COMMANDE_SUPPRIMEE, etc.)</param>
        /// <param name="entite">Type d'entité affectée (Paiement, Commande, etc.)</param>
        /// <param name="idEntite">ID de l'entité affectée</param>
        /// <param name="valeursAvant">État avant modification (objet sérialisé en JSON)</param>
        /// <param name="valeursApres">État après modification (objet sérialisé en JSON)</param>
        /// <param name="motif">Motif de l'action (obligatoire pour annulations/suppressions)</param>
        /// <param name="envoyerNotification">Si true, envoie une notification WhatsApp pour cette action critique</param>
        Task EnregistrerActionAsync(
            int idOperateur,
            string nomOperateur,
            string roleOperateur,
            string typeAction,
            string entite,
            int idEntite,
            object? valeursAvant = null,
            object? valeursApres = null,
            string? motif = null,
            bool envoyerNotification = false);

        /// <summary>
        /// Récupère toutes les entrées du journal d'audit (lecture seule)
        /// </summary>
        List<JournalAudit> ObtenirToutesLesEntrees();

        /// <summary>
        /// Récupère les entrées du journal pour une période donnée
        /// </summary>
        List<JournalAudit> ObtenirParPeriode(DateTime debut, DateTime fin);

        /// <summary>
        /// Récupère les entrées du journal pour un opérateur spécifique
        /// </summary>
        List<JournalAudit> ObtenirParOperateur(int idOperateur);

        /// <summary>
        /// Récupère les entrées du journal pour un type d'action spécifique
        /// </summary>
        List<JournalAudit> ObtenirParTypeAction(string typeAction);

        /// <summary>
        /// Récupère les entrées du journal pour une entité spécifique
        /// </summary>
        List<JournalAudit> ObtenirParEntite(string entite, int idEntite);

        /// <summary>
        /// Vérifie l'intégrité de toute la chaîne de hash du journal d'audit
        /// Retourne true si la chaîne est intacte, false si une modification a été détectée
        /// </summary>
        /// <returns>Tuple (intègre, message de diagnostic)</returns>
        (bool Integre, string Message) VerifierIntegriteChaine();

        /// <summary>
        /// Obtient des statistiques d'audit pour une période
        /// </summary>
        StatistiquesAudit ObtenirStatistiques(DateTime debut, DateTime fin);

        /// <summary>
        /// Étape 2 — Indicateur anti-fraude du Dashboard Boss : par utilisateur,
        /// nombre de suppressions, d'annulations et de modifications de prix
        /// (prix d'une pièce, ajout ou suppression de pièce) depuis <paramref name="depuisUtc"/>.
        /// Trié du plus grand total au plus petit.
        /// </summary>
        List<IndicateurAntiFraude> ObtenirIndicateurAntiFraude(DateTime depuisUtc);
    }

    /// <summary>
    /// Types d'action du journal surveillés par l'indicateur anti-fraude (Étape 2).
    /// Une seule source de vérité pour les services qui écrivent et le Dashboard qui lit.
    /// </summary>
    public static class ActionsAntiFraude
    {
        public const string Suppression    = "COMMANDE_SUPPRIMEE";
        public const string Annulation     = "COMMANDE_ANNULEE";
        public const string PrixModifie    = "COMMANDE_PRIX_MODIFIE";
        public const string PieceAjoutee   = "PIECE_AJOUTEE";
        public const string PieceSupprimee = "PIECE_SUPPRIMEE";

        /// <summary>Actions comptées comme « modification de prix » (le total facturé change).</summary>
        public static readonly string[] ModificationsPrix = { PrixModifie, PieceAjoutee, PieceSupprimee };

        public static readonly string[] Toutes = { Suppression, Annulation, PrixModifie, PieceAjoutee, PieceSupprimee };
    }

    /// <summary>Une ligne de l'indicateur anti-fraude : un utilisateur sur la période.</summary>
    public class IndicateurAntiFraude
    {
        public int IdOperateur { get; set; }
        public string NomOperateur { get; set; } = string.Empty;
        public string RoleOperateur { get; set; } = string.Empty;
        public int Suppressions { get; set; }
        public int Annulations { get; set; }
        public int ModificationsPrix { get; set; }
        public int Total => Suppressions + Annulations + ModificationsPrix;
    }

    /// <summary>
    /// Statistiques d'audit pour une période donnée
    /// </summary>
    public class StatistiquesAudit
    {
        public int NombreTotalActions { get; set; }
        public int NombreActionsSuppressionAnnulation { get; set; }
        public int NombreActionsModification { get; set; }
        public int NombreActionsCreation { get; set; }
        public int NombreNotificationsEnvoyees { get; set; }
        public Dictionary<string, int> ActionsParOperateur { get; set; } = new();
        public Dictionary<string, int> ActionsParType { get; set; } = new();
        public List<JournalAudit> ActionsCritiques { get; set; } = new();
    }
}
