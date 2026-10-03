namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Levée quand on tente de passer une pièce en "Livree" alors que la
    /// commande n'est pas entièrement payée.
    /// <para>
    /// <see cref="PeutForcer"/> = true : l'opérateur est Boss, il peut confirmer
    /// en fournissant un motif (rappeler la méthode avec ce motif).
    /// </para>
    /// </summary>
    public class LivraisonNonSoldeeException : InvalidOperationException
    {
        public decimal ResteAPayer { get; }
        public bool PeutForcer { get; }

        public LivraisonNonSoldeeException(decimal resteAPayer, bool peutForcer)
            : base(peutForcer
                ? $"Le client doit encore {resteAPayer:N0} FCFA sur cette commande.\n\n" +
                  "En tant que Boss, vous pouvez autoriser la livraison avec un motif obligatoire."
                : $"Livraison impossible : le client doit encore {resteAPayer:N0} FCFA sur cette commande.\n\n" +
                  "Encaissez le solde dans Paiements, ou demandez au Boss d'autoriser la livraison.")
        {
            ResteAPayer = resteAPayer;
            PeutForcer = peutForcer;
        }
    }
}
