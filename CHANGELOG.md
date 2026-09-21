# Changelog

## [2.1] - 2026-09-21

### 🌍 Gestion Dynamique de la Localisation, des Thèmes et des Paramètres

#### ✅ Nouveaux Services Centralisés

**EventAggregator (Notifications Globales)**
- Création de `SettingsChangedEvent` pour notification des changements de paramètres
- Implémentation de `IEventAggregator` pour communication inter-composants sans couplage
- Types de changements : Language, AccentColor, ReceiptInfo, WhatsAppMessages, BusinessSettings

**LanguageService (Multilinguisme Dynamique)**
- Création de `ILanguageService` pour gestion centralisée de la langue
- Changement dynamique de la culture (`Thread.CurrentThread.CurrentUICulture`)
- Fichiers de ressources `.resx` pour FR et EN (`Resources/Strings.fr.resx`, `Resources/Strings.en.resx`)
- Notification automatique de toutes les vues lors du changement de langue
- Méthode `GetString(key)` pour obtenir les traductions

**ThemeService (Gestion Dynamique des Couleurs)**
- Création de `IThemeService` pour gestion centralisée des thèmes
- Application dynamique de la couleur d'accentuation via `DynamicResource`
- Mise à jour automatique de `AccentColor`, `AccentBrush`, `AccentHoverBrush`
- Validation du format hexadécimal des couleurs

**ReceiptService (Synchronisation des Reçus)**
- Création de `IReceiptService` pour centralisation des infos d'atelier
- Structure `ReceiptInfo` (NomAtelier, Telephone, Adresse, PiedRecu)
- Notification automatique lors de la modification des paramètres d'impression
- Synchronisation avec toutes les vues utilisant les reçus

#### 🎨 Mises à jour de l'Interface

**App.xaml**
- Conversion des couleurs statiques en `DynamicResource` pour permettre les changements dynamiques
- `AccentBrush` et `AccentHoverBrush` utilisent maintenant `DynamicResource AccentColor`

**ParametresView**
- Injection des nouveaux services (`ILanguageService`, `IThemeService`, `IReceiptService`, `IEventAggregator`)
- `BtnAppliquerCouleur_Click` utilise maintenant `ThemeService.SetAccentColorAsync()`
- `BtnSauvegarderImpression_Click` utilise maintenant `ReceiptService.UpdateReceiptInfoAsync()`
- Ajout de gestionnaires pour changement de langue (`BtnLangueFR_Click`, `BtnLangueEN_Click`)

#### 📝 Fichiers Ajoutés

- `Services/SettingsChangedEvent.cs` - Événements de notification
- `Services/IEventAggregator.cs` - Interface d'agrégation d'événements
- `Services/EventAggregator.cs` - Implémentation du pattern Event Aggregator
- `Services/ILanguageService.cs` - Interface du service de localisation
- `Services/LanguageService.cs` - Service de gestion de la langue
- `Services/IThemeService.cs` - Interface du service de thèmes
- `Services/ThemeService.cs` - Service de gestion des couleurs
- `Services/IReceiptService.cs` - Interface du service de reçus
- `Services/ReceiptService.cs` - Service de synchronisation des reçus
- `Resources/Strings.fr.resx` - Traductions françaises
- `Resources/Strings.en.resx` - Traductions anglaises

#### 🔧 Modifications de l'Architecture

**App.cs**
- Enregistrement de `IEventAggregator` comme singleton
- Enregistrement de `ILanguageService` comme singleton
- Enregistrement de `IThemeService` comme singleton
- Enregistrement de `IReceiptService` comme singleton

**FenetreRecu.cs**
- Ajout de l'injection de `IReceiptService` dans le constructeur
- Utilisation dynamique des infos d'atelier depuis `ReceiptService`
- Les informations de l'atelier (nom, téléphone, adresse, pied de reçu) sont maintenant chargées dynamiquement depuis les paramètres
- Synchronisation automatique avec les changements dans les paramètres d'impression

**PaiementsView.cs**
- Injection de `IReceiptService` pour synchronisation des reçus
- Transmission du service à `FenetreRecu` lors de la génération de reçus
- Les reçus générés depuis PaiementsView utilisent maintenant les paramètres dynamiques

**ParametresView.cs**
- Modification de `SelectionnerLangue` pour utiliser `LanguageService.SetLanguageAsync()`
- Changement de culture dynamique lors de la sélection de langue
- Publication d'événements de notification via `EventAggregator`

