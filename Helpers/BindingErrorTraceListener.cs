using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace GestionCoutureApp.Helpers
{
    /// <summary>
    /// Listener personnalisé pour capturer et logger les erreurs de binding WPF
    /// qui sont normalement silencieuses et invisibles dans l'application.
    /// 
    /// Ces erreurs incluent:
    /// - Binding vers des propriétés inexistantes
    /// - Erreurs de conversion de type dans les bindings
    /// - ValidationRules qui échouent
    /// - Binding circulaires ou invalides
    /// </summary>
    public class BindingErrorTraceListener : TraceListener
    {
        private readonly string _logFilePath;
        private readonly StringBuilder _messageBuffer = new StringBuilder();

        public BindingErrorTraceListener()
        {
            // Logger dans le même dossier que les autres logs de l'application
            var logFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GestionCoutureApp",
                "Logs"
            );

            Directory.CreateDirectory(logFolder);
            _logFilePath = Path.Combine(logFolder, $"BindingErrors_{DateTime.Now:yyyyMMdd}.log");
        }

        public override void Write(string message)
        {
            // Accumuler le message (les erreurs WPF sont souvent écrites en plusieurs parties)
            _messageBuffer.Append(message);
        }

        public override void WriteLine(string message)
        {
            // Ajouter le dernier fragment et traiter le message complet
            _messageBuffer.Append(message);
            var fullMessage = _messageBuffer.ToString();
            _messageBuffer.Clear();

            // Ne logger que les véritables erreurs et warnings
            if (fullMessage.Contains("Error") || 
                fullMessage.Contains("Warning") || 
                fullMessage.Contains("Exception") ||
                fullMessage.Contains("BindingExpression"))
            {
                try
                {
                    var logEntry = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {fullMessage}{Environment.NewLine}";
                    File.AppendAllText(_logFilePath, logEntry);

                    // Également écrire dans la console Debug pour Visual Studio
                    Debug.WriteLine($"[BINDING ERROR] {fullMessage}");
                }
                catch
                {
                    // Ne jamais crasher à cause du logging lui-même
                }
            }
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string message)
        {
            // Traiter les événements de trace structurés
            if (eventType == TraceEventType.Error || eventType == TraceEventType.Warning)
            {
                WriteLine($"[{eventType}] {message}");
            }
        }

        public override void TraceEvent(TraceEventCache eventCache, string source, TraceEventType eventType, int id, string format, params object[] args)
        {
            if (eventType == TraceEventType.Error || eventType == TraceEventType.Warning)
            {
                WriteLine($"[{eventType}] {string.Format(format, args)}");
            }
        }
    }
}
