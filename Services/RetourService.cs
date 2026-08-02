// Services/RetourService.cs
// =============================================
// Implémentation du service Retour (Point 4).
//
// Règles :
//   - Un retour ne peut passer à "En reprise" que s'il est "Signalé".
//   - Un retour ne peut passer à "Résolu" que s'il est "En reprise".
//   - Jamais de suppression physique : la suppression n'existe pas dans
//     l'interface (même philosophie que les paiements).
// =============================================

using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;

namespace GestionCoutureApp.Services
{
    public class RetourService : IRetourService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        public RetourService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public List<Retour> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Retours
                .Include(r => r.Commande)
                    .ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .OrderByDescending(r => r.DateSignalement)
                .ToList();
        }

        public Retour? ObtenirParId(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Retours
                .Include(r => r.Commande)
                    .ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .FirstOrDefault(r => r.IdRetour == id);
        }

        public void Ajouter(Retour retour)
        {
            using var context = _contextFactory.CreateDbContext();

            // CORRECTIF (audit) : le cahier des charges est explicite — "Après
            // livraison, si un client revient...". Rien n'empêchait jusqu'ici
            // de signaler un retour sur une pièce qui n'a même pas encore été
            // livrée (voire pas commencée), ce qui n'a pas de sens métier :
            // un retour, par définition, concerne un travail déjà rendu au
            // client que celui-ci juge insatisfaisant.
            var piece = context.PiecesCommande.Find(retour.IdPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            if (piece.Statut != "Livree")
                throw new InvalidOperationException(
                    "Impossible d'enregistrer un retour : cette pièce n'a pas encore été " +
                    "livrée au client (statut actuel : " + piece.StatutAffiche + ").");

            retour.DateSignalement = DateTime.Now;
            context.Retours.Add(retour);
            context.SaveChanges();
        }

        // CORRECTIF (audit) : méthode manquante malgré la documentation du
        // modèle Retour qui l'annonçait déjà. Même mécanisme que Paiement,
        // Commission et Depense — jamais de suppression, annulation tracée.
        public void Annuler(int idRetour, string motif, string nomAnnulateur)
        {
            if (string.IsNullOrWhiteSpace(motif))
                throw new InvalidOperationException("Le motif d'annulation est obligatoire.");

            using var context = _contextFactory.CreateDbContext();

            var retour = context.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.EstAnnule)
                throw new InvalidOperationException("Ce retour est déjà annulé.");

            retour.EstAnnule = true;
            retour.MotifAnnulation = motif.Trim();
            retour.DateAnnulation = DateTime.Now;
            retour.NomAnnulateur = nomAnnulateur;

            context.SaveChanges();
        }

        public void DemarrerReprise(int idRetour, int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            var retour = context.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.Statut != "Signale")
                throw new InvalidOperationException(
                    "Seul un retour 'Signalé' peut passer en reprise. Actuel : " + retour.Statut);

            retour.Statut = "En reprise";
            context.SaveChanges();
        }

        public void Resoudre(int idRetour, int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            var retour = context.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.Statut != "En reprise")
                throw new InvalidOperationException(
                    "Seul un retour 'En reprise' peut être résolu. Actuel : " + retour.Statut);

            retour.Statut = "Resolu";
            retour.DateResolution = DateTime.Now;
            retour.IdOperateurResolution = idOperateur;
            retour.NomOperateurResolution = nomOperateur;
            context.SaveChanges();
        }

        public List<Retour> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.Retours
                .Include(r => r.Commande)
                    .ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .Where(r =>
                    (r.Commande != null && r.Commande.Client != null &&
                     (r.Commande.Client.Nom.Contains(motCle) ||
                      r.Commande.Client.Prenom.Contains(motCle))) ||
                    r.DescriptionProbleme.Contains(motCle) ||
                    r.Statut.Contains(motCle) ||
                    (r.Couturier != null &&
                     (r.Couturier.Nom.Contains(motCle) ||
                      r.Couturier.Prenom.Contains(motCle))))
                .OrderByDescending(r => r.DateSignalement)
                .ToList();
        }

        public List<StatistiqueRetourCouturier> StatistiquesParCouturier(
            DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();

            var retours = context.Retours
                .Include(r => r.Couturier)
                .Where(r => r.DateSignalement.Date >= dateDebut.Date &&
                            r.DateSignalement.Date <= dateFin.Date)
                .ToList();

            return retours
                .GroupBy(r => new { r.IdCouturier, r.Couturier!.Prenom, r.Couturier.Nom })
                .Select(g => new StatistiqueRetourCouturier
                {
                    IdCouturier = g.Key.IdCouturier,
                    NomCouturier = g.Key.Prenom + " " + g.Key.Nom,
                    NombreRetours = g.Count(),
                    NombreResolus = g.Count(r => r.Statut == "Resolu"),
                    NombreEnCours = g.Count(r => r.Statut != "Resolu")
                })
                .OrderByDescending(s => s.NombreRetours)
                .ToList();
        }
    }
}