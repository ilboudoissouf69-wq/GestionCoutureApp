namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Levée par <see cref="ClientService.Ajouter"/> quand un client avec le même
    /// nom+prénom normalisés ET le même numéro de téléphone existe déjà en base.
    ///
    /// L'UI intercepte cette exception séparément d'<see cref="InvalidOperationException"/>
    /// pour proposer à l'opérateur de sélectionner la fiche existante plutôt que de
    /// créer un doublon silencieux.
    /// </summary>
    public class DuplicatClientException : InvalidOperationException
    {
        /// <summary>Fiche existante qui correspond au doublon détecté.</summary>
        public Models.Client ClientExistant { get; }

        public DuplicatClientException(Models.Client clientExistant)
            : base(
                $"Un client similaire existe déjà : {clientExistant.Prenom} {clientExistant.Nom}" +
                (string.IsNullOrWhiteSpace(clientExistant.Telephone)
                    ? ""
                    : $" — {clientExistant.Telephone}") +
                $" (Id #{clientExistant.IdClient}).")
        {
            ClientExistant = clientExistant;
        }
    }
}
