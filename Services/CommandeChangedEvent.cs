namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Événement déclenché lorsqu'une commande est modifiée.
    /// Permet aux vues ouvertes (PaiementsView, CommandesView) de se rafraîchir automatiquement.
    /// </summary>
    public class CommandeChangedEventArgs : EventArgs
    {
        public int IdCommande { get; set; }
        
        /// <summary>
        /// Type de changement : "PieceAjoutee", "PieceModifiee", "PieceSupprimee", 
        /// "MontantModifie", "StatutModifie"
        /// </summary>
        public string TypeChangement { get; set; } = string.Empty;

        /// <summary>
        /// Contexte additionnel (ex: ID de la pièce concernée)
        /// </summary>
        public string? Details { get; set; }
    }
}
