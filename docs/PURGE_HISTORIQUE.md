# Purge de l'historique Git — Actions manuelles requises

> **À exécuter par le propriétaire du dépôt UNIQUEMENT.**  
> Ces commandes réécrivent l'historique. Tout collaborateur devra re-cloner après l'opération.

---

## Contexte

Deux types de fichiers ont été commités par erreur dans le passé et doivent être
retirés de **tout** l'historique (pas seulement de HEAD) :

| Fichier / Dossier | Raison |
|---|---|
| `client_secret.json` (et variantes `client_secret*.json`) | Secret OAuth Google — à révoquer immédiatement |
| `TestsBuildOut/` | Binaires compilés — inutiles dans le dépôt |

Le `git rm --cached` effectué en Phase 0 retire ces fichiers de HEAD mais **les anciens
commits les contiennent encore**. `git filter-repo` est nécessaire pour une purge complète.

---

## Prérequis

```powershell
# Installer git-filter-repo (Python requis)
pip install git-filter-repo
# Ou via pipx (recommandé) :
pipx install git-filter-repo
```

Vérifier :
```powershell
git filter-repo --version
```

---

## Étape 1 — Révoquer le secret OAuth AVANT la purge

> ⚠️ **Faire ceci EN PREMIER.** La purge de l'historique ne protège pas les accès
> déjà effectués avec le secret compromis.

1. Ouvrir [Google Cloud Console](https://console.cloud.google.com/)
2. Aller dans **APIs & Services → Credentials**
3. Localiser le client OAuth 2.0 correspondant à GestionCoutureApp
4. Cliquer **Delete** (ou **Disable** si une suppression immédiate est trop risquée)
5. Créer un **nouveau** client OAuth 2.0 :
   - Type : **Application de bureau** (Desktop app)
   - Nom : `GestionCoutureApp-YYYY` (année courante)
   - Télécharger le nouveau `client_secret.json`
6. Placer ce nouveau fichier **uniquement** dans :
   ```
   %LOCALAPPDATA%\GestionCoutureApp\client_secret.json
   ```
   **Ne jamais le mettre dans le dossier du projet.**

---

## Étape 2 — Purge avec git filter-repo

```powershell
# Dans le dossier racine du dépôt :
cd C:\Users\USER\GestionCoutureApp

# Purger client_secret.json et toutes ses variantes
git filter-repo --path-glob "client_secret*.json" --invert-paths --force

# Purger le dossier TestsBuildOut/
git filter-repo --path "TestsBuildOut" --invert-paths --force
```

> `--force` est nécessaire parce que le dépôt n'est pas une copie fraîche.

---

## Étape 3 — Vérifier la purge

```powershell
# Ne doit rien retourner :
git log --all --full-history -- "client_secret*.json"
git log --all --full-history -- "TestsBuildOut/"

# Vérifier que le .gitignore est bien propre :
git grep -n "client_secret" HEAD
```

---

## Étape 4 — Forcer la mise à jour du dépôt distant

> ⚠️ Cette opération réécrit l'historique du dépôt distant. Toute personne
> ayant cloné le dépôt devra **re-cloner** (pas de pull/merge possible).

```powershell
# Ajouter à nouveau le remote si filter-repo l'a supprimé :
git remote add origin <url-du-depot>

# Force push de toutes les branches et tags :
git push origin --force --all
git push origin --force --tags
```

---

## Étape 5 — Actions post-purge pour les collaborateurs

Communiquer à chaque personne ayant accès au dépôt :

```powershell
# Re-cloner (ne pas faire de git pull sur un ancien clone) :
cd C:\Users\<nom>
Remove-Item -Recurse -Force GestionCoutureApp
git clone <url-du-depot>
```

---

## Vérification finale

```powershell
# Ces deux commandes ne doivent retourner AUCUN résultat :
git grep -nE "client_secret" -- "*.json" "*.cs" "*.md" "*.txt"
git ls-files | Select-String "TestsBuildOut"
```

---

*Document créé lors de la Phase 0 des corrections de sécurité — $(Get-Date -Format 'yyyy-MM-dd')*
