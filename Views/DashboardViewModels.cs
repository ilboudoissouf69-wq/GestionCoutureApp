using System.Windows;

namespace GestionCoutureApp.Views
{
    /// <summary>
    /// Modèle d'affichage pour UNE carte « Prochaines Échéances de Livraison »
    /// du Dashboard. Regroupe les pièces d'une même commande (détail
    /// « Chemise (En cours) • Pantalon (À faire) », comme AlertesView.Grouper),
    /// construit exclusivement à partir des AlerteRendezVous d'IAlerteService —
    /// jamais utilisé dans les services.
    /// </summary>
    public class EcheanceDashboardVm
    {
        /// <summary>Id de la commande parente.</summary>
        public int IdCommande { get; set; }

        /// <summary>Nom affiché du client (Prénom Nom).</summary>
        public string NomClient { get; set; } = string.Empty;

        /// <summary>Numéro de téléphone du client (peut être vide).</summary>
        public string Telephone { get; set; } = string.Empty;

        /// <summary>Rendez-vous de la pièce la plus urgente du groupe.</summary>
        public DateTime DateEcheance { get; set; }

        /// <summary>
        /// Détail lisible des pièces du groupe, ex. :
        /// « Chemise (En cours) • Pantalon (À faire) ».
        /// </summary>
        public string DetailPieces { get; set; } = string.Empty;

        /// <summary>
        /// True si au moins une pièce est en NiveauAlerte « retard » ET qu'aucune
        /// pièce du groupe n'est prête (une pièce prête retire le caractère « retard »
        /// de la carte : le client peut venir récupérer).
        /// </summary>
        public bool EstEnRetard { get; set; }

        /// <summary>True si au moins une pièce a son rendez-vous aujourd'hui.</summary>
        public bool EstAujourdhui { get; set; }

        /// <summary>True si au moins une pièce est terminée (active le bouton 💬 Prêt).</summary>
        public bool APiecePrete { get; set; }

        /// <summary>Visibilité du bouton WhatsApp « Prêt » (visible si une pièce est prête).</summary>
        public Visibility VisibilitePretWhatsApp =>
            APiecePrete ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>
    /// Modèle d'affichage pour UNE carte « Charge des Couturiers » du Dashboard :
    /// jauge à 3 segments (prêtes / en cours / reste) en largeurs étoiles
    /// proportionnelles — aucune division, donc 0 pièce donne une barre grise.
    /// NbRetards (commandes distinctes) et CaTotal (somme des MontantCouture)
    /// reprennent exactement les calculs de l'ancien tableau
    /// « Performance Couturiers ».
    /// </summary>
    public class ChargeCouturierVm
    {
        /// <summary>Nom complet du couturier (Prénom Nom).</summary>
        public string NomComplet { get; set; } = string.Empty;

        /// <summary>Pièces actives : Statut != « Livree » et commande existante.</summary>
        public int TotalPieces { get; set; }

        public int PiecesAFaire { get; set; }
        public int PiecesEnCours { get; set; }

        /// <summary>Pièces actives au statut « Terminee » (prêtes).</summary>
        public int PiecesTerminees { get; set; }

        /// <summary>Commandes distinctes en retard (même calcul que l'ancien écran).</summary>
        public int NbRetards { get; set; }

        /// <summary>Somme des MontantCouture de toutes les pièces du couturier.</summary>
        public decimal CaTotal { get; set; }

        // ── Largeurs de la jauge 3 segments (GridLength étoiles) ──────────
        public GridLength LargeurTerminees { get; set; } = new(0, GridUnitType.Star);
        public GridLength LargeurEnCours { get; set; } = new(0, GridUnitType.Star);

        /// <summary>Par défaut pleine largeur : 0 pièce → barre entièrement grise.</summary>
        public GridLength LargeurReste { get; set; } = new(1, GridUnitType.Star);

        /// <summary>Répartition lisible sous la jauge.</summary>
        public string RepartitionAffiche =>
            $"✓ {PiecesTerminees} prête(s) · ⏳ {PiecesEnCours} en cours · 📋 {PiecesAFaire} à faire";

        /// <summary>Ligne discrète retards/CA conservée de l'ancien écran.</summary>
        public string LigneRetardsCa =>
            $"⚠ Retards : {NbRetards} · CA : {CaTotal:N0} FCFA";
    }
}
