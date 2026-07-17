// Services/IClientService.cs
// Interface du service Client.
// Définit les opérations CRUD possibles sur les clients.

using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public interface IClientService
    {
        // Récupère tous les clients de la base
        List<Client> ObtenirTous();

        // Ajoute un nouveau client en base
        void Ajouter(Client client);

        // Met à jour un client existant
        void Modifier(Client client);

        // Supprime un client par son Id
        void Supprimer(int id);

        // Cherche des clients par nom ou téléphone
        List<Client> Rechercher(string motCle);
    }
}