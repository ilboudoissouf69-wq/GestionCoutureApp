# 📋 Système de Logging - GestionCoutureApp

## 🎯 Vue d'ensemble

Un système de logging professionnel a été intégré à l'application pour enregistrer automatiquement toutes les actions, événements et erreurs. Les logs sont essentiels pour le diagnostic à distance et le support technique.

---

## 📦 Composants ajoutés

### 1. **Packages NuGet installés**
- `Serilog` (v4.2.0) - Bibliothèque de logging structuré
- `Serilog.Sinks.File` (v6.0.0) - Écriture dans des fichiers
- `Serilog.Extensions.Logging` (v8.0.0) - Intégration avec Microsoft.Extensions.Logging

### 2. **Services créés**

#### `ILogService` (`Services/ILogService.cs`)
Interface définissant les méthodes de logging :
- `LogInfo()` - Informations générales
- `LogWarning()` - Avertissements
- `LogError()` - Erreurs avec exceptions
- `LogAction()` - Actions utilisateur spécifiques
- `ObtenirCheminDossierLogs()` - Chemin du dossier des logs

#### `LogService` (`Services/LogService.cs`)
Implémentation utilisant Serilog avec :
- Rotation quotidienne des fichiers
- Conservation de 30 jours d'historique
- Format structuré : `[Date Heure] [Niveau] [Utilisateur] Message`
- Emplacement : `%AppData%\GestionCouture\Logs\`

### 3. **Nouvel onglet dans Paramètres**

**"🛠️ Maintenance & Support"** - 5ème onglet système

Contient :
- **Informations Système**
  - Version du logiciel
  - État de la base de données
  - Emplacement de la BDD

- **Diagnostic & Support Technique**
  - Description explicative des logs
  - Bouton "📂 Ouvrir le dossier des Logs"
  - Chemin complet du dossier affiché

---

## 📁 Emplacement des fichiers de logs

```
C:\Users\[USER]\AppData\Roaming\GestionCouture\Logs\
```

Les fichiers sont nommés :
- `gestion_couture_20260918.log` (aujourd'hui)
- `gestion_couture_20260917.log` (hier)
- etc.

---

## 📝 Ce qui est enregistré automatiquement

### ✅ Au démarrage de l'application
```
2026-09-18 14:30:15.123 [INF] [SYSTÈME] ═══════════════════════════════════════════════════
2026-09-18 14:30:15.124 [INF] [SYSTÈME] Application Gestion Couture démarrée
2026-09-18 14:30:15.125 [INF] [SYSTÈME] Version: 2.0 - Retouche Choco
2026-09-18 14:30:15.126 [INF] [SYSTÈME] Date de démarrage: 18/09/2026 14:30:15
2026-09-18 14:30:15.127 [INF] [SYSTÈME] ═══════════════════════════════════════════════════
```

### 🔐 Connexions utilisateur
```
2026-09-18 14:32:10.456 [INF] [Admin] ACTION: Connexion réussie | Utilisateur: Admin Boss (Boss)
```

### ⚠️ Tentatives de connexion échouées
```
2026-09-18 14:35:20.789 [WRN] [SYSTÈME] Tentative de connexion échouée pour l'identifiant: john.doe
```

### ❌ Erreurs non gérées
```
2026-09-18 14:40:33.012 [ERR] [Admin] Exception non gérée dans le Dispatcher
System.NullReferenceException: Object reference not set to an instance of an object.
   at GestionCoutureApp.Views.CommandesView.BtnSauvegarder_Click(...)
```

### 🚪 Fermeture de l'application
```
2026-09-18 18:00:00.123 [INF] [Admin] Fermeture de l'application par Admin
```

### 📂 Ouverture du dossier des logs
```
2026-09-18 15:15:45.678 [INF] [Admin] ACTION: Ouverture dossier logs | Utilisateur: Admin
```

---

## 🔧 Intégration dans le code

### Services enregistrés dans `App.cs`

```csharp
// Services
services.AddSingleton<ILogService, LogService>();  // Service de logging
services.AddSingleton<IAuthService, AuthService>();
// ... autres services
```

### Utilisation dans les vues

```csharp
public class MaView : Page
{
    private readonly ILogService _log;
    private readonly IAuthService _auth;

    public MaView()
    {
        _log = App.Services.GetRequiredService<ILogService>();
        _auth = App.Services.GetRequiredService<IAuthService>();
    }

    private void BtnSauvegarder_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            // ... logique métier ...
            
