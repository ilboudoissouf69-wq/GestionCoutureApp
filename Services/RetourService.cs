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

        private IQueryable<Retour> AvecIncludes(ApplicationDbContext context) =>
            context.Retours
                .Include(r => r.Commande).ThenInclude(c => c!.Client)
                .Include(r => r.PieceCommande)
                .Include(r => r.Couturier)
                .Include(r => r.CouturierReprise);

        public List<Retour> ObtenirTous()
        {
            using var context = _contextFactory.CreateDbContext();
            try
            {
                return AvecIncludes(context)
                    .OrderByDescending(r => r.DateSignalement)
                    .ToList();
            }
            catch
            {
                // Fallback si CouturierReprise pas encore en base
                return context.Retours
                    .Include(r => r.Commande).ThenInclude(c => c!.Client)
                    .Include(r => r.PieceCommande)
                    .Include(r => r.Couturier)
                    .OrderByDescending(r => r.DateSignalement)
                    .ToList();
            }
        }

        public Retour? ObtenirParId(int id)
        {
            using var context = _contextFactory.CreateDbContext();
            return AvecIncludes(context).FirstOrDefault(r => r.IdRetour == id);
        }

        public void Ajouter(Retour retour)
        {
            using var context = _contextFactory.CreateDbContext();

            var piece = context.PiecesCommande.Find(retour.IdPieceCommande)
                ?? throw new InvalidOperationException("Pièce introuvable.");

            // Un retour peut concerner une pièce Livrée OU Terminée
            // (le boss peut accepter un retour avant livraison officielle)
            if (piece.Statut != "Livree" && piece.Statut != "Terminee")
                throw new InvalidOperationException(
                    "Impossible d'enregistrer un retour : cette pièce n'est pas encore " +
                    "terminée (statut : " + piece.StatutAffiche + ").");

            retour.DateSignalement = DateTime.Now;
            context.Retours.Add(retour);
            context.SaveChanges();
        }

        public void Modifier(Retour retour)
        {
            using var context = _contextFactory.CreateDbContext();
            var existant = context.Retours.Find(retour.IdRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (existant.EstAnnule)
                throw new InvalidOperationException("Un retour annulé ne peut pas être modifié.");

            existant.DescriptionProbleme = retour.DescriptionProbleme;
            existant.IdCouturierReprise = retour.IdCouturierReprise;
            existant.DateRdvReprise = retour.DateRdvReprise;
            existant.HeureDebutReprise = retour.HeureDebutReprise;
            existant.HeureFinReprise = retour.HeureFinReprise;
            if (!string.IsNullOrEmpty(retour.CheminPhotoDefaut))
                existant.CheminPhotoDefaut = retour.CheminPhotoDefaut;

            context.SaveChanges();
        }

        public void DemarrerReprise(int idRetour, int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            var retour = context.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.Statut != "Signale")
                throw new InvalidOperationException(
                    "Seul un retour 'Signalé' peut passer en reprise.");

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
                    "Seul un retour 'En reprise' peut être marqué Prêt.");

            retour.Statut = "Pret";
            retour.DateResolution = DateTime.Now;
            retour.IdOperateurResolution = idOperateur;
            retour.NomOperateurResolution = nomOperateur;
            context.SaveChanges();
        }

        public void MarquerRendu(int idRetour, int idOperateur, string nomOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            var retour = context.Retours.Find(idRetour)
                ?? throw new InvalidOperationException("Retour introuvable.");

            if (retour.Statut != "Pret")
                throw new InvalidOperationException(
                    "Seul un retour 'Prêt' peut être marqué 'Rendu au client'.");

            retour.Statut = "Rendu";
            context.SaveChanges();
        }

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

        public List<Retour> Rechercher(string motCle)
        {
            using var context = _contextFactory.CreateDbContext();
            string m = motCle.ToLower();
            return AvecIncludes(context)
                .Where(r =>
                    (r.Commande != null && r.Commande.Client != null &&
                     (r.Commande.Client.Nom.ToLower().Contains(m) ||
                      r.Commande.Client.Prenom.ToLower().Contains(m))) ||
                    r.DescriptionProbleme.ToLower().Contains(m) ||
                    r.Statut.ToLower().Contains(m) ||
                    (r.Couturier != null &&
                     (r.Couturier.Nom.ToLower().Contains(m) ||
                      r.Couturier.Prenom.ToLower().Contains(m))) ||
                    (r.PieceCommande != null &&
                     r.PieceCommande.TypeVetement.ToLower().Contains(m)))
                .OrderByDescending(r => r.DateSignalement)
                .ToList();
        }

        public List<Retour> FiltrerParStatut(string statut)
        {
            using var context = _contextFactory.CreateDbContext();
            var query = AvecIncludes(context);
            if (statut != "Tous")
                query = query.Where(r => r.Statut == statut && !r.EstAnnule);
            return query.OrderByDescending(r => r.DateSignalement).ToList();
        }

        public List<StatistiqueRetourCouturier> StatistiquesParCouturier(
            DateTime dateDebut, DateTime dateFin)
        {
            using var context = _contextFactory.CreateDbContext();
            var retours = context.Retours
                .Include(r => r.Couturier)
                .Where(r => !r.EstAnnule &&
                            r.DateSignalement.Date >= dateDebut.Date &&
                            r.DateSignalement.Date <= dateFin.Date)
                .ToList();

            return retours
                .GroupBy(r => new { r.IdCouturier, Prenom = r.Couturier?.Prenom ?? "", Nom = r.Couturier?.Nom ?? "" })
                .Select(g => new StatistiqueRetourCouturier
                {
                    IdCouturier = g.Key.IdCouturier,
                    NomCouturier = (g.Key.Prenom + " " + g.Key.Nom).Trim(),
                    NombreRetours = g.Count(),
                    NombreResolus = g.Count(r => r.Statut == "Pret" || r.Statut == "Rendu"),
                    NombreEnCours = g.Count(r => r.Statut == "Signale" || r.Statut == "En reprise")
                })
                .OrderByDescending(s => s.NombreRetours)
                .ToList();
        }
    }
}
