# PROMPT V2 — Corriger et terminer le chantier performance (GestionCoutureApp)

> À copier tel quel dans une nouvelle session Claude Code, à la racine du dépôt.
> Ce prompt fait suite à `docs/PROMPT_PERF_GROS_VOLUME.md`. Un audit du 04/11/2026 a montré
> que ce premier prompt n'a été appliqué que partiellement, avec un bug bloquant. Tout ce qui
> suit a été **vérifié et mesuré** : ne pas refaire le diagnostic, corriger.

---

## 0. Règles de travail (non négociables)

1. **Ne jamais supprimer, nettoyer ou régénérer `bin\`** ni `bin\StressProfile-20261027\`.
   Pas de `dotnet clean`, pas de `rm -rf bin`. (Le 04/11 vers 12h05, `bin` a été effacé
   et la base de stress de 120 000 clients a été perdue.)
2. La base de stress de référence (120 000 clients, 120 000 commandes, 180 000 pièces,
   90 000 paiements, schéma **antérieur** à la migration `20261102000000`) est sauvegardée ici :
   `C:\Users\USER\GestionCoutureApp_StressBackup\gestion_couture_120k_20261102.db`
   **Ne jamais modifier ce fichier.** Pour chaque test, le recopier vers
   `bin\StressProfile-20261027\gestion_couture.db`, en supprimant d'abord les fichiers
   `-wal`/`-shm` éventuels, puis lancer :
   ```powershell
   $env:GESTIONCOUTUREAPP_DATA_DIR = (Join-Path (Get-Location).Path 'bin\StressProfile-20261027')
   $env:GESTIONCOUTUREAPP_DRIVE_BACKUP_FOLDER = 'Retouche Choco Stress Test 20261027'
   dotnet run --project GestionCoutureApp.csproj
   ```
3. Un commit par point ci-dessous, avec un message clair. Après chaque point :
   `dotnet build` (0 erreur) et `dotnet test` (**0 échec** : on ne déclare jamais
   « 100 % vert » sans avoir collé la ligne de résumé réelle de `dotnet test`).
4. **Les règles métier ne changent pas**, sauf mention explicite ci-dessous. Toute
   divergence avec l'ancien comportement doit être soit corrigée, soit listée et justifiée
   dans `docs/PERF_AVANT_APRES.md`.
5. Chaque affirmation « fait » dans `docs/PERF_AVANT_APRES.md` doit être accompagnée
   d'une preuve : temps mesuré en ms, sortie de `EXPLAIN QUERY PLAN`, ou nom du test.
6. Rappel des règles du prompt V1 : aucune table non bornée en mémoire ; listes affichées
   avec `ORDER BY … LIMIT` en SQL ; compteurs en `COUNT` SQL ; sommes en `SUM` SQL
   (`(double)` puis reconversion en `decimal`) ; pas de `.Date`, `ToLower()` ni calcul sur
   colonne dans un `Where` ; `AsNoTracking()` + projection DTO ; aucun accès à la base sur
   le thread UI ; listes WPF virtualisées.

---

## 1. 🔴 BLOQUANT — La migration `AjoutColonnesDenormalisees` n'est jamais appliquée

**Constat.** `Migrations/20261102000000_AjoutColonnesDenormalisees.cs` n'a ni `[DbContext]`
ni `[Migration]`, donc EF Core l'ignore. Dans ce projet, ce type de migration doit être
appliqué manuellement dans `App.cs` via `AppliquerSi("…", c => …)` (voir les blocs existants
vers les lignes 777 à 1201), mais elle n'y figure pas. **Preuve** : la base créée le 04/11 à
12h21 par la version compilée à 12h20 n'a pas les colonnes `DateRdvEffective` ni
`CommandeActive`. Conséquence probable : l'INSERT d'une pièce échoue (« no such column »),
donc **la création de commande est impossible**, et Alertes / Dashboard s'affichent vides
(exceptions avalées par les `catch`). Les tests ne l'ont pas vu parce que
`TestDbHelper` utilise `EnsureCreated()`, qui construit le schéma depuis le modèle C#.
`20261018000000_IndexesPerformance` est dans le même cas : vérifier et corriger aussi.

**À faire.**
- Ajouter dans `App.cs` un bloc `AppliquerSi("20261102000000_AjoutColonnesDenormalisees", …)`
  idempotent : `AjouterCol` pour les deux colonnes, backfill SQL (mêmes requêtes que la
  migration), puis création des index (section 2), puis `ANALYZE`. Faire de même pour
  `20261018000000_IndexesPerformance` si elle n'est pas appliquée.
- **Ordre** : le bloc doit s'exécuter AVANT tout accès EF qui utilise ces colonnes.
- **Format des dates** : le backfill doit produire exactement le format texte qu'écrit
  EF Core SQLite (`yyyy-MM-dd HH:mm:ss`, avec fraction seulement si elle existe), sinon
  les comparaisons de chaînes `DateRdvEffective < @maintenant` sont fausses. Ajouter un
  test qui compare, pour 1 000 pièces, la valeur backfillée et
  `DateRdvHelper.CalculerDateRdvEffective(...)`.
- Le backfill prend ~9 s pour 180 000 pièces (mesuré), donc plusieurs minutes à 3 M :
  l'exécuter **une seule fois**, dans une transaction, avec un message visible
  (« Mise à jour de la base, veuillez patienter… ») au lieu d'une fenêtre figée.
- Mettre à jour `LogDiagnosticSchema` / la validation de schéma de `App.cs` pour exiger
  ces deux colonnes et ces index.
- **Test obligatoire** `MigrationReelleTests` : créer une base SQLite fichier avec le
  schéma **ancien** (copier un petit extrait de la base de stress, ou rejouer les migrations
  jusqu'à `20261016120000`), exécuter le **vrai chemin de démarrage** (méthode d'`App.cs`
  extraite dans une classe testable, par exemple `SchemaUpgrader`), puis vérifier :
  colonnes présentes, index présents, valeurs backfillées correctes, et qu'un
  `CommandeService.Ajouter(...)` réussit. **Interdit d'utiliser `EnsureCreated()` dans ce test.**

**Acceptation.** Recopier la base de référence, lancer l'app, créer une commande avec
succès, puis vérifier `PRAGMA table_info(PiecesCommande)` et la liste des index.

---

## 2. 🔴 Index mal ordonnés : les sections d'alertes restent lentes (mesuré)

**Constat** (base de 120 000 clients, migration appliquée sur une copie) :

| Requête | Index actuel `(CommandeActive, Statut, DateRdvEffective)` | Index corrigé |
|---|---|---|
| Section Retards, page de 50 (`Statut IN ('A faire','En cours')` + `ORDER BY DateRdvEffective LIMIT 50`) | **1 300 à 1 900 ms**, plan : `USE TEMP B-TREE FOR ORDER BY` (tri des 89 000 lignes jointes) | **2 ms** avec `(CommandeActive, DateRdvEffective, Statut)` |
| Badge Retards `COUNT(DISTINCT IdCommande)` | **940 ms** | **75 ms** avec l'index couvrant `(CommandeActive, Statut, DateRdvEffective, IdCommande)` |

À 3 M de pièces, sans correction, chaque section prendrait plus de 20 s.

**À faire.**
- Créer `IX_PiecesCommande_Active_DateRdv_Statut ON PiecesCommande(CommandeActive, DateRdvEffective, Statut)`
  pour les listes triées par date.
- Remplacer `IX_PiecesCommande_CommandeActive_Statut_DateRdv` par l'index couvrant
  `(CommandeActive, Statut, DateRdvEffective, IdCommande)` pour les compteurs.
- Index pour les stats couturier / qualité :
  `(IdCouturier, Statut, DateRdvEffective)` (remplace `(IdCouturier, Statut)`).
- Déclarer ces index dans `ApplicationDbContext` (pour `EnsureCreated` des tests) **et**
  dans le bloc `App.cs` (pour les vraies bases). Supprimer les index devenus inutiles.
- Pour **chaque** requête des services Alertes, RetardAlerte, Dashboard et Retours :
  récupérer le SQL généré (`ToQueryString()`), exécuter `EXPLAIN QUERY PLAN` sur la base
  de stress, et coller le plan dans `docs/PERF_AVANT_APRES.md`. **Interdit** :
  `SCAN PiecesCommande`, `SCAN Commandes`, `SCAN Clients`, et `USE TEMP B-TREE FOR ORDER BY`
  sur une requête paginée.
- `ObtenirEcheancesUrgentesAsync` du Dashboard (`ORDER BY CASE WHEN DateRdvEffective < now …`)
  n'est pas indexable : la remplacer par deux requêtes indexées (les retards
  `ORDER BY DateRdvEffective LIMIT 30`, puis les échéances à venir `LIMIT 30`), fusionnées
  en mémoire sur ces ≤ 60 lignes.
- Ajouter dans `Perf_BudgetTests` un test qui exécute chaque requête d'écran sur ≥ 200 000
  pièces, dont ≥ 80 000 en retard, et exige **< 100 ms** par requête paginée et
  **< 150 ms** par compteur.

---

## 3. 🟠 Alertes RDV : restaurer les fonctions supprimées (régressions)

La refonte de `Views/AlertesView.cs` (1 045 → 408 lignes) et `AlerteService` a gardé la
performance mais a supprimé des fonctions. Comportement de référence : `git show 2c74da8:Views/AlertesView.cs`
et `git show 2c74da8:Services/AlerteService.cs`. Restaurer, en SQL borné :

1. **Alertes de Retours** (Étape 6) : avant, `ObtenirAlertesRetours()` alimentait les
   sections (retour en retard → Retards ; retour « Termine » → Prêts ; RDV de reprise
   aujourd'hui → Aujourd'hui ; sous 7 jours → Imminentes), et aussi la section Risque de
   retard (`EstAlertRetour`, `NumeroRetour`, affichage « 🔄 RETOUR … »). Aujourd'hui, les
   retours n'apparaissent plus nulle part. Les réintégrer dans chaque section : requête
   `Retours` bornée (`Take(taille)`) + `COUNT`, fusion des deux pages triées par date, et
   compteurs = pièces + retours. Ajouter un index `Retours(EstAnnule, Statut, DateRdvReprise)`.
2. **Notifications urgentes** (`VerifierNotificationsUrgentes` : son `SystemSounds.Exclamation`,
   popup « ⚠️ Alertes RDV urgentes », anti-répétition `_dernieresNotifications`) :
   supprimées, à restaurer avec une requête dédiée bornée à `Take(20)` sur les urgentes
   (RDV dépassé ou dans l'heure), pièces ET retours.
3. **Règle « stagnant »** (`PasEncorePriseEnCharge`) : une pièce « A faire » dont la moitié
   du délai dépôt → RDV est écoulée doit apparaître dans Imminentes même si son RDV est à
   plus de 7 jours. Ancienne formule : `maintenant >= dateDepot + (dateRdv - dateDepot)/2`,
   avec `dateDepot = DateDebut.Date + HeureDebut`. Rendre la condition indexable : ajouter
   une colonne persistée `DateMiParcours` maintenue comme `DateRdvEffective` (même
   `DateRdvHelper`, même backfill), ou une autre solution SQL prouvée par `EXPLAIN`.
4. **Pièce terminée dont le RDV est dépassé** : avant, elle allait dans **Retards**
   (triée en tête, bouton « Relancer le client ») ; aujourd'hui elle va dans Prêts.
   Rétablir l'ancienne priorité Retards > Prêts > Aujourd'hui > Imminentes, avec des
   conditions SQL exclusives.
5. **Messages WhatsApp** : `BtnWhatsApp_Click` envoie désormais « votre commande est
   prête » pour toutes les sections. Restaurer l'ancien comportement :
   `_whatsAppService.NotifierCommandePreteAsync(commande)` pour Prêts,
   `NotifierRappelRdvAsync(commande)` pour Aujourd'hui / Retards (voir l'ancien code
   vers la ligne 487), avec gestion d'erreur et du numéro manquant.
6. **Modale de détails** : restaurer la liste des pièces (`ListeModalPieces`) avec statut,
   couturier et RDV de chaque pièce. Requête par `IdCommande` à l'ouverture.
7. **Bouton « Voir plus » / pagination** : `_pageRetards`, `_pagePrets`, etc. ne sont jamais
   incrémentés et `txtTotal` est passé à `null`, donc on ne voit jamais au-delà de 50.
   Ajouter, pour chaque section, « X affichés sur Y » et un bouton « Voir plus » qui
   **ajoute** la page suivante (`ObservableCollection`, pas de rechargement complet).
8. **Pagination par commande, pas par pièce** : `ObtenirSectionAsync` pagine des pièces
   puis groupe par commande. Une commande peut donc être coupée entre deux pages, et
   `TotalCount` (pièces) ne correspond pas au badge (commandes distinctes). Paginer sur
   les `IdCommande` distincts (sous-requête `SELECT IdCommande … GROUP BY IdCommande
   ORDER BY MIN(DateRdvEffective) LIMIT 50`), puis charger les pièces de ces 50 commandes.
   `TotalCount` = nombre de commandes = badge.
9. **Badge Risque de retard** : `ObtenirResumeAsync` ne retire pas les cibles déjà
   prévenues (`SuivisAlerteRetard`, `NbPreventions > 0`, même RDV à ±1 min), alors que la
   liste les retire. Utiliser **exactement** le même prédicat que
   `ObtenirAlertesRisqueRetardPageAsync` (le factoriser dans une méthode partagée).
10. **N+1 dans `ObtenirAlertesRisqueRetardPageAsync`** : une requête `SuivisAlerteRetard`
    par élément de la page. Remplacer par une seule requête
    `WHERE (TypeCible, IdCible) IN (…50 clés…)` ou une jointure dans la projection.
11. **Formule d'heure par défaut** : `RetardAlerteService` utilisait `HeureFin ?? HeureDebut`,
    unifiée en `HeureFin ?? 17:00`. Garder 17:00, mais ajouter un test qui fige ce choix et
    le documenter dans `PERF_AVANT_APRES.md` comme changement métier volontaire.
12. **Annulation réelle** : passer le `CancellationToken` jusqu'aux `ToListAsync(ct)` /
    `CountAsync(ct)` dans les services ; le timer de 60 s ne doit pas lancer un chargement
    si `_chargementEnCours` est vrai.

**Test de non-régression obligatoire** : sur un jeu de 500 pièces et 50 retours couvrant
tous les cas (chaque statut × RDV passé / aujourd'hui / sous 7 j / plus tard / stagnant,
RDV d'exception, commande supprimée / annulée, suivi déjà prévenu), comparer section par
section les `IdCommande` / `IdRetour` obtenus avec l'**ancienne implémentation** (copiée
telle quelle dans le projet de tests depuis `2c74da8` sous le nom `AncienAlerteCalcul`) et
avec la nouvelle. Les ensembles doivent être identiques, aux changements documentés près.

---

## 4. 🟠 Dashboard : retards par couturier encore en mémoire

`Views/DashboardView.cs` vers la ligne 592 (`retardsData`) projette encore toutes les paires
`(IdCouturier, IdCommande)` en retard (89 000 lignes) puis fait `Distinct` / `GroupBy` en
mémoire. Remplacer par une seule requête SQL :
`GROUP BY IdCouturier` → `COUNT(DISTINCT IdCommande)`, filtrée sur `CommandeActive`,
`Statut <> 'Livree'` et `DateRdvEffective < @debutJour` (vérifier que la règle « retard »
reste identique à l'ancienne, qui utilisait `Commande.DateFin < aujourd'hui`).
Utiliser `CommandeActive` au lieu des jointures sur `Commandes` dans **toutes** les
requêtes du Dashboard (compteurs, couturiers, `CompterPiecesAAttribuer`).
Le timer de 60 s ne doit rien faire si `!IsVisible` ou si un rafraîchissement est en cours.

