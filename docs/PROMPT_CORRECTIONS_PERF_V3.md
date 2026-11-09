# PROMPT V3 — Finir le chantier performance (GestionCoutureApp)

> À copier tel quel dans une nouvelle session Claude Code, à la racine du dépôt.
> Suite de `docs/PROMPT_PERF_GROS_VOLUME.md` (V1) et `docs/PROMPT_CORRECTIONS_PERF_V2.md` (V2).
> L'audit du 09/11/2026 a validé l'essentiel de la V2 (migration réelle, index, fonctions
> d'alertes restaurées, doublons clients indexés). Il reste **7 points précis**, tous mesurés
> ci-dessous. Ne pas refaire le diagnostic : corriger, mesurer, prouver.

---

## 0. Règles de travail (inchangées, non négociables)

1. **Ne jamais toucher** à `bin\`, `bin\StressProfile-20261027\` ni à la sauvegarde de
   référence `C:\Users\USER\GestionCoutureApp_StressBackup\gestion_couture_120k_20261102.db`.
   Pas de `dotnet clean`. Pour un test manuel : recopier la sauvegarde vers
   `bin\StressProfile-20261027\gestion_couture.db` (supprimer d'abord `-wal`/`-shm`).
2. Si l'application est ouverte, elle verrouille `bin\Debug`. Ne pas la fermer de force :
   compiler et tester avec `-p:BaseOutputPath=C:/Users/USER/AppData/Local/Temp/gca_build/`.
3. Un commit par point. Après chaque point : `dotnet build` (0 erreur) et `dotnet test`
   complet (pas seulement `TestCategory!=Perf`). Coller la **ligne de résumé réelle**
   dans `docs/PERF_AVANT_APRES.md`.
4. Règles métier inchangées. Pour chaque méthode réécrite, garder l'ancienne version dans
   le projet de tests (`Ancien…`) et ajouter un test qui compare les résultats sur un jeu
   couvrant tous les cas : **même résultat au franc près**, mêmes identifiants.
5. Pour chaque requête modifiée : coller dans `docs/PERF_AVANT_APRES.md` le SQL généré
   (`ToQueryString()`), le résultat de `EXPLAIN QUERY PLAN` et le temps mesuré sur la base
   120k. **Interdits** dans un plan : `SCAN Clients`, `SCAN PiecesCommande`, `SCAN Commandes`,
   `SCAN Paiements`, `USE TEMP B-TREE FOR ORDER BY` sur une requête paginée.
6. Règles V1/V2 toujours valables : rien de non borné en mémoire, `COUNT`/`SUM`/`GROUP BY`
   en SQL, `AsNoTracking()` + projection, pas de `Include` pour un calcul, rien sur le
   thread UI.

---

## État mesuré au 09/11 (base 120k, après migration)

`dotnet test` : **Échoué! - échec : 3, réussite : 396, total : 399** :
- `B06_RetardAlerteService_ObtenirAlertesRisqueRetard_Budget` : **1 557 ms** (budget 300 ms) ;
- `B07_RetardAlerteService_ObtenirStatsDashboard_Budget` : **310 ms** (budget 300 ms) ;
- TearDown `Perf_ChargeTests` : `CommissionService.CalculerApercu` **13 364 ms** (seuil 10 s).
- `TresorerieService.CalculerBilan` : **~8 s** (passe le seuil historique de 10 s, mais
  l'objectif est < 300 ms).

Requêtes des écrans (base réelle 120k) :

| Requête | Temps | Plan |
|---|---|---|
| Badge « Risque » (`ObtenirResumeAsync`) | 569 ms | sous-requête corrélée avec `CASE`, index de `SuivisAlerteRetard` inutilisable |
| Badge « Imminentes » | 273 ms | `MULTI-INDEX OR` + `TEMP B-TREE FOR count(DISTINCT)` |
| Badge « Retards » | 126 ms | `TEMP B-TREE FOR count(DISTINCT)` |
| Page d'une section (`GROUP BY IdCommande ORDER BY MIN(date) LIMIT 5`) | 145 ms | agrège TOUTES les pièces de la section à chaque page |
| Recherche clients (`StartsWith` → `LIKE`) | 36 à 103 ms | **`SCAN Clients`** |

À 2 M de clients (×17), ces temps deviennent 2 à 15 s. Objectif : tout < 100 ms.

---

## 1. Risque de retard : rendre le filtre « déjà prévenu » indexable

**Fichiers** : `Services/RetardAlerteService.cs` (`ObtenirAlertesRisqueRetardPageCoreAsync`,
`ObtenirStatsDashboardAsync`), `Services/AlerteService.cs` (`ObtenirResumeAsync`, `nbRisque`).

**Cause** : le `NOT EXISTS` compare `s.TypeCible == (p.RendezVousException.HasValue ? "Piece" : "Commande")`
et `s.IdCible == (… ? p.IdPieceCommande : p.IdCommande)`. Avec un `CASE`, SQLite ne peut pas
utiliser l'index unique `(TypeCible, IdCible)`. Il évalue donc la sous-requête ligne par ligne.

**À faire** :
- Remplacer par **deux branches sans `CASE`** :
  `(p.RendezVousException == null && !Suivis.Any(s => s.TypeCible == "Commande" && s.IdCible == p.IdCommande && …))`
  `|| (p.RendezVousException != null && !Suivis.Any(s => s.TypeCible == "Piece" && s.IdCible == p.IdPieceCommande && …))`.
  Vérifier dans le plan : `SEARCH s USING INDEX IX_SuivisAlerteRetard_TypeCible_IdCible (TypeCible=? AND IdCible=?)`.
- Remplacer la fenêtre `DateRdvTraite BETWEEN DateRdvEffective ± 1 min` (calcul
  `datetime(...)` sur la colonne) par une égalité exacte. Pour ça, enregistrer
  `DateRdvTraite` tronquée à la minute **et** comparer avec `DateRdvEffective` tronquée à la
  minute (ou stocker `DateRdvEffective` déjà tronquée à la minute, ce qui est le cas en
  pratique : vérifier et documenter). Si un ajustement de données est nécessaire, l'ajouter
  au bloc de migration d'`App.cs`.
- **Factoriser** ce prédicat dans une seule méthode (`Expression<Func<PieceCommande,bool>>`)
  utilisée par la liste, le badge et les stats du Dashboard. Les trois doivent donner le
  même nombre.
- Supprimer le `Distinct()` sur `(IdCommande, IdPieceCommande, DateRdvEffective)` de `nbRisque` :
  `IdPieceCommande` est déjà unique, donc un `COUNT(*)` suffit.
- Ajouter à `StressDataHelper` / aux tests de budget **≥ 5 000 lignes `SuivisAlerteRetard`**,
  pour mesurer le cas réel (aujourd'hui la base 120k n'en a aucune).

**Acceptation** : B06 et B07 passent ; liste « Risque » < 50 ms ; badge < 100 ms sur 120k.

---

## 2. Badges d'alertes : < 100 ms au total, même à 2 M

**Fichier** : `Services/AlerteService.cs` → `ObtenirResumeAsync` (appelé par `AlertesView`
**et** toutes les 60 s par `MainWindow.ActualiserBadgeAlertesAsync`).

**À faire** :
- `Imminentes` : séparer le `OR` en deux `COUNT` indexés (fenêtre 7 j via
  `(CommandeActive, DateRdvEffective, Statut)`, stagnantes via
  `(CommandeActive, Statut, DateMiParcours)`), puis retirer en SQL les commandes comptées
  deux fois (`UNION` de deux sous-requêtes d'`IdCommande`, puis `COUNT`).
- `COUNT(DISTINCT IdCommande)` sur des dizaines de milliers de lignes reste linéaire. Choisir
  **une** solution et la justifier :
  (a) **compteurs agrégés par commande** : colonnes dénormalisées sur `Commandes`
  (`DateRdvMin`, `NbPiecesNonTerminees`, `NbPiecesTerminees`, `CommandeActive`), maintenues
  par `CommandeService` / `RetardAlerteService` comme `DateRdvEffective`, avec backfill dans
  `App.cs`, puis `COUNT(*)` indexé sur `Commandes` (sans `DISTINCT`) ;
  ou (b) afficher « 999+ » au-delà de 999 (`SELECT COUNT(*) FROM (SELECT DISTINCT … LIMIT 1000)`),
  ce qui borne le coût quel que soit le volume.
  La solution (a) est recommandée : elle sert aussi au point 3.
- `MainWindow` : ne pas lancer un rafraîchissement si le précédent n'est pas fini ; ne rien
  faire si la fenêtre est minimisée ; ne pas recalculer si `AlertesView` vient de le faire
  (réutiliser son dernier résumé pendant 60 s, cache partagé dans `AlerteService`).
- Tous les compteurs de `ObtenirResumeAsync` sont lancés aujourd'hui **en série** sur un
  même `DbContext`. Les regrouper en **une seule requête SQL** (`SELECT (SELECT COUNT…), (SELECT COUNT…), …`)
  ou les paralléliser avec un contexte par requête.

**Acceptation** : `ObtenirResumeAsync` < 100 ms sur 120k, mesuré < 150 ms sur la base 2M.

---

## 3. Sections d'alertes : pagination qui ne relit pas toute la section

**Fichier** : `Services/AlerteService.cs` → `ObtenirSectionAsync`.

**Bugs et causes** :
- `.Skip((page - 1) * taille).Take(page * taille)` : la page 2 renvoie 2× `taille` commandes,
  la page 3 en renvoie 3×. Corriger en `.Take(taille)`. Ajouter un test sur les pages 1, 2 et 3.
- `GROUP BY IdCommande … ORDER BY MIN(DateRdvEffective)` agrège toutes les pièces de la
  section (89 000 en retard) à chaque page, et à chaque fois le `COUNT(DISTINCT)` aussi.

**À faire** : avec les colonnes du point 2(a), paginer **directement sur `Commandes`** :
`WHERE CommandeActive AND <condition de section sur les colonnes agrégées> ORDER BY DateRdvMin LIMIT @taille OFFSET @skip`,
avec un index adapté à chaque section (par exemple `(CommandeActive, DateRdvMin)`), puis
charger les pièces des seules commandes de la page. Si le point 2(a) n'est pas retenu,
utiliser une pagination par curseur (keyset : `WHERE (DateRdvEffective, IdPieceCommande) > (@dernier…)`)
au lieu de `OFFSET`.
Vérifier que les règles d'affectation (Retards > Prêts > Aujourd'hui > Imminentes,
stagnantes, terminées en retard dans Retards) restent identiques au test de
non-régression `AncienAlerteCalcul` de la V2.

**Acceptation** : page 1, page 2 et page 100 < 50 ms chacune sur 120k, plan sans
`TEMP B-TREE`.

---

## 4. Recherche clients : utiliser vraiment l'index

**Fichier** : `Services/ClientService.cs` (`Rechercher` ~l.212, `RechercherPageAsync` ~l.223),
et toute autre recherche par `StartsWith` (Commandes, Paiements, Statut, Retours).

**Cause (prouvée)** : EF traduit `StartsWith` en `LIKE 'abc%'`. SQLite n'utilise un index
pour `LIKE` que si l'index est en `COLLATE NOCASE`, ce qui n'est pas le cas de
`IX_Clients_NomRecherche`. De plus, `(c.Telephone ?? "")` génère un `COALESCE` qui empêche
l'index, et le `OR` sur trois colonnes force un `SCAN Clients`.
Mesure : `LIKE` = `SCAN`, alors que l'intervalle `NomRecherche >= 'abc' AND NomRecherche < 'abc\uffff'`
= `SEARCH USING COVERING INDEX`, **13 ms**.

**À faire** :
- `NomRecherche` et `PrenomNomRecherche` sont déjà normalisées en minuscules sans accents :
  remplacer `StartsWith(cle)` par un **intervalle**
  (`string.Compare(c.NomRecherche, cle) >= 0 && string.Compare(c.NomRecherche, cleFin) < 0`,
  avec `cleFin = cle + '\uffff'`, ou `EF.Functions.Collate` / SQL brut paramétré si EF ne
  traduit pas proprement).
- Téléphone : supprimer `?? ""` (`c.Telephone != null && intervalle`) et utiliser un index
  sur `Telephone` adapté à l'intervalle.
- Remplacer le `OR` par **trois requêtes indexées** (nom, prénom-nom, téléphone), chacune
  `Take(taille * page)`, fusionnées et dédoublonnées sur l'`IdClient` en mémoire (≤ 150 lignes),
  puis triées. Le total affiché : `COUNT` de chacune, avec « environ » si un dédoublonnage
  est possible, ou un `UNION` SQL compté si c'est assez rapide (mesurer).
- La base de stress a des noms tous identiques (`nomclient777 prenom777`) : ajouter au
  générateur et aux tests des noms réalistes (avec accents, préfixes communs) pour mesurer.

**Acceptation** : plan `SEARCH … USING INDEX` pour chaque branche ; recherche < 50 ms sur
120k et < 100 ms sur 2M ; mêmes résultats que l'ancienne recherche « commence par ».

---

## 5. Commissions : `CalculerApercu` (13,4 s)

**Fichier** : `Services/CommissionService.cs` → `CalculerApercu` (~l.33).

**Cause** : charge les pièces terminées/livrées de la période **avec suivi des entités**
(pas d'`AsNoTracking`) et 5 `Include`/`ThenInclude` (`MaterielSupplements`, `Commande`,
`Commande.Paiements`, `Commande.Pieces`, `Commande.MaterielSupplements`) : produit
cartésien. Puis `foreach (couturier)` avec `Where(...).ToList()` et `PartEncaisseeDeLaPiece`
en mémoire.

**À faire** :
- Écrire en SQL les agrégats nécessaires, sans `Include` :
  1. par pièce : `MontantCouture`, `IdCouturier`, `IdCommande`, somme des matériaux de la
     pièce (`GROUP BY IdPieceCommande`) ;
  2. par commande concernée : total facturé (pièces + matériaux) et total encaissé non
     annulé (`GROUP BY IdCommande`), avec une **sous-requête** sur les commandes de la
     période, **pas** une liste d'IDs matérialisée envoyée en paramètre ;
  3. retours non résolus : `NOT EXISTS` en SQL au lieu de `Contains` en mémoire.
- Reproduire `PartEncaisseeDeLaPiece` (prorata de l'encaissé) **à l'identique** sur ces
  agrégats, puis `GROUP BY IdCouturier`.
- Bornes de dates exclusives et indexables (`DateTerminee >= debut AND < finExclusive`),
  index `(Statut, DateTerminee, IdCouturier)` si le plan le demande.
- Test de comparaison avec l'ancienne méthode sur un jeu couvrant : paiements partiels,
  annulés, matériaux, plusieurs pièces par commande, retours non résolus, filtre couturier.

**Acceptation** : < 300 ms sur 120k ; TearDown `Perf_ChargeTests` vert.

---

## 6. Trésorerie : `CalculerBilan` (~8 s)

**Fichier** : `Services/TresorerieService.cs` → `CalculerBilan` (~l.235) et
`CalculerRepartitionCa`.

**Cause** : les trois agrégats (`PiecesCommande`, `MaterielsSupplements`, `Paiements`) filtrent
avec `idsPeriode.Contains(p.IdCommande)`, où `idsPeriode` est une liste d'IDs **matérialisée en
mémoire** (jusqu'à 120 000 valeurs) renvoyée à SQLite en paramètre. Puis une boucle
`foreach` sur toutes les commandes de la période.

**À faire** :
- Remplacer `idsPeriode.Contains(...)` par une **jointure / sous-requête** sur `Commandes`
  filtrée par la période (`DateDebut >= debut AND DateDebut < finExclusive`, plus les
  conditions existantes).
- Calculer directement en SQL les totaux dont le bilan a besoin (total facturé, encaissé,
  reste à encaisser, nombre de commandes soldées, etc.), au lieu de dictionnaires par
  commande puis d'une boucle. Si un calcul par commande est indispensable (par exemple
  `MAX(0, facturé - encaissé)`), le faire en SQL avec `GROUP BY IdCommande` dans une
  sous-requête, puis `SUM` au-dessus.
- Index couvrants si le plan le demande (`Paiements(IdCommande, EstAnnule, MontantPaye)`,
  `MaterielsSupplements(IdCommande, Quantite, PrixUnitaire)`).
- Même traitement pour `CalculerRepartitionCa` et toute méthode de `TresorerieService` ou
  `DepenseService` qui matérialise une liste d'IDs.

**Acceptation** : `CalculerBilan` < 300 ms sur 120k ; résultats identiques à l'ancienne
méthode.

---

## 7. Mesure finale (sans laquelle rien n'est « fait »)

1. **Base 120k** (copie fraîche de la sauvegarde) : lancer l'app, ouvrir chaque écran
   (Dashboard, Alertes RDV, Commandes, Clients, Paiements, Retours, Statut, Commissions,
   Dépenses / Trésorerie, Dashboard couturier), plus 10 alertes « Suivant », une recherche
   client, la création d'un client et d'une commande. Relever les logs `[PERF]` (premier
   affichage et chargement complet). Relever la mémoire du processus après 10 minutes de
   navigation.
2. **Base 2M clients / 3M pièces** : la générer avec `StressDataGenerator` **hors de `bin`**
   (`C:\Users\USER\GestionCoutureApp_Stress2M\`), avec ≥ 5 000 suivis d'alerte, ≥ 2 000
   retours et des noms réalistes. Lancer l'app dessus (`GESTIONCOUTUREAPP_DATA_DIR`) et
   refaire les mêmes mesures. Mesurer aussi la durée de la migration au premier démarrage.
3. Remplir `docs/PERF_AVANT_APRES.md` : un tableau par écran (avant / 120k / 2M), les plans
   `EXPLAIN`, la ligne de résumé de `dotnet test`.

**Critères d'acceptation** (2M) : chaque écran < 1 s au premier affichage, < 2 s complet ;
requêtes paginées < 100 ms ; badges < 150 ms ; Commissions et Bilan < 1 s ; mémoire
< 300 Mo après 10 minutes ; aucun gel de l'interface > 100 ms ;
**`dotnet test` complet = 0 échec**.

Procéder dans l'ordre 1 → 7. Terminer par un rapport honnête : fait (avec preuve),
non fait, et pourquoi.
