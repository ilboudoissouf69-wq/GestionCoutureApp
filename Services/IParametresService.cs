namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Accès typé aux réglages de l'application (Point 8).
    /// Chaque réglage a un accesseur dédié + une valeur par défaut
    /// raisonnable si la clé n'existe pas encore en base.
    /// </summary>
    public interface IParametresService
    {
        Task<int> ObtenirDelaiAlerteRendezVousHeures();
        Task DefinirDelaiAlerteRendezVousHeures(int heures);

        // 0 = "fin de journée uniquement" (Point 7)
        Task<int> ObtenirFrequenceSyncHeures();
        Task DefinirFrequenceSyncHeures(int heures);

        Task<decimal> ObtenirSalaireMensuelSecretaire();
        Task DefinirSalaireMensuelSecretaire(decimal montant);

        // Accès générique, pour les futurs réglages (cf. décision 8.1)
        Task<string?> ObtenirValeur(string cle);
        Task DefinirValeur(string cle, string valeur);
    }
}