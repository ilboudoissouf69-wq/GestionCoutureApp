# GestionCoutureApp

Application de bureau WPF pour la gestion d'un atelier de couture.  
Développée pour **Retouche Choco / Ilassa Design** (Burkina Faso).

---

## Table des matières

1. [Prérequis](#prérequis)
2. [Installation et premier lancement](#installation-et-premier-lancement)
3. [Migrations de base de données](#migrations-de-base-de-données)
4. [Rôles utilisateurs](#rôles-utilisateurs)
5. [Fonctionnalités principales](#fonctionnalités-principales)
6. [Sauvegarde automatique](#sauvegarde-automatique)
7. [Données de démonstration](#données-de-démonstration)
8. [Lancer les tests](#lancer-les-tests)
9. [Limitations connues et dettes techniques](#limitations-connues-et-dettes-techniques)
10. [Licence](#licence)

---

## Prérequis

| Outil | Version minimale | Remarque |
|---|---|---|
| Windows | 10 (64-bit) | Windows 11 recommandé |
| .NET Runtime | 8.0 | [Télécharger](https://dotnet.microsoft.com/download/dotnet/8.0) |
| .NET SDK | 8.0 | Requis pour compiler / migrer |
| Git | — | Pour cloner le dépôt |

> Le runtime seul suffit pour exécuter l'application. Le SDK est nécessaire pour
> compiler, exécuter les migrations EF Core ou lancer les tests.

---

## Installation et premier lancement

```bash
# 1. Cloner le dépôt
git clone <url-du-depot>
cd GestionCoutureApp

# 2. Compiler
dotnet build -c Release

# 3. Lancer (le premier démarrage crée la base et le compte Boss par défaut)
dotnet run
```

**Ou** ouvrir `GestionCoutureApp.sln` dans Visual Studio 2022+ et lancer avec F5.

### Premier démarrage

Au tout premier lancement :
1. La base SQLite est créée automatiquement dans  
   `%LOCALAPPDATA%\GestionCoutureApp\gestion_couture.db`
2. Un compte administrateur par défaut est créé :
   - Identifiant : `boss`
   - Mot de passe : `boss123`
3. **Une fenêtre de changement de mot de passe obligatoire s'affiche.**  
   Vous ne pouvez pas accéder à l'application tant que ce mot de passe n'a pas
   été changé. Choisissez un mot de passe fort (≥ 6 caractères).

---

## Migrations de base de données

Les migrations sont appliquées **automatiquement** à chaque démarrage de
l'application (`context.Database.Migrate()` dans `App.cs`).

Pour créer une nouvelle migration manuellement (développement) :

```bash
# Depuis le dossier racine du projet
dotnet ef migrations add NomDeLaMigration --project GestionCoutureApp

# Vérifier le SQL généré sans appliquer
dotnet ef migrations script --project GestionCoutureApp

# Appliquer manuellement (optionnel, l'app le fait au démarrage)
dotnet ef database update --project GestionCoutureApp
```

> La base est stockée dans `%LOCALAPPDATA%\GestionCoutureApp\` et non dans le
> dossier de l'exécutable, pour éviter les problèmes de droits d'écriture sous
> Windows (installation dans Program Files).

---

## Rôles utilisateurs

L'application distingue trois rôles, assignés à la création d'un employé :

### Boss (administrateur)
Accès complet à toutes les fonctionnalités :
- Tableau de bord (statistiques, graphique de revenus)
- Gestion des clients
- Gestion des commandes (créer, modifier, supprimer)
- Gestion des paiements
- Gestion des types de vêtements et mesures requises
- Gestion des employés (créer, modifier, suspendre)
- Calcul et enregistrement des commissions couturiers

### Secrétaire
Accès partiel :
- Tableau de bord
- Gestion des clients
- Création de commandes (avec confirmation par mot de passe)
- Gestion des paiements
- Pas d'accès : employés, types de vêtements, commissions

### Couturier
Accès restreint :
- Tableau de bord personnel (ses propres commandes uniquement)
- Pas d'accès aux données financières ni aux autres employés

---

## Fonctionnalités principales

| Module | Description |
|---|---|
| **Authentification** | Anti brute-force (5 tentatives → verrouillage 2 min), PBKDF2+sel (100 000 itérations) |
| **Clients** | Fiche client, recherche accent-insensible |
| **Commandes** | Mesures dynamiques par type de vêtement (pas de 0,5 cm), photo client (import fichier ou webcam), suivi statut |
| **Paiements** | Pas de suppression — annulation avec motif obligatoire, génération de numéro de reçu unique, protection contre sur-paiement |
| **Commissions** | Aperçu avant enregistrement, verrouillage des commandes incluses, annulation avec déverrouillage et audit |
| **Types de vêtements** | Pantalon, Chemise, Robe, Boubou, Veste — mesures et descriptions configurables par le Boss |
| **Sauvegarde auto** | Toutes les 4 heures, rotation sur 15 fichiers, copie optionnelle vers support externe |

---

## Sauvegarde automatique

La sauvegarde utilise `VACUUM INTO` (cohérente même pendant une écriture),
pas une copie brute du fichier.

**Emplacement local :** `%LOCALAPPDATA%\GestionCoutureApp\Backups\`

**Sauvegarde externe (optionnel) :** créer un fichier texte
`%LOCALAPPDATA%\GestionCoutureApp\chemin_sauvegarde_externe.txt`
contenant le chemin cible, par exemple :

```
D:\SauvegardesCouture
```

ou un dossier synchronisé par un client cloud (OneDrive, Google Drive, etc.).  
Si la destination est inaccessible (clé USB débranchée, réseau hors ligne),
la sauvegarde locale est quand même effectuée — l'échec externe est journalisé
mais ne bloque pas l'application.

> **Recommandation :** configurer au minimum une sauvegarde externe (clé USB
> ou dossier cloud synchronisé). Sans cela, une panne du poste fait perdre la
> base ET tout l'historique de sauvegardes simultanément.

---

## Données de démonstration

Par défaut, **aucune donnée de démonstration n'est insérée** (correctif de
sécurité : l'ancien comportement créait 350 faux clients et des comptes employés
avec des mots de passe connus dans le code source sur toute installation neuve).

Pour charger les données de démo (formation, présentation client) :

```bash
dotnet run -- --demo
```

**Ne jamais utiliser `--demo` sur un poste de production.**

---

## Lancer les tests

```bash
# Depuis la racine du dépôt
dotnet test GestionCoutureApp.Tests

# Avec détail des tests
dotnet test GestionCoutureApp.Tests --verbosity normal
```

Le projet de tests (`GestionCoutureApp.Tests/`) utilise xUnit et
EF Core InMemory. Aucune base de données réelle n'est nécessaire.

Couverture actuelle : **34 tests** couvrant :
- `AuthService` : authentification, brute-force, verrouillage
- `PaiementService` : enregistrement, validation montants, annulation, précision decimal
- `CommissionService` : calcul aperçu, enregistrement, verrouillage, annulation
- `CommandeService` : CRUD, gardes-fous métier (commission verrouillée, montant < encaissé)

---

## Limitations connues et dettes techniques

### Application mono-poste (SQLite)

Cette application est conçue pour **un seul poste à la fois**.  
SQLite sur un partage réseau (NAS, SMB) est déconseillé par ses propres
développeurs pour des accès simultanés : le verrouillage de fichier réseau est
peu fiable selon le NAS/serveur utilisé, et les verrous en mémoire de
l'application ne protègent qu'un seul processus.

Si plusieurs postes doivent travailler simultanément sur les mêmes données
(plusieurs secrétaires en caisse en même temps), la solution robuste est une
vraie base serveur (PostgreSQL ou SQL Server). EF Core est déjà utilisé :
la migration du provider SQLite vers PostgreSQL (`Npgsql.EntityFrameworkCore.PostgreSQL`)
ne touche qu'au `csproj` et à la chaîne de connexion.

### Données en clair (pas de chiffrement au repos)

La base SQLite et les sauvegardes (y compris sur clé USB si configuré) sont
stockées en clair. Si le poste ou la clé USB est volé, toutes les données
clients (mesures, photos, historique financier) sont accessibles.

**Mitigations sans changement de base de données :**
- Activer le chiffrement de lecteur Windows (BitLocker) sur le poste
- Chiffrer le dossier de sauvegardes avec l'EFS Windows (Encrypting File System)
- Ne pas stocker les sauvegardes sur une clé USB non chiffrée

**Solution complète :** migrer vers SQLCipher (`SQLitePCLRaw.bundle_e_sqlcipher`).
C'est un remplacement direct de SQLite avec chiffrement AES-256 transparent.
La migration nécessite : nouveau paquet NuGet, chaîne de connexion avec clé,
et une opération de conversion de la base existante (`ATTACH ... AS ... KEY ...`).

### Dépendance webcam obsolète (AForge, 2013)

`AForge.Video` et `AForge.Video.DirectShow` reposent sur l'ancienne API
DirectShow (COM) et ne sont plus maintenus depuis 2013. Certaines webcams
modernes sous Windows 10/11 ne sont pas détectées correctement.

**Migration recommandée** (impact limité à `Views/WebcamCaptureWindow.cs`) :
- `OpenCvSharp4.Windows` (NuGet) — wrapper .NET de OpenCV, Media Foundation
- `Windows.Media.Capture` (WinRT) — API Microsoft native Windows 10+

### Architecture code-behind (pas de MVVM)

L'application utilise du code-behind WPF pur, sans pattern MVVM
(pas d'`ObservableObject`, pas de `RelayCommand`). Ce choix est assumé pour
une application de cette taille. Les fichiers les plus volumineux
(`CommandesView.cs`, `PaiementsView.cs`) mélangent logique UI et logique
métier, ce qui les rend plus difficiles à maintenir et impossibles à tester
directement (les tests unitaires couvrent les *Services*, pas les vues).

Si une migration MVVM progressive est engagée, `CommunityToolkit.Mvvm`
(déjà retiré du `.csproj`) est la bibliothèque recommandée pour ce projet.

---

## Licence

Propriétaire — © 2026 Retouche Choco / Ilassa Design. Tous droits réservés.  
Usage interne uniquement. Ne pas redistribuer sans autorisation écrite.
