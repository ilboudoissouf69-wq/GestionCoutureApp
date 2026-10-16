namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Modèle d'affichage pour UNE CARTE dans AlertesView.
    /// Regroupe toutes les pièces d'une même commande qui tombent dans
    /// la même section (Prêtes / Retard / Aujourd'hui / À venir).
    /// Étape 6 : peut aussi représenter un retour (EstAlertRetour = true).
    ///
    /// Construit dans AlertesView.cs à partir des AlerteRendezVous
    /// fournis par IAlerteService — jamais utilisé dans les services.
    /// </summary>
    public class AlerteCommandeVm
    {
        /// <summary>Id de la commande parente (0 si c'est un retour pur).</summary>
        public int IdCommande { get; set; }

        /// <summary>Nom affiché du client (Prénom Nom).</summary>
        public string NomClient { get; set; } = string.Empty;

        /// <summary>Numéro de téléphone du client (peut être vide).</summary>
        public string Telephone { get; set; } = string.Empty;

        /// <summary>
        /// Date de la pièce la plus urgente du groupe
        /// (exceptions RendezVousException déjà appliquées dans DateRendezVous).
        /// </summary>
        public DateTime DateEcheance { get; set; }

        /// <summary>Heure RDV formatée "HH:mm" de la pièce la plus urgente.</summary>
        public string HeureRdv { get; set; } = string.Empty;

        /// <summary>
        /// Détail lisible des pièces du groupe, ex. :
        /// "Chemise (En cours) • Pantalon (À faire)".
        /// </summary>
        public string DetailPieces { get; set; } = string.Empty;

        /// <summary>
        /// Libellé contextuel du bouton WhatsApp selon la section
        /// (ex. "💬 Prévenir : commande prête", "💬 Rappel RDV", …).
        /// Vide = pas de bouton WhatsApp dans cette section.
        /// </summary>
        public string LibelleBoutonWhatsApp { get; set; } = string.Empty;

        /// <summary>True quand au moins une pièce est en TypeAlerte "PasEncorePriseEnCharge".</summary>
        public bool EstStagnant { get; set; }

        /// <summary>Ids des PieceCommande regroupées dans cette carte.</summary>
        public List<int> IdsPiecesCommande { get; set; } = new();

        // ── Étape 6 : champs retour ───────────────────────────────────────
        /// <summary>True si cette carte représente un retour (pas une commande).</summary>
        public bool EstAlertRetour { get; set; } = false;

        /// <summary>IdRetour si EstAlertRetour, 0 sinon.</summary>
        public int IdRetour { get; set; } = 0;

        /// <summary>Numéro lisible RET-AAAA-NNNNN, vide pour les commandes.</summary>
        public string NumeroRetour { get; set; } = string.Empty;

        // ── Propriétés calculées utilisées par le XAML ───────────────────

        /// <summary>True si le bouton WhatsApp doit être visible.</summary>
        public bool AfficherWhatsApp => !string.IsNullOrEmpty(LibelleBoutonWhatsApp) && !EstAlertRetour;

        /// <summary>Date d'échéance formatée pour l'affichage (dd/MM/yy HH:mm).</summary>
        public string DateEcheanceAffichee => DateEcheance.ToString("dd/MM/yy HH:mm");

        /// <summary>Date courte pour l'affichage compact (dd/MM/yyyy).</summary>
        public string DateCourte => DateEcheance.ToString("dd/MM/yyyy");

        /// <summary>
        /// Libellé de la carte : « RETOUR RET-2026-00001 » pour les retours,
        /// « CMD-42 » pour les commandes.
        /// </summary>
        public string LibelleType => EstAlertRetour
            ? $"🔄 RETOUR  {NumeroRetour}"
            : $"CMD-{IdCommande}";

        /// <summary>Couleur d'accent de la carte (ambre pour retours, bleu-ardoise pour commandes).</summary>
        public string CouleurAccent => EstAlertRetour ? "#D97706" : "#1D4ED8";
    }
}
