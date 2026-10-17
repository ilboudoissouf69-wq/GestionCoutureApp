using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// Mini-chrono diagnostic : wraps un Stopwatch et loggue en niveau Debug via
    /// Microsoft.Extensions.Logging (routé vers Serilog en production).
    ///
    /// Usage (dans un service ou une vue) :
    ///   using var t = PerfLogger.Start("ChargerDashboard");
    ///   // ... opérations ...
    ///   t.Checkpoint("requête1");
    ///   // ... suite ...
    ///   // Le Dispose() loggue automatiquement le temps total écoulé.
    ///
    /// Pour activer les logs PERF en production, baisser le niveau minimum de
    /// Serilog de Warning → Debug dans LogService (ligne .MinimumLevel.Debug() déjà présente).
    /// </summary>
    public sealed class PerfTimer : IDisposable
    {
        private readonly string    _label;
        private readonly Stopwatch _sw;
        private readonly ILogger?  _logger;
        private bool               _disposed;

        internal PerfTimer(string label, ILogger? logger)
        {
            _label  = label;
            _logger = logger;
            _sw     = Stopwatch.StartNew();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _sw.Stop();
            _logger?.LogDebug("[PERF] {Label} — {ElapsedMs} ms", _label, _sw.ElapsedMilliseconds);
            // Fallback si pas de logger injecté : Debug.WriteLine (visible dans VS Output)
            if (_logger == null)
                System.Diagnostics.Debug.WriteLine($"[PERF] {_label} — {_sw.ElapsedMilliseconds} ms");
        }

        /// <summary>Loggue un checkpoint intermédiaire sans arrêter le chrono.</summary>
        public void Checkpoint(string step)
        {
            _logger?.LogDebug("[PERF] {Label} › {Step} — {ElapsedMs} ms", _label, step, _sw.ElapsedMilliseconds);
            if (_logger == null)
                System.Diagnostics.Debug.WriteLine($"[PERF] {_label} › {step} — {_sw.ElapsedMilliseconds} ms");
        }

        public long ElapsedMs => _sw.ElapsedMilliseconds;
    }

    /// <summary>
    /// Factory statique + gestion d'un logger global optionnel.
    /// PerfLogger.Configure(logger) est appelé une fois au démarrage (App.cs ou DI).
    /// Si aucun logger n'est configuré, les mesures sont écrites via Debug.WriteLine.
    /// </summary>
    public static class PerfLogger
    {
        private static ILogger? _globalLogger;

        /// <summary>
        /// Configure le logger global. Appelé une fois dans App.OnStartup
        /// après construction du ServiceProvider.
        /// </summary>
        public static void Configure(ILogger logger) => _globalLogger = logger;

        /// <summary>
        /// Démarre un chronomètre qui loggue automatiquement à la sortie du bloc using.
        /// Le niveau Debug signifie que les logs ne polluent pas la production
        /// sauf si le niveau minimum est explicitement abaissé.
        /// </summary>
        public static PerfTimer Start(string label) => new PerfTimer(label, _globalLogger);

        /// <summary>
        /// Surcharge avec un logger spécifique (pour les services qui ont déjà leur ILogger<T>).
        /// </summary>
        public static PerfTimer Start(string label, ILogger logger) => new PerfTimer(label, logger);
    }
}
