namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Logique pure (sans UI, sans DI) pour la fenêtre FenetreTicketsCommande.
    /// Séparée en classe statique pour pouvoir être testée en isolation complète.
    /// </summary>
    public static class TicketsCommandeLogique
    {
        /// <summary>
        /// Mode d'impression proposé par défaut selon le montant déjà encaissé.
        /// Règle métier (décision 5) :
        ///   – Aucun acompte → EtiquetteEtBonClient (le client repart avec un papier).
        ///   – Acompte versé → EtiquetteSeule (le client a déjà son FenetreRecu).
        /// L'opérateur peut toujours changer le mode avant d'imprimer.
        /// </summary>
        public static ModeImpression ModeParDefaut(decimal montantEncaisse)
            => montantEncaisse <= 0m ? ModeImpression.EtiquetteEtBonClient
                                     : ModeImpression.EtiquetteSeule;

        /// <summary>Libellé court affiché sur le bouton Imprimer.</summary>
        public static string LibelleMode(ModeImpression mode) => mode switch
        {
            ModeImpression.EtiquetteSeule     => "Étiquette seule",
            ModeImpression.BonClientSeul      => "Bon client seul",
            ModeImpression.EtiquetteEtBonClient => "Étiquette + Bon client",
            _ => "?"
        };
    }

    /// <summary>Les trois modes d'impression disponibles pour une commande.</summary>
    public enum ModeImpression
    {
        EtiquetteSeule,
        BonClientSeul,
        EtiquetteEtBonClient
    }
}
