namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Événement déclenché lorsqu'une commande est modifiée.
    /// Permet aux vues ouvertes (PaiementsView, CommandesView) de se rafraîchir automatiquement.
    /// 
    /// CORRECTIF AUDIT #1 : Résout le bug de sur-paiement lorsqu'une pièce est ajoutée
    /// après qu'un acompte a été versé (PaiementsView affichait l'ancien montant sans refresh).
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
