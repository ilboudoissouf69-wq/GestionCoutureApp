# Changelog

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
