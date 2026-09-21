using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using GestionCoutureApp.Data;

namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Service de gestion des reçus.
    /// Centralise les informations de l'atelier et notifie les changements dynamiquement.
    /// </summary>
    public class ReceiptService : IReceiptService
    {
        private readonly IDbContextFactory<ApplicationDbContext> _contextFactory;
        private readonly IParametresService _parametresService;
        private readonly IEventAggregator _eventAggregator;
        private readonly ConcurrentBag<Action<ReceiptInfo>> _receiptChangeHandlers = new();

        public ReceiptService(
            IDbContextFactory<ApplicationDbContext> contextFactory,
            IParametresService parametresService,
            IEventAggregator eventAggregator)
        {
            _contextFactory = contextFactory;
            _parametresService = parametresService;
            _eventAggregator = eventAggregator;
        }

        public async Task<ReceiptInfo> GetReceiptInfoAsync()
        {
            return new ReceiptInfo
            {
                NomAtelier = await _parametresService.ObtenirNomAtelier(),
                Telephone = await _parametresService.ObtenirTelAtelier(),
                Adresse = await _parametresService.ObtenirAdresseAtelier(),
                PiedRecu = await _parametresService.ObtenirPiedRecu()
            };
        }

        public async Task UpdateReceiptInfoAsync(ReceiptInfo info)
        {
            // Sauvegarder dans la base de données
            await _parametresService.DefinirNomAtelier(info.NomAtelier);
            await _parametresService.DefinirTelAtelier(info.Telephone);
            await _parametresService.DefinirAdresseAtelier(info.Adresse);
            await _parametresService.DefinirPiedRecu(info.PiedRecu);

            // Notifier tous les abonnés directs
            foreach (var handler in _receiptChangeHandlers)
            {
                try
                {
                    handler(info);
                }
                catch
                {
                    // Ignorer les erreurs de handlers
                }
            }

            // Publier l'événement global
            _eventAggregator.Publish(new SettingsChangedEvent
            {
                Type = SettingsChangedType.ReceiptInfo,
                Data = info
            });
        }

        public void SubscribeToReceiptChanges(Action<ReceiptInfo> handler)
        {
            _receiptChangeHandlers.Add(handler);
        }

        public void UnsubscribeFromReceiptChanges(Action<ReceiptInfo> handler)
        {
            var tempHandlers = _receiptChangeHandlers.ToList();
            tempHandlers.Remove(handler);
            
            // Nettoyer si vide
            if (!tempHandlers.Any())
            {
                _receiptChangeHandlers.Clear();
            }
        }
    }
}