            string utilisateur = _auth.UtilisateurConnecte?.Nom ?? "Inconnu";
            _log.LogAction("Sauvegarde commande", $"ID: {commande.Id}", utilisateur);
        }
        catch (Exception ex)
        {
            _log.LogError("Erreur lors de la sauvegarde", ex, _auth.UtilisateurConnecte?.Nom);
            MessageBox.Show("Erreur: " + ex.Message);
        }
    }
}
```

---

## 📊 Interface utilisateur

### Onglet "Maintenance & Support"

**Accès :** Paramètres → Navigation → 🛠️ Maintenance

**Contenu visible :**
1. **Informations Système**
   - Version : v2.0 — Retouche Choco
   - État BDD : 🟢 Connectée (Local)
   - Chemin BDD : C:\Users\...\GestionCouture\gestion_couture.db

2. **Diagnostic & Support**
   - Encadré jaune avec explication des logs
   - Bouton bleu : "📂 Ouvrir le dossier des Logs"
   - Texte gris : Emplacement du dossier

**Bilingue :** Français / Anglais (suit le paramètre de langue)

---

## 🌐 Support multilingue

Toutes les chaînes sont traduites dans le dictionnaire `_t` de `ParametresView.cs` :

| Clé | Français | English |
|-----|----------|---------|
| `nav_maintenance` | Maintenance | Maintenance |
| `page_maint_titre` | Maintenance & Support | Maintenance & Support |
| `lbl_diagnostic` | Diagnostic & Support Technique | Diagnostics & Technical Support |
| `btn_ouvrir_logs` | 📂 Ouvrir le dossier des Logs | 📂 Open Logs Folder |

---

## 🐛 Corrections effectuées

### RetoursView.cs
- ✅ Ajout de `_p` (IParametresService)
- ✅ Ajout de `_whatsApp` (IWhatsAppService)
- ✅ Les boutons WhatsApp dans la vue Retours fonctionnent maintenant

### RetoursView.xaml
- ✅ Correction des balises XML dupliquées
- ✅ Suppression du code DataGrid en double

---

## 🎓 Guide d'utilisation pour le Boss

### Comment accéder aux logs ?

1. Ouvrir l'application
2. Aller dans **⚙️ Paramètres**
3. Cliquer sur **🛠️ Maintenance** (dernier onglet système)
4. Cliquer sur le bouton **📂 Ouvrir le dossier des Logs**
5. Le dossier s'ouvre dans l'Explorateur Windows
6. Double-cliquer sur le fichier `.log` le plus récent
7. Ouvrir avec Notepad ou un autre éditeur de texte

### Quand consulter les logs ?

- ❌ Après une erreur inattendue
- 🐛 Pour diagnostiquer un bug récurrent
- 📞 Avant d'appeler le support technique
- 🔍 Pour vérifier les actions d'un utilisateur
- 📊 Pour analyser l'utilisation de l'application

### Que partager avec le développeur ?

Lors d'un problème :
1. Ouvrir le fichier log du jour où le problème s'est produit
2. Copier les lignes avec `[ERR]` (erreurs)
3. Copier 5-10 lignes avant et après l'erreur (contexte)
4. Envoyer par email ou WhatsApp

---

## 🔒 Sécurité et confidentialité

### Ce qui N'EST PAS enregistré :
- ❌ Mots de passe (jamais)
- ❌ Numéros de carte bancaire
- ❌ Photos des clients
- ❌ Contenu détaillé des messages WhatsApp

### Ce qui EST enregistré :
- ✅ Actions utilisateur (connexion, création commande, etc.)
- ✅ Messages d'erreur techniques
- ✅ Nom de l'utilisateur connecté
- ✅ Horodatage précis (date + heure + millisecondes)

---

## 📈 Maintenance

### Nettoyage automatique
- Les fichiers de plus de **30 jours** sont supprimés automatiquement
- Pas d'action manuelle nécessaire
- Espace disque : ~1-5 Mo par mois

### Sauvegarde manuelle
Les logs ne sont PAS inclus dans la sauvegarde Google Drive (fichiers techniques).  
Pour conserver des logs importants :
1. Copier les fichiers `.log` ailleurs
2. Les renommer avec une date et une description
3. Les archiver dans un dossier séparé

---

## ✅ Résumé des avantages

| Avantage | Description |
|----------|-------------|
| 🔍 **Diagnostic rapide** | Identifie la cause exacte d'un problème en quelques secondes |
| 🕐 **Historique complet** | 30 jours d'actions conservées |
| 👥 **Traçabilité** | Sait qui a fait quoi et quand |
| 📞 **Support à distance** | Le développeur peut résoudre un problème sans se déplacer |
| 🛡️ **Détection d'anomalies** | Repère les tentatives de connexion suspectes |
| 📊 **Analyse d'utilisation** | Comprend comment l'application est utilisée |

---

## 📞 Support technique

En cas de problème avec le système de logging :
- Vérifier que le dossier existe : `%AppData%\GestionCouture\Logs\`
- Vérifier les permissions d'écriture sur ce dossier
- Redémarrer l'application
- Contacter le développeur avec une capture d'écran

---

**Version du document :** 1.0  
**Date :** 18 septembre 2026  
**Auteur :** Assistant Kiro  
**Application :** GestionCoutureApp v2.0 - Retouche Choco
