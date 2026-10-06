# GestionCoutureApp

Application de bureau Windows pour la gestion d'un atelier de **RETOUCHE** de vêtements.  
Développée pour **ILBOUDO ISSOUF — Burkina Faso.  
Monnaie : FCFA.

> **Important** : l'atelier fait de la **retouche** (élargir, raccourcir, reprendre un vêtement
> existant). Il ne fabrique pas de vêtements neufs. Les couturiers n'ont pas accès à
> l'ordinateur : la secrétaire met à jour les statuts pour eux.

---

## Table des matières

1. [Prérequis](#prérequis)
2. [Installation sur un nouveau poste](#installation-sur-un-nouveau-poste)
3. [Premier démarrage](#premier-démarrage)
4. [Lancer l'application au quotidien](#lancer-lapplication-au-quotidien)
5. [Rôles utilisateurs et règles métier](#rôles-utilisateurs-et-règles-métier)
6. [Fonctionnalités](#fonctionnalités)
7. [Règles financières](#règles-financières)
8. [Sauvegarde et restauration](#sauvegarde-et-restauration)
9. [Lancer les tests](#lancer-les-tests)
10. [Limitations connues](#limitations-connues)
11. [Licence](#licence)

---

## Prérequis

| Outil | Version | Lien de téléchargement |
|---|---|---|
| Windows | 10 64-bit minimum (Windows 11 recommandé) | — |
| .NET Runtime | **8.0** | https://dotnet.microsoft.com/download/dotnet/8.0 |
| .NET SDK | **8.0** | https://dotnet.microsoft.com/download/dotnet/8.0 |
| Git | toute version récente | https://git-scm.com/download/win |

---

## Installation sur un nouveau poste

### Étape 1 — Installer .NET 8

1. Télécharger le **SDK .NET 8** depuis https://dotnet.microsoft.com/download/dotnet/8.0  
   (choisir *Windows x64 — SDK Installer*)
2. Exécuter l'installeur
3. Vérifier dans PowerShell :

```powershell
dotnet --version
# doit afficher : 8.0.xxx
```

### Étape 2 — Installer Git

```powershell
git --version   # doit afficher : git version 2.x.x
```

### Étape 3 — Cloner le dépôt

```powershell
git clone <url-du-depot>
cd GestionCoutureApp
```

### Étape 4 — Compiler

```powershell
dotnet build -c Release
```

### Étape 5 — Lancer

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
3. Affiche une fenêtre de **changement de mot de passe obligatoire**.

> ⚠️ Ne communiquez jamais le mot de passe boss à une secrétaire ou un couturier.

---

## Lancer l'application au quotidien

```powershell
cd C:\chemin\vers\GestionCoutureApp
dotnet run
```

Ou double-clic sur `GestionCoutureApp.exe` dans `bin\Release\net8.0-windows\`.

---

## Rôles utilisateurs et règles métier

### Boss (administrateur)

- Accès complet à **tous** les modules
- Peut modifier le prix d'une pièce (même après paiement), changer le client, le type de vêtement
- Seul à pouvoir annuler des paiements et des commissions
- Peut supprimer une commande sans paiement (motif obligatoire, tracé dans l'audit)
- Peut rétrograder le statut d'une pièce (retour arrière) avec motif obligatoire
- Peut forcer la livraison d'une commande non soldée avec motif

### Secrétaire

- Accès : tableau de bord, clients, commandes, paiements, statuts, retours, alertes

**Sans aucun paiement sur la commande :**
- Peut tout modifier (client, type de vêtement, prix, date, heure, couturier, statut, notes)
- Peut supprimer la commande si toutes les pièces sont encore "À faire" (motif obligatoire)
- Peut supprimer une pièce si elle est vierge ("À faire", aucun paiement)

**Après le premier paiement encaissé :**
- Client, type de vêtement et montants **verrouillés** (Boss uniquement pour les modifier)
- Couturier, date, heure et notes restent modifiables
- Ne peut pas supprimer la commande

**Après Terminée/Livrée :**
- **Lecture seule** — aucune modification possible
- Boss uniquement avec motif de rétrogradation

**Interdit (toujours) :**
- Annuler un paiement ou une commission
- Accéder aux employés, commissions, paramètres de sécurité, dépenses

### Couturier

- Accès restreint : tableau de bord personnel (ses propres pièces uniquement)
- Aucun accès aux données financières ni aux autres employés
- **N'utilise pas l'ordinateur** : la secrétaire met à jour les statuts pour lui

---

## Flux de statuts d'une pièce

```
À faire → En cours → Terminée → Livrée
```

- **Avance** (À faire → Terminée, etc.) : autorisée pour tous (Boss et Secrétaire)
- **Retour arrière** (Terminée → En cours, etc.) : Boss uniquement + motif obligatoire
- **Livrée** : bloqué si commande non soldée (sauf Boss avec motif)
- **Historique** : chaque changement est tracé dans le journal d'audit

---

## Fonctionnalités

| Module | Description |
|---|---|
| **Authentification** | Verrouillage après 5 tentatives (2 min), hashage PBKDF2 + sel (100 000 itérations) |
| **Clients** | Fiche client, recherche insensible aux accents, détection de doublons |
| **Commandes** | Multi-pièces, mesures dynamiques, photo, flux strict de statuts |
| **Paiements** | Annulation avec motif, reçu unique, protection contre le sur-paiement |
| **Commissions** | Basées sur `DateTerminee` (et non la date de RDV). Taux configurables. |
| **Retours / Retouches** | Suivi des reprises gratuites et payantes |
| **Dépenses** | Enregistrement et validation des dépenses réelles de l'atelier |
| **Trésorerie** | Bilan financier par période, rapport de cohérence |
| **Alertes RDV** | Rafraîchissement auto toutes les 60 s, niveaux J-1/jour/retard, son + popup, badge menu |
| **Journal d'audit** | Traçabilité immuable de toutes les actions sensibles (Boss, lecture seule) |
| **Sauvegarde auto** | Toutes les 4 heures, rotation 15 fichiers locaux, optionnel Google Drive |
| **File "À attribuer"** | Badge compteur des pièces actives sans couturier assigné |
| **Suggestion couturier** | À la création : propose le couturier le moins chargé (Indisponibles exclus) |

---

## Règles financières

### Matériaux (tissu, boutons, galon…)

Les matériaux achetés pour le client sont une **avance remboursée**, pas une charge :

```
Exemple : couture 1 000 F + galon 1 000 F → client paie 2 000 F
Bénéfice atelier = 1 000 F (la couture seulement)
Le galon circule mais ne reste pas dans la caisse atelier
```

**Formule du bénéfice (unique, appliquée partout) :**

```
Bénéfice = CA encaissé − commissions − charges d'exploitation
```

où les charges = dépenses validées (loyer, électricité, salaires…) + salaire secrétaire proratisé.

Les matériaux **ne sont jamais déduits** du bénéfice. Ils apparaissent en ligne séparée
pour information ("dont matériaux : X FCFA").

### Commissions

- Calculées sur `MontantCouture` uniquement (jamais sur les matériaux)
- Filtrées sur la **date de terminaison réelle** (`DateTerminee`) et non la date de RDV
- Une pièce rétrogradée (reprise) perd sa `DateTerminee` (remise à null)

### Indicateur "matériaux non remboursés"

Affiché dans DepensesView et TresorerieView : matériaux avancés dont la commande
n'est pas encore soldée. Alerte sur les créances clients en attente.

---

## Sauvegarde et restauration

### Emplacement des sauvegardes locales

```
C:\Users\<VotreNom>\AppData\Local\GestionCoutureApp\Backups\
```

### Restaurer une sauvegarde

1. Fermer l'application
2. Copier le fichier `.db` vers :
   ```
   C:\Users\<VotreNom>\AppData\Local\GestionCoutureApp\gestion_couture.db
   ```
3. Relancer l'application

---

## Lancer les tests

L'application doit être **fermée** avant de lancer les tests.

```powershell
cd c:\chemin\vers\GestionCoutureApp
dotnet test GestionCoutureApp.Tests
```

Résultat attendu : **141/141 réussis, 0 échec.**

---

## Limitations connues

### Application mono-poste

SQLite sur partage réseau est déconseillé pour des accès simultanés.

### Données non chiffrées au repos

Activer BitLocker sur le disque du poste est fortement recommandé.

### Webcam

La capture par webcam utilise AForge (API DirectShow). En cas de problème, utiliser
l'import de photo par fichier.

---

## Résumé des décisions techniques (Tâches 1-7)

| Décision | Justification |
|---|---|
| `DateTerminee` sur PieceCommande | Seule façon d'avoir une date de terminaison réelle, indépendante du RDV |
| Rattrapage historique via journal audit → DateFin | Avant T1, aucune date de terminaison n'existait. DateFin est une approximation conservative (à valider avec le propriétaire) |
| Matériaux = avance, jamais déduites du bénéfice | Règle métier confirmée par l'exemple galon du cahier des charges |
| MaterielSupplement ne crée pas de Depense auto | Confirmé après audit du code : aucun lien automatique n'existe |
| Secrétaire peut supprimer si aucun paiement + statut initial | Correction d'erreurs légitimes sans argent engagé |
| Flux strict + retour arrière Boss avec motif | Prévient les régressions accidentelles ; motif tracé dans l'audit |
| SuggererCouturier filtre uniquement Role=="Couturier" | Le Boss supervise, il n'est pas suggéré automatiquement à la création |
| Timer 60s dans AlertesView (non testable unitairement) | UI uniquement ; les tests portent sur la logique service (IAlerteService) |
| Historique statuts = journal d'audit existant (STATUT_PIECE_MODIFIE) | Évite une table dédiée ; le journal est déjà immuable et chaîné |

### Points à valider avec le propriétaire

1. **Rattrapage DateTerminee** : pour les pièces déjà Terminee avant T1, on utilise la date du journal d'audit quand disponible, sinon `Commande.DateFin`. Les commissions historiques peuvent donc être affectées à une période légèrement différente de la réalité.
2. **Suggestion couturier Boss exclu** : si le Boss couture régulièrement, l'inclure dans la suggestion (changer le filtre dans `SuggererCouturier`).
3. **Délai de répétition alertes (15 min)** : configurable dans `AlertesView.DelaiRepetitionNotif`. À ajuster selon les préférences.
4. **MaterielSupplement.CoutAchat** : si l'atelier applique une marge sur les matériaux (acheté 5 000 F, vendu 7 000 F), il faudra ajouter un champ `CoutAchat` pour calculer la vraie marge.
5. **Salaire secrétaire proratisé** : le calcul actuel divise le salaire mensuel par le nombre de jours du mois de début de période. Si la période chevauche deux mois, une amélioration est possible.

---

## Licence

Propriétaire — © 2026 Ilboudo Issouf. Tous droits réservés.  
Usage interne uniquement. Ne pas redistribuer sans autorisation écrite.