---

## 5. 🟠 Tests en échec : `TresorerieService.CalculerBilan` (14,4 s)

`dotnet test` : **9 échecs sur 397** (TearDown de `Perf_ChargeTests` :
`'TresorerieService.CalculerBilan' dépasse 10 s (14400 ms)`). Le bilan affirmait pourtant
« 100 % vert ». Cause : `CalculerBilan` charge toutes les commandes de la période avec
`Include(Pieces)`, `Include(MaterielSupplements)` et `Include(Paiements)` (produit
cartésien), puis calcule en mémoire. Réécrire en agrégats SQL : `SUM` par `IdCommande`
sur chacune des trois tables (trois requêtes `GROUP BY`), assemblage en mémoire sur des
dictionnaires, ou mieux, directement les totaux nécessaires au bilan. Mêmes résultats au
franc près (test de comparaison avec l'ancienne méthode). Objectif : < 300 ms.
Même traitement pour `CalculerRepartitionCa` et toute méthode de `TresorerieService` /
`CommissionService` qui fait `Include` + calcul en mémoire.
`CommissionService` : 8 filtres `.Date` (lignes 56, 57, 159, 160…) à remplacer par des
bornes `>= debut` / `< finExclusive`.

---

## 6. 🟠 Clients : recherche et détection de doublons (critique à 2 M de clients)

`Services/ClientService.cs` :
- ligne ~69 : **à chaque création de client**, `context.Clients.AsEnumerable()` charge et
  normalise TOUS les clients pour détecter les doublons ;
- ligne ~136 : idem pour retrouver le client en conflit de téléphone ;
- ligne ~212 `Rechercher` et ~240 (recherche avec accents) : chargement de toute la table.

**À faire.**
- Colonne persistée `Clients.NomRecherche` = `TexteHelper.NormaliserPourRecherche($"{Nom} {Prenom}")`,
  maintenue à chaque ajout / modification, backfillée par le bloc `App.cs` (calcul en C#
  par lots de 5 000, dans une transaction, car SQLite ne sait pas retirer les accents),
  avec un index sur cette colonne.
- Doublon : `WHERE NomRecherche = @cle` (index) ; conflit de téléphone :
  `WHERE Telephone = @tel` (index unique existant).
- Recherche : préfixe indexé (`NomRecherche LIKE @cle || '%'`, ou `StartsWith`), plus un
  mot de prénom via une colonne `PrenomNomRecherche` si nécessaire, téléphone par préfixe ;
  `Take(50)`, débounce de 300 ms, annulation de la requête précédente. Si la recherche
  « contient » est indispensable, utiliser une table **FTS5** (`trigram`), maintenue par
  des triggers.
- Appliquer la même recherche indexée à `CommandesView`, `PaiementsView`, `StatutView`
  et `RetourService.ObtenirPageAsync` (qui utilise encore `Include` et `ToLower().Contains()`
  sur 6 colonnes : passer en projection DTO, sans `Include`).
- Test : 200 000 clients, création d'un client < 50 ms, recherche < 100 ms.

---

## 7. 🟡 Retours : petits écarts

- `ChargerPerformanceQualiteAsync` charge désormais tous les employés (y compris inactifs
  et secrétaires) au lieu de `Statut == "Actif" && Role in (Couturier, Boss)` : rétablir le filtre.
- La période utilise maintenant `DateRdvEffective` au lieu de `Commande.DateFin` :
  documenter ce changement (inclut les RDV d'exception) ou revenir à la règle d'origine.
- `NbReprises` (`IdCouturierRepriseEffectif`) doit toujours être calculé, par `GROUP BY` SQL.
- `MettreAJourBadgesAsync` applique un filtre texte différent de `ObtenirPageAsync` :
  utiliser le même prédicat, factorisé.

---

## 8. Mesure finale (obligatoire, pas de case cochée sans chiffres)

1. Sur la base de référence de 120 000 clients (copie fraîche) : lancer l'app, mesurer
   via les logs `[PERF]` le premier affichage et le chargement complet de **chaque écran**
   (Dashboard, Alertes RDV, Commandes, Clients, Paiements, Retours, Statut, Commissions,
   Dépenses, Dashboard couturier), plus la création d'un client et d'une commande.
2. Générer une base de **2 000 000 clients / 3 000 000 pièces** avec `StressDataGenerator`
   (dans un **autre** dossier, par exemple `C:\Users\USER\GestionCoutureApp_Stress2M\`, jamais dans `bin`),
   et refaire les mêmes mesures.
3. Remplir `docs/PERF_AVANT_APRES.md` avec un tableau par écran : avant / après 120k /
   après 2M, et le plan `EXPLAIN` de chaque requête.

**Critères d'acceptation** (2 M clients) : premier affichage < 1 s par écran, chargement
complet < 2 s, aucune requête paginée > 100 ms, aucun compteur > 150 ms, mémoire du
processus < 300 Mo après 10 minutes de navigation, aucun gel du thread UI > 100 ms,
`dotnet test` = 0 échec (coller la ligne de résumé), et toutes les fonctions de la
section 3 vérifiées à la main (en listant ce qui a été cliqué).

Procéder dans l'ordre 1 → 8. Ne pas passer à l'étape suivante tant que la précédente
ne respecte pas ses critères. À la fin, fournir un rapport honnête : ce qui est fait (avec
preuve), ce qui ne l'est pas, et pourquoi.
