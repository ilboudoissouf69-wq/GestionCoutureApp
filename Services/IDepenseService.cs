using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IDepenseService
    {
        List<Depense> ObtenirTous();
        void Ajouter(Depense depense);

        // CORRECTIF (audit — Décision 3.1) : remplace Supprimer(int id).
        // Voir DepenseService.Annuler pour la justification complète.
        void Annuler(int idDepense, string motif, string nomAnnulateur);

        decimal TotalParPeriode(DateTime debut, DateTime fin);
    }
}
