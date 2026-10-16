namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Type de domaine métier qui a changé.
    /// Un seul événement suffit pour déclencher le rafraîchissement du dashboard.
    /// </summary>
    public enum DomainChangedType
    {
        /// <summary>Une commande ou pièce a été créée/modifiée/annulée/supprimée.</summary>
        Commande,
        /// <summary>Un paiement a été ajouté ou annulé.</summary>
        Paiement,
        /// <summary>Une commission a été enregistrée ou annulée.</summary>
        Commission,
        /// <summary>Une dépense a été ajoutée, validée ou annulée.</summary>
        Depense,
        /// <summary>Un retour a changé d'état.</summary>
        Retour,
        /// <summary>Un matériel/supplément a été ajouté, modifié ou supprimé.</summary>
        Materiel
    }

    /// <summary>
    /// Événement léger publié via <see cref="IEventAggregator"/> chaque fois qu'un service
    /// métier modifie ses données avec succès (après SaveChanges). Les dashboards s'y abonnent
    /// pour se rafraîchir automatiquement.
    /// </summary>
    public class DomainChangedEvent
    {
        public DomainChangedType Type { get; set; }

        /// <summary>Contexte facultatif (ex. id de l'entité modifiée).</summary>
        public object? Data { get; set; }
    }
}
