// Services/IRetourService.cs
// =============================================
// Interface du service Retour (Point 4).
// =============================================

using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IRetourService
    {
        /// <summary>Obtenir tous les retours, triés par date de signalement décroissante.</summary>
        List<Retour> ObtenirTous();

        /// <summary>Obtenir un retour par son Id.</summary>
        Retour? ObtenirParId(int id);

        /// <summary>Enregistrer un nouveau retour.</summary>
        void Ajouter(Retour retour);

        /// <summary>Faire passer un retour à "En reprise".</summary>
        void DemarrerReprise(int idRetour, int idOperateur, string nomOperateur);

        /// <summary>Faire passer un retour à "Résolu".</summary>
        void Resoudre(int idRetour, int idOperateur, string nomOperateur);

        /// <summary>Rechercher par mot-clé (nom client, description, statut).</summary>
        List<Retour> Rechercher(string motCle);

        /// <summary>Statistiques retours par couturier sur une période.</summary>
        List<StatistiqueRetourCouturier> StatistiquesParCouturier(DateTime dateDebut, DateTime dateFin);
    }

    /// <summary>DTO pour les statistiques retours par couturier.</summary>
    public class StatistiqueRetourCouturier
    {
        public int IdCouturier { get; set; }
        public string NomCouturier { get; set; } = string.Empty;
        public int NombreRetours { get; set; }
        public int NombreResolus { get; set; }
        public int NombreEnCours { get; set; }
    }
}