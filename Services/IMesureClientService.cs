using GestionCoutureApp.Models;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service pour la fiche de mesures réutilisables d'un client.
    ///
    /// CONTRAT :
    ///   - Les mesures retournées sont la SOURCE DE VÉRITÉ (version à jour).
    ///   - La modification d'une mesure ici ne rétro-modifie JAMAIS les pièces
    ///     existantes — celles-ci conservent leur copie figée.
    ///   - Toutes les écritures sont auditées (IdOperateur, NomOperateur, DateMiseAJour).
    /// </summary>
    public interface IMesureClientService
    {
        // ── LECTURE ──────────────────────────────────────────────────────────

        /// <summary>
        /// Retourne toutes les mesures pour un client, groupées par type de vêtement.
        /// Retourne une liste vide si le client n'a aucune mesure.
        /// </summary>
        List<FicheMesuresParType> ObtenirFicheMesures(int idClient);

        /// <summary>
        /// Retourne les mesures d'un client pour un type de vêtement précis,
        /// ou une liste vide si aucune n'existe encore.
        /// </summary>
        List<MesureClient> ObtenirMesuresPourType(int idClient, string typeVetement);

        /// <summary>
        /// Retourne la date de la dernière mise à jour pour un (client, type),
        /// ou null si aucune mesure n'existe encore.
        /// </summary>
        DateTime? ObtenirDateDerniereMaj(int idClient, string typeVetement);

        // ── ÉCRITURE ─────────────────────────────────────────────────────────

        /// <summary>
        /// Met à jour (ou crée) une mesure pour un client.
        /// Si la mesure (IdClient, TypeVetement, NomMesure) existe : met à jour Valeur,
        /// DateMiseAJour, IdOperateurMiseAJour, NomOperateurMiseAJour.
        /// Sinon : crée une nouvelle entrée.
        /// </summary>
        void UpsertMesure(int idClient, string typeVetement, string nomMesure, string valeur,
            int idOperateur, string nomOperateur);

        /// <summary>
        /// Met à jour en lot toutes les mesures d'un type de vêtement pour un client,
        /// dans une seule transaction. Idéal pour la validation d'une commande ou
        /// la sauvegarde depuis la fiche client.
        /// </summary>
        void UpsertMesures(int idClient, string typeVetement,
            IEnumerable<(string NomMesure, string Valeur)> mesures,
            int idOperateur, string nomOperateur);

        /// <summary>
        /// Supprime toutes les mesures d'un type de vêtement pour un client.
        /// </summary>
        void SupprimerMesuresPourType(int idClient, string typeVetement,
            int idOperateur, string nomOperateur);
    }

    // ── DTO de présentation ───────────────────────────────────────────────────

    /// <summary>
    /// Groupe de mesures pour un type de vêtement donné.
    /// Utilisé dans la fiche client (onglet Mesures).
    /// </summary>
    public class FicheMesuresParType
    {
        /// <summary>Ex. : « Pantalon »</summary>
        public string TypeVetement { get; set; } = string.Empty;

        /// <summary>Date de la dernière mise à jour dans ce groupe.</summary>
        public DateTime? DateDerniereMaj { get; set; }

        /// <summary>Nom de l'opérateur ayant fait la dernière MAJ.</summary>
        public string NomOperateurDerniereMaj { get; set; } = string.Empty;

        /// <summary>Label formaté pour l'UI : « Mesures du 15/10/2026 »</summary>
        public string LabelDate =>
            DateDerniereMaj.HasValue
                ? $"Mesures du {DateDerniereMaj.Value:dd/MM/yyyy}"
                : "Aucune mesure enregistrée";

        public List<MesureClient> Mesures { get; set; } = new();
    }
}