**App.xaml**
- Correction : les styles de boutons variantes (`BtnSuccess`, `BtnDanger`, `BtnWarning`, `BtnNavy`) utilisent maintenant `StaticResource` pour `BasedOn` (évitent l'erreur "BtnSuccess not found")
- Les couleurs d'accentuation utilisent `DynamicResource` pour permettre les changements dynamiques

---

## [2.0] - 2026-09-21

### 🔒 Sécurité
- **Audit #12** : Introduction de `enum RoleEmploye` pour remplacer les chaînes magiques ("Boss", "Secretaire", "Couturier")
  - Création de `Models/RoleEmploye.cs` avec enum et méthodes d'extension
  - Ajout de la propriété `RoleEnum` dans `Employe` pour validation robuste
  - Extension de `AuthorizationHelper` avec méthodes `RequireRoleEnum` et `RequireRoleByIdEnum`
  - Validation des rôles via enum dans `AuthService.AuthentifierInterne`
  - Utilisation de `RequireRoleByIdEnum` dans `PaiementService.Annuler`
- **Audit #13** : Correction du deadlock potentiel dans `WhatsAppService`
  - Remplacement de `.Result` synchrone par `await` async dans toutes les méthodes
  - Conversion de `IWhatsAppService` en interface async complète
  - Mise à jour des appels dans `CommandesView.cs` et `ClientsView.cs` avec `async/await`
- **Audit #9** : Utilisation de `RequireRoleById` au lieu de la recherche par nom dans `PaiementService.Annuler`
- **Secret Google OAuth** : Suppression de `client_secret.json` du suivi Git et déplacement vers `%LOCALAPPDATA%`

### 🧹 Nettoyage
- Suppression de `repair.py` (script de dépannage avec chemin absolu)
- Réorganisation de la documentation : déplacement des fichiers de travail vers le dossier `docs/`
- **Documentation déplacée vers `docs/`** :
  - `AMELIORATIONS_RETOURS.md`
  - `AUDIT_IMPLEMENTATION_COMPLETE.md`
  - `CORRECTIONS_FINALES.md`
  - `GUIDE_TEST.md`
  - `LOGGING_README.md`
  - `README_CHANGEMENTS.txt`
  - `TRESORERIE_GUIDE.md`

### 🔧 Améliorations
- **Logging** : Système de logging professionnel avec Serilog (session précédente)
- **Retours** : Correction de la logique "Prime Zéro Défaut" (95% qualité minimum)
- **Interface** : Refonte de `RetoursView` pour meilleure lisibilité

## [1.0] - 2026-09-18

### ✨ Nouvelles fonctionnalités
- Système de logging professionnel avec Serilog
- Gestion des retours et reprises avec performance qualité
- Intégration WhatsApp pour notifications clients
- Sauvegarde automatique locale et Google Drive optionnelle
- Système d'alertes de rendez-vous
- Gestion des dépenses et matériaux supplémentaires

### 🔒 Sécurité
- Hachage PBKDF2-SHA256 pour mots de passe (100 000 itérations)
- Verrouillage anti brute-force (5 échecs → 2 minutes)
- RBAC en profondeur (Boss, Secrétaire, Couturier)
- Suppression de données de démonstration automatique en production

### 🏗️ Architecture
- Base de données SQLite avec EF Core
- Injection de dépendances
- Services pour la logique métier
- Code-behind WPF (choix assumé pour application de cette taille)

### 📋 Limitations connues
- Application mono-poste (SQLite)
- Pas de chiffrement au repos (données en clair)
- Dépendance AForge obsolète (webcam)
- Architecture code-behind (pas de MVVM)

---

## Notes de version

Pour les détails techniques et procédures de mise à jour, consultez les fichiers dans le dossier `docs/` :

- `docs/LOGGING_README.md` - Guide du système de logging
- `docs/AMELIORATIONS_RETOURS.md` - Détails interface RetoursView
- `docs/TRESORERIE_GUIDE.md` - Guide trésorerie
- `docs/GUIDE_TEST.md` - Guide de test étape par étape

---

**Version : 2.0**  
**Date : 21 septembre 2026**  
**Projet : GestionCoutureApp - Retoupe Choco / Ilassa Design**  
**Auteur : Session de correction d'audit**
