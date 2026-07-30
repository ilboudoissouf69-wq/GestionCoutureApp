using System.Globalization;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;
using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    public class ParametresService : IParametresService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;

        // Clés stockées en base (table Parametres : Cle / Valeur)
        private const string CleDelaiAlerte = "DelaiAlerteRendezVousHeures";
        private const string CleFrequenceSync = "FrequenceSyncHeures";
        private const string CleSalaireSecretaire = "SalaireMensuelSecretaire";

        public ParametresService(IDbContextFactory<ApplicationDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<string?> ObtenirValeur(string cle)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var param = await context.Parametres.FindAsync(cle);
            return param?.Valeur;
        }

        public async Task DefinirValeur(string cle, string valeur)
        {
            await using var context = await _contextFactory.CreateDbContextAsync();
            var param = await context.Parametres.FindAsync(cle);

            if (param == null)
                context.Parametres.Add(new Parametre { Cle = cle, Valeur = valeur });
            else
                param.Valeur = valeur;

            await context.SaveChangesAsync();
        }

        public async Task<int> ObtenirDelaiAlerteRendezVousHeures()
        {
            var v = await ObtenirValeur(CleDelaiAlerte);
            return int.TryParse(v, out var heures) ? heures : 3; // défaut : 3h avant le rendez-vous
        }

        public async Task DefinirDelaiAlerteRendezVousHeures(int heures)
            => await DefinirValeur(CleDelaiAlerte, heures.ToString(CultureInfo.InvariantCulture));

        public async Task<int> ObtenirFrequenceSyncHeures()
        {
            var v = await ObtenirValeur(CleFrequenceSync);
            return int.TryParse(v, out var heures) ? heures : 4; // défaut : toutes les 4h
        }

        public async Task DefinirFrequenceSyncHeures(int heures)
            => await DefinirValeur(CleFrequenceSync, heures.ToString(CultureInfo.InvariantCulture));

        public async Task<decimal> ObtenirSalaireMensuelSecretaire()
        {
            var v = await ObtenirValeur(CleSalaireSecretaire);
            return decimal.TryParse(v, NumberStyles.Any, CultureInfo.InvariantCulture, out var montant)
                ? montant : 0m;
        }

        public async Task DefinirSalaireMensuelSecretaire(decimal montant)
            => await DefinirValeur(CleSalaireSecretaire, montant.ToString(CultureInfo.InvariantCulture));
    }
}