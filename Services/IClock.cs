namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Abstraction de l'horloge système. Permet de mocker le temps dans les tests.
    /// Les dates d'audit sont stockées en UTC (UtcNow).
    /// Les dates d'affichage utilisent Now (heure locale).
    /// </summary>
    public interface IClock
    {
        /// <summary>Heure locale courante (affichage uniquement).</summary>
        DateTime Now { get; }
        /// <summary>Heure UTC courante (stockage en base).</summary>
        DateTime UtcNow { get; }
    }

    /// <summary>Implémentation production qui délègue à DateTime.</summary>
    public sealed class SystemClock : IClock
    {
        public DateTime Now    => DateTime.Now;
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
