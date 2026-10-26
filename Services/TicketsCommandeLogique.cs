using System.Globalization;
using GestionCoutureApp.Models;

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

        /// <summary>Formate les montants de façon stable, quelle que soit la culture de la machine.</summary>
        public static string FormaterMontant(decimal montant)
            => montant.ToString("N0", CultureInfo.GetCultureInfo("fr-FR"));

        /// <summary>Décrit les étiquettes à produire. Une valeur null signifie une étiquette regroupée.</summary>
        public static IReadOnlyList<string?> DecouperEtiquettes(int nombrePieces, bool uneParPiece)
        {
            int nombre = Math.Max(0, nombrePieces);
            if (nombre <= 1 || !uneParPiece)
                return new string?[] { null };

            return Enumerable.Range(1, nombre)
                .Select(i => $"PIÈCE {i}/{nombre}")
                .Cast<string?>()
                .ToArray();
        }

        /// <summary>Retourne le texte du solde et indique si la commande est soldée.</summary>
        public static (string Texte, bool EstSolde) LibelleSolde(decimal resteAPayer)
            => resteAPayer > 0m
                ? ($"SOLDE À ENCAISSER : {FormaterMontant(resteAPayer)} FCFA", false)
                : ("SOLDÉ ✓", true);

        /// <summary>Construit un résumé compact du nombre et des types de pièces.</summary>
        public static string ResumePieces(IEnumerable<PieceCommande>? pieces)
        {
            var liste = pieces?.ToList() ?? new List<PieceCommande>();
            string compte = liste.Count == 1 ? "1 pièce" : $"{liste.Count} pièces";
            var types = liste
                .GroupBy(p => p.TypeVetement?.Trim() ?? string.Empty, StringComparer.OrdinalIgnoreCase)
                .Select(g => (Type: g.Key, Nombre: g.Count()))
                .Where(g => !string.IsNullOrWhiteSpace(g.Type))
                .ToList();
            if (types.Count <= 1)
                return compte;

            string detail = string.Join(", ", types.Select(g => $"{g.Nombre} {g.Type}"));
            return $"{compte} : {detail}";
        }

        /// <summary>Détermine les éléments client visibles selon le rôle existant.</summary>
        public static VisibiliteTickets VisibiliteSelonRole(string? role)
        {
            bool couturier = string.Equals(role, RoleEmploye.Couturier.ToDbString(), StringComparison.OrdinalIgnoreCase);
            return new VisibiliteTickets(!couturier, !couturier, !couturier);
        }

        /// <summary>Libellé court affiché sur le bouton Imprimer.</summary>
        public static string LibelleMode(ModeImpression mode) => mode switch
        {
            ModeImpression.EtiquetteSeule     => "Étiquette seule",
            ModeImpression.BonClientSeul      => "Bon client seul",
            ModeImpression.EtiquetteEtBonClient => "Étiquette + Bon client",
            _ => "?"
        };
    }

    public readonly record struct VisibiliteTickets(
        bool OngletBonClientVisible,
        bool ModeBonClientSeulVisible,
        bool ModeEtiquetteEtBonVisible);

    /// <summary>Les trois modes d'impression disponibles pour une commande.</summary>
    public enum ModeImpression
    {
        EtiquetteSeule,
        BonClientSeul,
        EtiquetteEtBonClient
    }
}
