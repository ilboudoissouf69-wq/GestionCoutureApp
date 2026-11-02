# PROMPT — Rendre GestionCoutureApp fluide avec 2 millions de clients

> À copier tel quel dans une nouvelle session Claude Code, à la racine du dépôt.

---

## Contexte

Application WPF .NET 8 + EF Core + SQLite (`GestionCoutureApp`). Avec la base de stress
`bin\StressProfile-20261027` (120 000 clients, 120 000 commandes, 180 000 pièces dont
89 000 en retard, 60 000 terminées), les écrans **Alertes RDV** (jusqu'à 10 min, voire
gel complet), **Dashboard**, **Retours** et dans une moindre mesure **Paiements**
mettent 1 à 10 minutes à s'afficher. Objectif : **chaque écran s'affiche en moins d'1 seconde
avec 2 000 000 de clients / 3 000 000 de pièces**, comme s'il y avait 2 clients.

Lancer le profil de stress :
```powershell
$env:GESTIONCOUTUREAPP_DATA_DIR = (Join-Path (Get-Location).Path 'bin\StressProfile-20261027')
$env:GESTIONCOUTUREAPP_DRIVE_BACKUP_FOLDER = 'Retouche Choco Stress Test 20261027'
dotnet run --project GestionCoutureApp.csproj
```

## Diagnostic déjà établi (mesuré, ne pas refaire)

Le SQL brut n'est PAS le goulot (toutes les requêtes < 1,5 s sur la base de stress).
Le temps est perdu dans la **matérialisation de tables entières en mémoire** puis dans
la **construction de dizaines de milliers d'éléments WPF**.

1. `Services/AlerteService.cs` → `ObtenirPiecesActivesAsync()` charge **toutes** les pièces
   non livrées (150 000 lignes), puis `ObtenirToutesAlertesAsync()` crée un `AlerteRendezVous`
   pour chacune (Retrait contient les 60 000 « Terminee » quelle que soit la date, Retards
   les 89 000 en retard). Le tri, la déduplication, le `GroupBy` et le plafond à 200 cartes
   se font ensuite en mémoire dans `Views/AlertesView.cs` → `RepartirEtAfficher()`.
