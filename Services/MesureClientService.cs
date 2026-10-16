using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Implémentation de <see cref="IMesureClientService"/>.
    ///
    /// Règle fondamentale : ce service ne touche JAMAIS aux enregistrements
    /// <see cref="Mesure"/> liés aux <see cref="PieceCommande"/>.
    /// Il gère uniquement la table <see cref="MesureClient"/> (source de vérité).
    /// </summary>
    public class MesureClientService : IMesureClientService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly ILogger<MesureClientService> _logger;

        public MesureClientService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            ILogger<MesureClientService> logger)
        {
            _contextFactory = contextFactory;
            _logger = logger;
        }

        // ── LECTURE ──────────────────────────────────────────────────────

        public List<FicheMesuresParType> ObtenirFicheMesures(int idClient)
        {
            using var ctx = _contextFactory.CreateDbContext();

            var mesures = ctx.MesuresClient
                .Where(mc => mc.IdClient == idClient)
                .OrderBy(mc => mc.TypeVetement)
                .ThenBy(mc => mc.NomMesure)
                .ToList();

            if (mesures.Count == 0)
                return new List<FicheMesuresParType>();

            return mesures
                .GroupBy(mc => mc.TypeVetement)
                .Select(g => new FicheMesuresParType
                {
                    TypeVetement          = g.Key,
                    DateDerniereMaj       = g.Max(mc => mc.DateMiseAJour),
                    NomOperateurDerniereMaj = g
                        .OrderByDescending(mc => mc.DateMiseAJour)
                        .First()
                        .NomOperateurMiseAJour,
                    Mesures = g.ToList()
                })
                .ToList();
        }

        public List<MesureClient> ObtenirMesuresPourType(int idClient, string typeVetement)
        {
            using var ctx = _contextFactory.CreateDbContext();
            return ctx.MesuresClient
                .Where(mc => mc.IdClient == idClient && mc.TypeVetement == typeVetement)
                .OrderBy(mc => mc.NomMesure)
                .ToList();
        }

        public DateTime? ObtenirDateDerniereMaj(int idClient, string typeVetement)
        {
            using var ctx = _contextFactory.CreateDbContext();
            return ctx.MesuresClient
                .Where(mc => mc.IdClient == idClient && mc.TypeVetement == typeVetement)
                .Select(mc => (DateTime?)mc.DateMiseAJour)
                .Max();
        }

        // ── ÉCRITURE ─────────────────────────────────────────────────────

        public void UpsertMesure(int idClient, string typeVetement, string nomMesure,
            string valeur, int idOperateur, string nomOperateur)
        {
            using var ctx = _contextFactory.CreateDbContext();

            var existante = ctx.MesuresClient.FirstOrDefault(
                mc => mc.IdClient == idClient
                   && mc.TypeVetement == typeVetement
                   && mc.NomMesure == nomMesure);

            if (existante != null)
            {
                existante.Valeur                 = valeur;
                existante.DateMiseAJour          = DateTime.Now;
                existante.IdOperateurMiseAJour   = idOperateur;
                existante.NomOperateurMiseAJour  = nomOperateur;
            }
            else
            {
                ctx.MesuresClient.Add(new MesureClient
                {
                    IdClient               = idClient,
                    TypeVetement           = typeVetement,
                    NomMesure              = nomMesure,
                    Valeur                 = valeur,
                    DateMiseAJour          = DateTime.Now,
                    IdOperateurMiseAJour   = idOperateur,
                    NomOperateurMiseAJour  = nomOperateur
                });
            }

            ctx.SaveChanges();
            _logger.LogInformation(
                "MesureClient upsert : Client#{IdClient} {TypeVetement}/{NomMesure} = {Valeur} par {Op}",
                idClient, typeVetement, nomMesure, valeur, nomOperateur);
        }

        public void UpsertMesures(int idClient, string typeVetement,
            IEnumerable<(string NomMesure, string Valeur)> mesures,
            int idOperateur, string nomOperateur)
        {
            using var ctx = _contextFactory.CreateDbContext();

            // Charger les existantes en une seule requête
            var existantes = ctx.MesuresClient
                .Where(mc => mc.IdClient == idClient && mc.TypeVetement == typeVetement)
                .ToList();

            var now = DateTime.Now;
            int nb = 0;

            foreach (var (nomMesure, valeur) in mesures)
            {
                if (string.IsNullOrWhiteSpace(nomMesure) || string.IsNullOrWhiteSpace(valeur))
                    continue;

                var existante = existantes.FirstOrDefault(
                    mc => mc.NomMesure == nomMesure);

                if (existante != null)
                {
                    existante.Valeur                = valeur;
                    existante.DateMiseAJour         = now;
                    existante.IdOperateurMiseAJour  = idOperateur;
                    existante.NomOperateurMiseAJour = nomOperateur;
                }
                else
                {
                    ctx.MesuresClient.Add(new MesureClient
                    {
                        IdClient               = idClient,
                        TypeVetement           = typeVetement,
                        NomMesure              = nomMesure,
                        Valeur                 = valeur,
                        DateMiseAJour          = now,
                        IdOperateurMiseAJour   = idOperateur,
                        NomOperateurMiseAJour  = nomOperateur
                    });
                }
                nb++;
            }

            ctx.SaveChanges();
            _logger.LogInformation(
                "MesureClient batch upsert : Client#{IdClient} {TypeVetement} — {Nb} mesure(s) par {Op}",
                idClient, typeVetement, nb, nomOperateur);
        }

        public void SupprimerMesuresPourType(int idClient, string typeVetement,
            int idOperateur, string nomOperateur)
        {
            using var ctx = _contextFactory.CreateDbContext();
            var aSupprimer = ctx.MesuresClient
                .Where(mc => mc.IdClient == idClient && mc.TypeVetement == typeVetement)
                .ToList();

            if (aSupprimer.Count == 0) return;

            ctx.MesuresClient.RemoveRange(aSupprimer);
            ctx.SaveChanges();
            _logger.LogInformation(
                "MesureClient suppression : Client#{IdClient} {TypeVetement} ({Nb} mesure(s)) par {Op}",
                idClient, typeVetement, aSupprimer.Count, nomOperateur);
        }
    }
}
