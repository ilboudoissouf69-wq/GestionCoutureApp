namespace GestionCoutureApp.Services
{
    /// <summary>
    /// Événement de notification pour les changements de paramètres globaux.
    /// Permet aux vues de se mettre à jour dynamiquement sans redémarrage.
    /// </summary>
    public class SettingsChangedEvent
    {
        public SettingsChangedType Type { get; set; }
        public object? Data { get; set; }
    }

    /// <summary>
    /// Types de changements de paramètres qui nécessitent une mise à jour globale.
    /// </summary>
    public enum SettingsChangedType
    {
        /// <summary>La langue a changé (FR ↔ EN)</summary>
        Language,
        /// <summary>La couleur d'accentuation a changé</summary>
        AccentColor,
        /// <summary>Les informations de l'atelier ont changé (nom, téléphone, adresse)</summary>
        ReceiptInfo,
        /// <summary>Les messages WhatsApp ont changé</summary>
        WhatsAppMessages,
        /// <summary>Les paramètres métier ont changé</summary>
        BusinessSettings
    }
}