2. **Cause n°1 du gel de 10 min** : `RetardAlerteService.ObtenirAlertesRisqueRetardAsync()`
   renvoie ~89 000 alertes, et `AlertesView.AfficherSectionRisqueRetard()` /
   `ConstruireSectionRisqueRetard()` crée **une `Border` à la main par alerte** (≈15 éléments
   chacune → >1 million d'éléments visuels) dans un `StackPanel` non virtualisé, **sans plafond**,
   sur le thread UI. Le tout est relancé toutes les 60 s par `_timer`.
3. `Views/DashboardView.cs` → `ChargerEcheancesAsync()` appelle `ObtenirRetards()` +
   `ObtenirRendezVousSemaine()` (150 000 pièces chargées, ~150 000 objets créés, `GroupBy`)
   **pour n'afficher que 6 cartes** (`Take(6)` en mémoire).
   `ChargerStatsCouturiersDtoAsync()` charge les 180 000 pièces pour sommer `MontantCouture`
   en mémoire et projette tous les retards pour un `Distinct` en mémoire.
   `RetardAlerteService.ObtenirStatsDashboardAsync()` charge ~89 000 candidats + toute la
   table `SuivisAlerteRetard` pour produire un simple compteur. Les sommes de paiements /
   dépenses passent par `.AsEnumerable().Sum()`. Rafraîchissement complet toutes les 60 s.
4. `Views/RetoursView.cs` → `ChargerPerformanceQualite()` est **synchrone sur le thread UI**,
   charge toutes les pièces Terminee/Livree avec `Include(p => p.Commande)` (≈90 000 entités
   suivies, 1,4 s de SQL seul + matérialisation), utilise `.Date` dans le `Where` (non
   indexable), puis compte en mémoire par couturier. Appelé à chaque chargement / filtre.
   `RetourService.ObtenirPageAsync` utilise `ToLower().Contains()` sur 6 colonnes jointes.
5. Démarrage : `BackupService` copie la base entière à chaque lancement
   (7 copies de 105 Mo = 716 Mo dans `Backups`). À 2 M de clients ≈ 1,5 Go par copie.

## Règles absolues (s'appliquent à TOUT le code touché)

- **Interdit** de charger une table ou un ensemble non borné en mémoire pour l'afficher,
  le compter, le sommer, le trier ou le filtrer. Toute liste affichée = `ORDER BY ... LIMIT`
  en SQL (`OrderBy().Skip().Take()` traduit par EF). Tout compteur = `COUNT` SQL. Toute
  somme = `SUM` SQL. Tout regroupement = `GROUP BY` SQL.
- Interdit : `.AsEnumerable()` / `.ToList()` avant un `Where`, `OrderBy`, `GroupBy`, `Sum`,
  `Count`, `Distinct` ou `Take`. Pour les `decimal` (non sommables par SQLite dans EF),
  utiliser `Sum(x => (double)x.Montant)` puis reconvertir en `decimal`, ou `GROUP BY` SQL
  qui renvoie quelques lignes — jamais toutes les lignes.
- Interdit dans un `Where` : `.Date`, `ToLower()`, `ToString()`, ternaires sur colonnes,
  calculs sur colonnes → les filtres doivent être indexables (bornes `>=` / `<`).
- Toute requête d'écran : `AsNoTracking()`, projection DTO (`Select`), jamais `Include`
  pour un affichage de liste.
- Aucun accès base sur le thread UI : tout est `async`/`await` (ou `Task.Run`) ; l'UI ne
  reçoit que le résultat déjà borné.
- Toute liste WPF potentiellement longue : `ListBox`/`ListView`/`DataGrid` avec
  `VirtualizingPanel.IsVirtualizing="True"`, `VirtualizingPanel.VirtualizationMode="Recycling"`,
  `ScrollViewer.CanContentScroll="True"`, et **jamais** placée dans un `ScrollViewer` ou
  `StackPanel` parent qui lui donne une hauteur infinie (sinon la virtualisation est
  désactivée). Interdit de générer des lignes en code-behind avec `Children.Add` en boucle
  sur des données.
- Chaque section affiche au maximum 50 éléments + le **total exact** (COUNT SQL) +
  un bouton « Voir plus » / pagination qui charge la page suivante en SQL.
- **Les règles métier ne changent pas** : mêmes sections, mêmes priorités, mêmes badges,
  mêmes totaux. Seule la manière de calculer change. Tous les tests existants de
  `GestionCoutureApp.Tests` doivent rester verts ; ajouter des tests qui comparent
  l'ancien et le nouveau calcul sur un petit jeu de données.

## Travail à réaliser, écran par écran (un commit par étape)

### Étape 0 — Mesure et filet de sécurité
- Activer les logs `[PERF]` (niveau Debug) via `PerfLogger` sur chaque chargement d'écran
  et chaque méthode de service touchée. Noter les temps AVANT dans `docs/PERF_AVANT_APRES.md`.
- Écrire un générateur de données de stress paramétrable (`--clients 2000000`) qui
  produit une distribution réaliste ET des cas extrêmes (dizaines de milliers de retards).
- Ajouter dans les tests des tests de charge (base SQLite fichier, ≥ 200 000 pièces) avec
  un budget : chaque méthode de service d'écran < 300 ms, retourne ≤ 50 éléments.

### Étape 1 — Date de RDV effective persistée (fondation des alertes)
La date de RDV d'une pièce est `RendezVousException ?? Commande.DateFin.Date + (Commande.HeureFin ?? 17:00)`
(attention : `RetardAlerteService` utilise `HeureFin ?? HeureDebut` → unifier avec le métier
et documenter le choix). Ce calcul est impossible à indexer tel quel.
- Ajouter une colonne `PiecesCommande.DateRdvEffective` (DateTime, non nulle), remplie par
  une migration (UPDATE SQL sur l'existant) et **maintenue par le service** à chaque création /
  modification de commande, de `DateFin`/`HeureFin`, de `RendezVousException`, de report de RDV.
  Centraliser ce calcul dans une seule méthode.
- Ajouter une colonne dénormalisée `PiecesCommande.CommandeActive` (bool : commande non
  supprimée et non annulée) maintenue de la même façon, OU garantir que la jointure sur
  `Commandes` est indexée — choisir et justifier.
- Index : `(Statut, DateRdvEffective)`, `(IdCouturier, Statut)`, `Commandes(EstSupprimee, EstAnnulee, DateFin)`.
  Vérifier chaque requête avec `EXPLAIN QUERY PLAN` (pas de `SCAN` sur une grosse table).

### Étape 2 — Alertes RDV (`AlerteService`, `RetardAlerteService`, `AlertesView`)
- Supprimer `ObtenirPiecesActivesAsync()` qui charge tout, et son cache.
- Nouvelle API :
  - `Task<ResumeAlertesDto> ObtenirResumeAsync()` : uniquement des `COUNT` SQL pour les
    badges (Prêts, Retards, Aujourd'hui, Imminentes, Risque de retard, badge menu).
  - `Task<PagedResult<AlerteCommandeVm>> ObtenirSectionAsync(SectionAlerte section, int page, int taille = 50)` :
    filtre + `ORDER BY` + `LIMIT` en SQL, regroupement par commande en SQL ou sur la seule page.
  - Les règles d'affectation « une pièce dans une seule section par priorité
    (retard > prêt > aujourd'hui > imminente) » doivent être exprimées en conditions SQL
    exclusives sur `Statut` et `DateRdvEffective`, pas en dédupliquant en mémoire.
- « Risque de retard » : même traitement (COUNT + page de 50). Pour l'exclusion par
  `SuivisAlerteRetard`, faire un `NOT EXISTS` / `LEFT JOIN` SQL, ne plus charger toute la table.
- `AlertesView` : remplacer `ConstruireSectionRisqueRetard` / `ConstruireLigneRisqueRetard`
  (Borders construites en code) par un `DataTemplate` XAML dans une liste virtualisée.
  Chaque section : 50 cartes max + total + « Voir plus ». Affichage immédiat d'un squelette /
  indicateur de chargement, puis remplissage section par section (les badges d'abord).
- `VerifierNotificationsUrgentes` : requête SQL dédiée bornée (`Take(20)`) sur les urgentes.
- Timer 60 s : ne recharger que les compteurs (`ObtenirResumeAsync`) et la page visible,
  annuler (`CancellationToken`) tout chargement en cours quand la vue est quittée.

### Étape 3 — Dashboard (`DashboardView`, services associés)
- `ChargerEcheancesAsync` : nouvelle méthode service `ObtenirEcheancesUrgentesAsync(6)` —
  requête SQL avec `ORDER BY` priorité/date `LIMIT 6` (sur commandes), ne plus appeler
  `ObtenirRetards()` / `ObtenirRendezVousSemaine()`.
- `ChargerStatsCouturiersDtoAsync` : CA par couturier = `GROUP BY IdCouturier` + `SUM` SQL ;
  retards par couturier = `COUNT(DISTINCT IdCommande)` SQL groupé. Rien en mémoire.
- `ObtenirStatsDashboardAsync` : compteurs SQL (`COUNT` avec `NOT EXISTS` sur les suivis).
- Sommes Paiements / Dépenses : `SUM` SQL (voir règle decimal).
- `ChargerGraphiqueRevenusDtoAsync` : `GROUP BY` jour en SQL sur la plage.
- Lancer toutes les sections en parallèle (chacune son DbContext), afficher chaque carte
  dès que son résultat arrive. Timer 60 s : ne rien faire si la vue n'est pas visible.

### Étape 4 — Retours (`RetoursView`, `RetourService`)
- `ChargerPerformanceQualite` → méthode service async : `GROUP BY IdCouturier` + `COUNT`
  pour les pièces (sans `Include`, bornes de dates indexables `>= debut` / `< demainMinuit`)
  et pour les retours. Ne plus l'appeler à chaque changement de page de la grille.
- Badges (`MettreAJourBadges`) : `COUNT ... GROUP BY Statut` SQL sur tout le filtre,
  pas sur la page courante en mémoire (aujourd'hui c'est faux ET lent).
- `ObtenirPageAsync` : projection DTO, pas d'`Include`, recherche indexable (voir étape 6).
- Supprimer le `_context` longue durée du constructeur (DbContext par opération).
- Supprimer le repli `catch → ObtenirTous()` qui recharge toute la table en cas d'erreur.

### Étape 5 — Paiements, Commandes, Clients, Statut, Commissions, Dépenses, Couturier
Appliquer les mêmes règles à TOUS les autres écrans : rechercher systématiquement dans
`Views/` et `Services/` les motifs `ToList()`, `AsEnumerable()`, `ObtenirTous`, `Include(`,
`.Date`, `ToLower()`, `Children.Add` dans une boucle, `ItemsControl` dans un `ScrollViewer`,
et corriger chaque occurrence qui touche une table susceptible de grossir
(Clients, Commandes, PiecesCommande, Paiements, Retours, JournalAudit, Commissions,
MaterielsSupplements, Depenses). Lister chaque correction dans `docs/PERF_AVANT_APRES.md`.

### Étape 6 — Recherche texte à 2 M de clients
`Contains` / `ToLower().Contains` = scan complet. Mettre en place une recherche indexable :
colonnes normalisées (`NomRecherche` en minuscules sans accents, maintenue par le service)
avec recherche par préfixe (`StartsWith` → `LIKE 'abc%'` indexé) et/ou table FTS5 SQLite
pour nom/prénom/téléphone. Débounce 300 ms, `Take(50)`, annulation de la requête précédente.

### Étape 7 — Sauvegarde et maintenance
- Sauvegarde au démarrage : en arrière-plan, via l'API de sauvegarde en ligne SQLite
  (`SqliteConnection.BackupDatabase`) ou `VACUUM INTO`, jamais bloquante, au plus une fois
  par jour, avec rétention (ex. 7 dernières).
- Exécuter `PRAGMA optimize` à la fermeture ; `ANALYZE` après migration.

## Critères d'acceptation (à vérifier et chiffrer dans `docs/PERF_AVANT_APRES.md`)

Sur une base générée à **2 000 000 clients / 3 000 000 pièces** :
- Chaque écran (Dashboard, Alertes RDV, Commandes, Clients, Paiements, Retours, Statut,
  Commissions, Dépenses) : premier affichage < 1 s, données complètes < 2 s.
- Aucune requête de service d'écran > 300 ms ; aucune ne retourne plus de 50 lignes de
  détail (hors `GROUP BY` agrégés).
- Mémoire du processus stable (< 300 Mo) en naviguant 10 minutes entre les écrans.
- Le thread UI ne gèle jamais plus de 100 ms (pas de fenêtre « Ne répond pas »).
- Totaux / badges identiques à l'ancien calcul (tests de non-régression).
- `dotnet build` sans avertissement nouveau, `dotnet test` 100 % vert.

Procéder étape par étape, montrer les temps avant/après pour chaque étape, et ne pas passer
à la suivante tant que la précédente ne respecte pas ses critères.
