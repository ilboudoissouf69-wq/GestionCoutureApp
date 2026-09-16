using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IRetourService
    {
        List<Retour> ObtenirTous();
        Retour? ObtenirParId(int id);
        void Ajouter(Retour retour);
        void Modifier(Retour retour);
        void DemarrerReprise(int idRetour, int idOperateur, string nomOperateur);
        void Resoudre(int idRetour, int idOperateur, string nomOperateur);
        void MarquerRendu(int idRetour, int idOperateur, string nomOperateur);
        void Annuler(int idRetour, string motif, string nomAnnulateur);
        List<Retour> Rechercher(string motCle);
        List<Retour> FiltrerParStatut(string statut);
        List<StatistiqueRetourCouturier> StatistiquesParCouturier(DateTime dateDebut, DateTime dateFin);
    }

    public class StatistiqueRetourCouturier
    {
        public int IdCouturier { get; set; }
        public string NomCouturier { get; set; } = string.Empty;
        public int NombreRetours { get; set; }
        public int NombreResolus { get; set; }
        public int NombreEnCours { get; set; }
    }
}