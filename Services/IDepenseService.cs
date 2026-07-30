using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IDepenseService
    {
        List<Depense> ObtenirTous();
        void Ajouter(Depense depense);
        void Supprimer(int id);
        decimal TotalParPeriode(DateTime debut, DateTime fin);
    }
}