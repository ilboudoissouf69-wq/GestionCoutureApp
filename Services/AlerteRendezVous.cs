namespace GestionCoutureApp.Services
{
    /// <summary>
    /// DTO pour l'affichage des alertes (Point 5 + Étape 6 Retours).
    /// Les alertes sont calculées à la volée à partir des pièces et retours,
    /// aucune table dédiée n'est stockée en base de données.
    /// </summary>
    public class AlerteRendezVous
    {
        // CORRECTIF (audit) : une alerte est maintenant rattachée à une PIÈCE,
        // pas seulement à une commande — nécessaire pour honorer
        // PieceCommande.RendezVousException (Point 5, cas d'exception) et pour
        // ne pas mélanger plusieurs pièces d'une même commande sous un seul
        // couturier/statut, comme le faisait l'ancienne version
        // (elle ne regardait que c.Pieces.FirstOrDefault()).
        public int IdCommande { get; set; }
        public int IdPieceCommande { get; set; }

        // ── Étape 6 : champs spécifiques aux RETOURS ─────────────────────
        /// <summary>0 pour les alertes commande, IdRetour pour les alertes retour.</summary>
        public int IdRetour { get; set; } = 0;
        /// <summary>NumeroRetour formaté (RET-AAAA-NNNNN) ou vide pour les commandes.</summary>
        public string NumeroRetour { get; set; } = string.Empty;
        /// <summary>True si cette alerte provient d'un retour (pas d'une pièce de commande).</summary>
        public bool EstAlertRetour { get; set; } = false;

        public string NomClient { get; set; } = string.Empty;
        public string Telephone { get; set; } = string.Empty;
        public string TypeVetement { get; set; } = string.Empty;
        public DateTime DateRendezVous { get; set; }
        public string HeureRendezVous { get; set; } = string.Empty;
        public string Statut { get; set; } = string.Empty;
        public string TempsRestant { get; set; } = string.Empty;
        public string NomCouturier { get; set; } = string.Empty;
        public bool EstUrgent { get; set; }
        public bool ProposerContactWhatsApp { get; set; }

        // CORRECTIF (audit) : NOUVEAU — distingue les deux types d'alerte du
        // Point 5. "PasEncorePriseEnCharge" et "RendezVousProche" n'ont pas la
        // même urgence ni le même message pour la secrétaire, elles ne
        // doivent pas être confondues dans une seule liste indifférenciée.
        // Étape 6 : ajout de "Retour" comme troisième type.
        public string TypeAlerte { get; set; } = string.Empty;
        // "PasEncorePriseEnCharge" | "RendezVousProche" | "Retard" | "Retour"

        // Libellé lisible pour la colonne "Alerte"
        public string MotifAlerte => TypeAlerte switch
        {
            "PasEncorePriseEnCharge" => "⚠️  Colis stagnant — mi-délai dépassé, statut 'À faire'",
            "RendezVousProche"       => "⏰  RDV proche — vêtement pas encore terminé",
            "Retard"                 => "🔴  RDV dépassé — vêtement pas encore terminé",
            "Retour"                 => "🔄  Retour en cours — RDV reprise",
            _                       => TypeAlerte
        };

        // True si la pièce est terminée (même si le RDV est futur) — active le bouton orange
        public bool PiecePrete => Statut is "Terminée" or "Terminee";

        // ── TÂCHE 7 : Niveau d'urgence ───────────────────────────────────

        /// <summary>
        /// Niveau d'urgence calculé à partir du RDV et du statut.
        /// "retard"    : DateRendezVous dépassée et pièce/retour non terminé(e)
        /// "jourbj"    : RDV aujourd'hui (même jour)
        /// "demain"    : RDV demain
        /// "normal"    : autres cas
        /// </summary>
        public string NiveauAlerte
        {
            get
            {
                var maintenant = DateTime.Now;

                // Pour un retour : considéré "fini" si Terminé ou Rendu
                if (EstAlertRetour)
                {
                    bool fini = Statut is "Termine" or "Terminé" or "Rendu";
                    if (fini) return "normal";
                    if (DateRendezVous.Date < maintenant.Date) return "retard";
                    if (DateRendezVous.Date == maintenant.Date) return "jourbj";
                    if (DateRendezVous.Date == maintenant.Date.AddDays(1)) return "demain";
                    return "normal";
                }

                bool pieceFinie = Statut is "Terminee" or "Terminée" or "Livree" or "Livrée";
                if (pieceFinie) return "normal";
                if (DateRendezVous.Date < maintenant.Date) return "retard";
                if (DateRendezVous.Date == maintenant.Date) return "jourbj";
                if (DateRendezVous.Date == maintenant.Date.AddDays(1)) return "demain";
                return "normal";
            }
        }

        /// <summary>
        /// Vrai si le RDV est dans moins d'une heure ou dépassé, et pièce/retour non terminé(e).
        /// Déclenche la notification sonore/popup.
        /// </summary>
        public bool NecessiteNotificationUrgente =>
            NiveauAlerte is "retard" or "jourbj"
            && (DateRendezVous - DateTime.Now).TotalHours <= 1.0;
    }

    public interface IAlerteService
    {
        Task<List<AlerteRendezVous>> ObtenirAlertesActuelles();
        Task<List<AlerteRendezVous>> ObtenirTousRendezVousAVenir();
        /// <summary>RDV de la semaine en cours (pour la section Retrait).</summary>
        Task<List<AlerteRendezVous>> ObtenirRendezVousSemaine();
        /// <summary>
        /// Pièces non terminées/livrées dont le RDV est déjà dépassé
        /// (commandes non supprimées uniquement).
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirRetards();

        /// <summary>
        /// Étape 6 : Retours actifs avec DateRdvReprise renseignée
        /// (Statuts : Signale, En reprise, Termine — pas encore Rendu ni Annulé).
        /// Même niveaux J-1 / Jour J / retard.
        /// </summary>
        Task<List<AlerteRendezVous>> ObtenirAlertesRetours();
    }
}
