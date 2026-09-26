using GestionCoutureApp.Data;
using GestionCoutureApp.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service d'audit immuable avec chaînage de hash et notifications externes.
    /// AUCUNE méthode Update/Delete n'est fournie : le journal est append-only.
    /// </summary>
    public class AuditService : IAuditService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IWhatsAppService _whatsAppService;
        private readonly IParametresService _parametresService;
        private readonly ILogger<AuditService> _logger;

        // Actions qui déclenchent automatiquement une notification externe
        private static readonly HashSet<string> ActionsCritiques = new()
        {
            "PAIEMENT_ANNULE",
            "COMMANDE_SUPPRIMEE",
            "COMMISSION_ANNULEE",
            "DEPENSE_ANNULEE",
            "PARAMETRE_MODIFIE_SECURITE"
        };

        public AuditService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IWhatsAppService whatsAppService,
            IParametresService parametresService,
            ILogger<AuditService> logger)
        {
            _contextFactory = contextFactory;
            _whatsAppService = whatsAppService;
            _parametresService = parametresService;
            _logger = logger;
        }

        public async Task EnregistrerActionAsync(
            int idOperateur,
            string nomOperateur,
            string roleOperateur,
            string typeAction,
            string entite,
            int idEntite,
            object? valeursAvant = null,
            object? valeursApres = null,
            string? motif = null,
            bool envoyerNotification = false)
        {
            using var context = _contextFactory.CreateDbContext();

            // Récupérer le hash de la dernière entrée pour le chaînage.
            // On lit en ORDER BY IdJournal DESC pour avoir le vrai dernier enregistrement.
            string? hashPrecedent = context.JournalAudit
                .OrderByDescending(j => j.IdJournal)
                .Select(j => j.HashCourant)
                .FirstOrDefault();

            // Construire l'entrée avec tous ses champs métier.
            var entree = new JournalAudit
            {
                DateHeureUtc = DateTime.UtcNow,
                IdOperateur = idOperateur,
                NomOperateur = nomOperateur,
                RoleOperateur = roleOperateur,
                TypeAction = typeAction,
                Entite = entite,
                IdEntite = idEntite,
                ValeursAvant = valeursAvant != null ? AuditJsonHelper.Serialize(valeursAvant) : null,
                ValeursApres = valeursApres != null ? AuditJsonHelper.Serialize(valeursApres) : null,
                Motif = motif,
                HashPrecedent = hashPrecedent,
                AdresseIp = "localhost",
                NotificationEnvoyee = false
            };

            // ✅ Calculer HashCourant AVANT SaveChanges.
            // IdJournal n'est PAS inclus dans CalculerHash() (voir JournalAudit.cs) :
            // on évite ainsi le problème de l'Id valant 0 avant persistance, tout
            // en conservant un chaînage cryptographiquement fiable via HashPrecedent.
            entree.HashCourant = entree.CalculerHash();

            context.JournalAudit.Add(entree);
            context.SaveChanges();   // ← IdJournal reçoit sa valeur définitive ici,
                                     //   mais HashCourant est déjà correct en base.

            _logger.LogInformation(
                "Audit enregistré : {Action} sur {Entite} #{Id} par {Operateur} ({Role})",
                typeAction, entite, idEntite, nomOperateur, roleOperateur);

            // Notification externe pour actions critiques
            if (envoyerNotification || ActionsCritiques.Contains(typeAction))
            {
                await EnvoyerNotificationExterne(entree);
                entree.NotificationEnvoyee = true;
                context.SaveChanges();
            }
        }

        private async Task EnvoyerNotificationExterne(JournalAudit entree)
        {
            try
            {
                // Récupérer le numéro de supervision depuis les paramètres
                var numeroSupervision = await _parametresService.ObtenirValeur("NumeroSupervisionAudit");
                
                if (string.IsNullOrEmpty(numeroSupervision))
                {
                    _logger.LogWarning("Aucun numéro de supervision configuré pour les alertes d'audit");
                    return;
                }

                // Construire le message d'alerte
                string message = $"🔴 ALERTE SÉCURITÉ FINANCIÈRE 🔴\n\n" +
                                $"Action: {entree.ActionAffichee}\n" +
                                $"Entité: {entree.Entite} #{entree.IdEntite}\n" +
                                $"Opérateur: {entree.NomOperateur} ({entree.RoleOperateur})\n" +
                                $"Date: {entree.DateHeureLocale}\n";

                if (!string.IsNullOrEmpty(entree.Motif))
                {
                    message += $"Motif: {entree.Motif}\n";
                }

                message += $"\nJournal d'audit ID: {entree.IdJournal}\n" +
                          $"Hash: {entree.HashCourant[..8]}...";

                // Ouvrir WhatsApp avec le message pré-rempli (semi-automatique)
                // La supervision devra valider manuellement l'envoi
                _whatsAppService.OuvrirConversation(numeroSupervision, message);

                _logger.LogInformation(
                    "Notification d'audit préparée pour le numéro de supervision pour {Action}",
                    entree.TypeAction);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex,
                    "Erreur lors de la préparation de la notification externe pour {Action}",
                    entree.TypeAction);
                // Ne pas faire échouer l'opération d'audit si la notification échoue
            }
        }

        public List<JournalAudit> ObtenirToutesLesEntrees()
        {
            using var context = _contextFactory.CreateDbContext();
            return context.JournalAudit
                .OrderByDescending(j => j.DateHeureUtc)
                .ToList();
        }

        public List<JournalAudit> ObtenirParPeriode(DateTime debut, DateTime fin)
        {
            using var context = _contextFactory.CreateDbContext();
            var debutUtc = debut.ToUniversalTime();
            var finUtc = fin.ToUniversalTime();

            return context.JournalAudit
                .Where(j => j.DateHeureUtc >= debutUtc && j.DateHeureUtc <= finUtc)
                .OrderByDescending(j => j.DateHeureUtc)
                .ToList();
        }

        public List<JournalAudit> ObtenirParOperateur(int idOperateur)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.JournalAudit
                .Where(j => j.IdOperateur == idOperateur)
                .OrderByDescending(j => j.DateHeureUtc)
                .ToList();
        }

        public List<JournalAudit> ObtenirParTypeAction(string typeAction)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.JournalAudit
                .Where(j => j.TypeAction == typeAction)
                .OrderByDescending(j => j.DateHeureUtc)
                .ToList();
        }

        public List<JournalAudit> ObtenirParEntite(string entite, int idEntite)
        {
            using var context = _contextFactory.CreateDbContext();
            return context.JournalAudit
                .Where(j => j.Entite == entite && j.IdEntite == idEntite)
                .OrderByDescending(j => j.DateHeureUtc)
                .ToList();
        }

        public (bool Integre, string Message) VerifierIntegriteChaine()
        {
            using var context = _contextFactory.CreateDbContext();
            var entrees = context.JournalAudit
                .OrderBy(j => j.IdJournal)
                .ToList();

            if (!entrees.Any())
            {
                return (true, "Journal d'audit vide");
            }

            // Vérifier la première entrée
            var premiere = entrees[0];
            if (premiere.HashPrecedent != null)
            {
                return (false, $"Corruption détectée : la première entrée (ID {premiere.IdJournal}) " +
                              "a un hash précédent non null");
            }

            if (!premiere.VerifierIntegrite())
            {
                return (false, $"Corruption détectée : l'entrée ID {premiere.IdJournal} " +
                              "a un hash invalide (contenu modifié)");
            }

            // Vérifier le chaînage de toutes les entrées suivantes
            for (int i = 1; i < entrees.Count; i++)
            {
                var courante = entrees[i];
                var precedente = entrees[i - 1];

                // Vérifier que le hash courant de l'entrée est valide
                if (!courante.VerifierIntegrite())
                {
                    return (false, $"Corruption détectée : l'entrée ID {courante.IdJournal} " +
                                  "a un hash invalide (contenu modifié)");
                }

                // Vérifier le chaînage avec l'entrée précédente
                if (courante.HashPrecedent != precedente.HashCourant)
                {
                    return (false, $"Rupture de chaîne détectée entre les entrées " +
                                  $"ID {precedente.IdJournal} et {courante.IdJournal}. " +
                                  "Une entrée a été supprimée ou modifiée.");
                }
            }

            return (true, $"Intégrité vérifiée : {entrees.Count} entrée(s) validée(s), " +
                         "aucune modification détectée");
        }

        public StatistiquesAudit ObtenirStatistiques(DateTime debut, DateTime fin)
        {
            using var context = _contextFactory.CreateDbContext();
            var debutUtc = debut.ToUniversalTime();
            var finUtc = fin.ToUniversalTime();

            var entrees = context.JournalAudit
                .Where(j => j.DateHeureUtc >= debutUtc && j.DateHeureUtc <= finUtc)
                .ToList();

            var stats = new StatistiquesAudit
            {
                NombreTotalActions = entrees.Count,
                NombreActionsSuppressionAnnulation = entrees.Count(e =>
                    e.TypeAction.Contains("ANNULE") || e.TypeAction.Contains("SUPPRIME")),
                NombreActionsModification = entrees.Count(e => e.TypeAction.Contains("MODIFIE")),
                NombreActionsCreation = entrees.Count(e =>
                    e.TypeAction.Contains("AJOUTE") || e.TypeAction.Contains("CREE")),
                NombreNotificationsEnvoyees = entrees.Count(e => e.NotificationEnvoyee),
                ActionsParOperateur = entrees
                    .GroupBy(e => e.NomOperateur)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ActionsParType = entrees
                    .GroupBy(e => e.TypeAction)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ActionsCritiques = entrees
                    .Where(e => ActionsCritiques.Contains(e.TypeAction))
                    .OrderByDescending(e => e.DateHeureUtc)
                    .ToList()
            };

            return stats;
        }
    }
}
