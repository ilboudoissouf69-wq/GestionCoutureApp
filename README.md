# GestionCoutureApp

Application de bureau Windows pour la gestion d'un atelier de couture.  
Développée pour **Retoupe Choco / Ilassa Design** — Burkina Faso.

---

## Table des matières

1. [Prérequis](#prérequis)
2. [Installation sur un nouveau poste](#installation-sur-un-nouveau-poste)
3. [Premier démarrage](#premier-démarrage)
4. [Lancer l'application au quotidien](#lancer-lapplication-au-quotidien)
5. [Rôles utilisateurs](#rôles-utilisateurs)
6. [Fonctionnalités](#fonctionnalités)
7. [Sauvegarde et restauration](#sauvegarde-et-restauration)
8. [Lancer les tests](#lancer-les-tests)
9. [Limitations connues](#limitations-connues)
10. [Licence](#licence)

---

## Prérequis

| Outil | Version | Lien de téléchargement |
|---|---|---|
| Windows | 10 64-bit minimum (Windows 11 recommandé) | — |
| .NET Runtime | **8.0** | https://dotnet.microsoft.com/download/dotnet/8.0 |
| .NET SDK | **8.0** | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Git | toute version récente | https://git-scm.com/download/win |

> Le **.NET Runtime** suffit pour utiliser l'application au quotidien.  
> Le **.NET SDK** est nécessaire uniquement pour compiler, migrer la base ou lancer les tests.

---

## Installation sur un nouveau poste

### Étape 1 — Installer .NET 8

1. Télécharger le **SDK .NET 8** depuis https://dotnet.microsoft.com/download/dotnet/8.0  
   (choisir *Windows x64 — SDK Installer*)
2. Exécuter l'installeur et suivre les étapes (suivant → suivant → terminer)
3. Vérifier l'installation dans un terminal PowerShell :

```powershell
dotnet --version
# doit afficher : 8.0.xxx
```

### Étape 2 — Installer Git

1. Télécharger Git depuis https://git-scm.com/download/win
2. Installer avec les options par défaut
3. Vérifier :

```powershell
git --version
# doit afficher : git version 2.x.x
```

### Étape 3 — Cloner le dépôt

Ouvrir PowerShell dans le dossier où vous souhaitez installer l'application, puis :

```powershell
git clone <url-du-depot>
cd GestionCoutureApp
```

### Étape 4 — Compiler l'application

```powershell
dotnet build -c Release
```

La commande télécharge automatiquement tous les paquets NuGet nécessaires.  
La première fois peut prendre 1 à 2 minutes selon la connexion internet.

### Étape 5 — Lancer l'application

```powershell
dotnet run
```

**C'est tout.** La base de données est créée automatiquement au premier démarrage.

---

## Premier démarrage

Au tout premier lancement, l'application :

1. Crée la base de données SQLite dans :
   ```
   C:\Users\<VotreNom>\AppData\Local\GestionCoutureApp\gestion_couture.db
   ```
2. Crée un compte administrateur par défaut :
   - **Identifiant** : `boss`
   - **Mot de passe** : `boss123`
3. Affiche immédiatement une fenêtre de **changement de mot de passe obligatoire**.  
   Vous ne pouvez pas continuer tant que le mot de passe n'est pas changé.  
   Choisissez un mot de passe d'au moins 6 caractères et notez-le soigneusement.

> ⚠️ Ne communiquez jamais le mot de passe boss à une secrétaire ou un couturier.

---

## Lancer l'application au quotidien

### Option A — Via PowerShell (recommandé pour développement)

```powershell
cd C:\chemin\vers\GestionCoutureApp
dotnet run
```

### Option B — Double-clic sur l'exécutable compilé

Après un `dotnet build -c Release`, l'exécutable se trouve dans :

```
GestionCoutureApp\bin\Release\net8.0-windows\GestionCoutureApp.exe
```

Vous pouvez créer un raccourci sur le bureau vers ce fichier `.exe`.

### Option C — Via Visual Studio 2022

1. Ouvrir `GestionCoutureApp.sln`
2. Appuyer sur **F5** (ou le bouton ▶ Démarrer)

---

## Rôles utilisateurs

### Boss (administrateur)

Accès complet :
- Tableau de bord avec statistiques financières
- Gestion des clients, commandes, paiements
- Gestion des employés (créer, modifier, suspendre un compte)
- Calcul et enregistrement des commissions couturiers
- Configuration des types de vêtements et mesures
- Paramètres de l'application, sauvegardes
- Journal d'audit de sécurité (lecture seule)
- Suppression logique de commandes (avec motif obligatoire, tracé)
- Annulation de paiements (avec motif obligatoire, tracé)

### Secrétaire

Accès partiel :
- Tableau de bord
- Gestion des clients
- Création de commandes
- Enregistrement de paiements
- **Interdit** : annuler un paiement, supprimer une commande, accéder aux employés, aux commissions, aux paramètres de sécurité

### Couturier

Accès restreint :
- Tableau de bord personnel (ses propres pièces uniquement)
- **Aucun accès** aux données financières ni aux autres employés

---

## Fonctionnalités

| Module | Description |
|---|---|
| **Authentification** | Verrouillage après 5 tentatives (2 min), hashage PBKDF2 + sel (100 000 itérations) |
| **Clients** | Fiche client, recherche insensible aux accents |
| **Commandes** | Commandes multi-pièces, mesures dynamiques par type de vêtement, photo client (fichier ou webcam), suivi de statut |
| **Paiements** | Annulation avec motif obligatoire (jamais de suppression), numéro de reçu unique, protection contre le sur-paiement |
| **Commissions** | Aperçu avant validation, verrouillage des pièces incluses, annulation avec déverrouillage |
| **Retours / Retouches** | Suivi des reprises gratuites et payantes |
| **Dépenses** | Enregistrement et validation des dépenses atelier |
| **Trésorerie** | Bilan financier par période, rapport de cohérence argent/travail |
| **Journal d'audit** | Traçabilité immuable de toutes les actions financières sensibles (Boss uniquement, lecture seule) |
| **Sauvegarde auto** | Toutes les 4 heures, rotation sur 15 fichiers locaux, copie optionnelle Google Drive |
| **Types de vêtements** | Mesures requises configurables par le Boss |

---

## Sauvegarde et restauration

### Emplacement des sauvegardes locales

```
C:\Users\<VotreNom>\AppData\Local\GestionCoutureApp\Backups\
```

Les sauvegardes sont nommées `gestion_couture_YYYYMMDD_HHMMSS.db`.  
Les 15 plus récentes sont conservées automatiquement.

### Restaurer une sauvegarde

1. Fermer l'application
2. Copier le fichier `.db` de sauvegarde vers :
   ```
   C:\Users\<VotreNom>\AppData\Local\GestionCoutureApp\gestion_couture.db
   ```
   (remplacer le fichier existant)
3. Relancer l'application

### Sauvegarde Google Drive (optionnel)

Pour activer la sauvegarde vers Google Drive :

1. Créer un projet Google Cloud et activer l'API Drive
2. Télécharger le fichier `client_secret.json` (identifiants OAuth)
3. Placer ce fichier dans :
   ```
   C:\Users\<VotreNom>\AppData\Local\GestionCoutureApp\client_secret.json
   ```
4. Activer la sauvegarde Drive dans **Paramètres → Sauvegarde**

> ⚠️ Ne jamais mettre `client_secret.json` dans le dépôt Git.

---

## Lancer les tests

```powershell
# Depuis le dossier racine du projet
# (fermer l'application avant de lancer les tests)
dotnet test GestionCoutureApp.Tests
```

Résultat attendu : **41/41 réussis, 0 ignoré, 0 échec.**

> Si le build échoue avec "fichier verrouillé", c'est que l'application est encore
> ouverte. Fermez-la complètement, vérifiez avec `Get-Process dotnet`, puis relancez.

---

## Limitations connues

### Application mono-poste

L'application est conçue pour **un seul poste à la fois**.  
SQLite sur un partage réseau (NAS, partage Windows) est déconseillé pour des accès simultanés.  
Si plusieurs postes doivent travailler en même temps, la migration vers PostgreSQL
est possible sans changer l'architecture (EF Core gère les deux providers).

### Données non chiffrées au repos

La base SQLite est stockée en clair. En cas de vol du poste ou de la clé USB
de sauvegarde, toutes les données sont accessibles.

**Mesures de protection recommandées :**
- Activer BitLocker sur le disque du poste
- Chiffrer le dossier de sauvegardes avec EFS Windows
- Ne pas stocker les sauvegardes sur une clé USB non chiffrée

### Webcam (dépendance ancienne)

La capture photo par webcam utilise la bibliothèque AForge (2013, API DirectShow).
Certaines webcams récentes peuvent ne pas être détectées.  
En cas de problème, utiliser l'import de photo par fichier (bouton "Choisir un fichier").

---

## Licence

Propriétaire — © 2026 Retoupe Choco / Ilassa Design. Tous droits réservés.  
Usage interne uniquement. Ne pas redistribuer sans autorisation écrite.
