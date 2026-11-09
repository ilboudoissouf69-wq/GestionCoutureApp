# Performance — état vérifié (2026-11-04)

Ce rapport sépare les changements présents des critères réellement validés. Les temps
ci-dessous viennent des sorties de tests de ce checkout, pas d'une extrapolation.

## Corrections réalisées

- `App.cs` applique manuellement les migrations d'index et de colonnes denormalisées,
  y compris `DateRdvEffective`, `CommandeActive`, `DateMiParcours` et les clés de
  recherche client. Le backfill affiche un message, s'exécute par lots et met à jour les
  diagnostics de schéma.
- L'index unique `Paiements.RecuNumero` est filtré sur les reçus non vides. Une inspection
  en lecture seule du profil qui affichait l'erreur a trouvé 90 000 lignes dont le reçu
  vaut `''`; le schéma est maintenant migrable sans modifier ces lignes. Le test d'upgrade
  valide plusieurs reçus vides et refuse deux reçus non vides identiques.
- `GestionCoutureApp.Tests/MigrationReelleTests.cs` crée une base SQLite fichier avec un
  ancien schéma explicite (sans `EnsureCreated`), exécute le chemin de migration réel et
  vérifie 1 000 valeurs backfillées ainsi que les colonnes et index attendus. Ce test est
  inclus dans la suite fonctionnelle passée. Le fixture n'exécute pas `CommandeService.Ajouter`
  après l'upgrade; cette vérification reste à ajouter.
- Les alertes ont des index date-first/couvrants et des pages SQL regroupées par commande.
  Les retours sont présents dans les sections et les notifications urgentes, la règle
  stagnante utilise `DateMiParcours`, la modale détaille les pièces, les sections disposent
  de « Voir plus », le timer respecte visibilité/annulation et WhatsApp distingue prêts et
  rappels.
- La recherche client est normalisée et paginée par préfixe; Commandes et Paiements
  chargent des pages. Le changement « contient » vers « commence par » demeure une
  divergence fonctionnelle à confirmer avec l'utilisateur.
- Les totaux de trésorerie utilisent une requête SQL condensée, un cache SQLite fixé à
  32 Mo, le stockage temporaire en mémoire et des index couvrants sur pièces/paiements/
  matériaux; les dates de Commission emploient
  des bornes exclusives. Les statistiques Retours par couturier sont agrégées en SQL,
  avec la reprise effective groupée sur `IdCouturierReprise ?? IdCouturier`.
- Le générateur Perf fixe les clés étrangères des retours et construit des relations
  pièce/commande valides pour les scénarios de pièces multiples.

## Mesures observées

| Opération | Base Perf de 130 000 clients / 200 000 pièces | Résultat |
|---|---:|---|
| `RetourService.ObtenirPageAsync` | 125 ms, 20 éléments | sous le budget de test de 300 ms |
| `CommandeService.ObtenirPageLightAsync` | 158 ms, 20 éléments | sous le budget de test de 300 ms |
| `ClientService.ObtenirPageAsync` | 43 ms, 50 éléments | sous le budget de test de 300 ms |
| `PaiementService.ObtenirPageLightAsync` | 68 ms, 15 éléments | sous le budget de test de 300 ms |
| `AlerteService.ObtenirToutesAlertesAsync` | 674 ms; 50 retraits, 50 retards, 50 retours | échec du budget de 300 ms |
| `RetardAlerteService.ObtenirAlertesRisqueRetardAsync` | 1 589 ms, 50 alertes | échec du budget de 300 ms |
| `RetardAlerteService.ObtenirStatsDashboardAsync` | 362 ms; 64 714 retards, 901 clients | échec du budget de 300 ms |
| `AlerteService.ObtenirRetards` | 2 ms, 50 éléments | sous le budget de test de 300 ms |
| `AlerteService.ObtenirRendezVousSemaine` | 3 ms, 50 éléments | sous le budget de test de 300 ms |
| `TresorerieService.CalculerBilan` | test P04 isolé : 8 s sur 120 000 commandes après réglage du cache SQLite | sous le seuil historique de test de 10 000 ms; encore au-dessus du critère demandé de 300 ms |
| `CommissionService.CalculerApercu` | 10 140 ms sur le jeu de charge 120k | échec du seuil de charge de 10 000 ms |

Les mesures de l'ancien audit sur la base 120k (2 ms, 75 ms et ~9 s) sont des valeurs
rapportées par l'audit, non reproduites ici. Aucun plan `EXPLAIN QUERY PLAN` n'a été
capturé pour ce rapport.

## Validation exécutée

- `dotnet build GestionCoutureApp.Tests/GestionCoutureApp.Tests.csproj --no-restore -v:q`:
  génération réussie, 0 erreur, 226 avertissements.
- `dotnet test ... --filter "TestCategory!=Perf" --no-build`: **Réussi! - échec: 0,
  réussite: 367, ignorée(s): 0, total: 367, durée: 30 s** (dernière exécution).
- P04 isolé passe en 8 s sur 120k après le réglage SQLite (garde-fou <10 s; cible du brief
  <300 ms toujours manquée). Dernier `dotnet test` complet : **Échoué! - échec: 3,
  réussite: 396, ignorée(s): 0, total: 399, durée: 14 m 28 s**. Les contrôles de budget
  affichent `ObtenirToutesAlertesAsync` à 674 ms, `ObtenirAlertesRisqueRetardAsync` à
  1 589 ms et `ObtenirStatsDashboardAsync` à 362 ms, au-delà de 300 ms; le TearDown Perf
  signale aussi `CommissionService.CalculerApercu` à 10 140 ms. Le scénario grand volume
  de `CommandesEncaissablesService` n'a plus signalé l'ancienne exception de traduction.
- `git diff --check`: aucune erreur de whitespace; seuls des avertissements de conversion
  LF/CRLF restent affichés.

## Acceptation non établie

- Le seuil visé par le brief (<300 ms bilan et <100 ms pour chaque page) n'est pas atteint
  partout. Le bilan et l'API legacy risque retard restent trop lents.
- Aucun test sur la sauvegarde 120k n'a été lancé. Le fichier de référence se trouve hors
  des racines accessibles de ce checkout. Les dossiers `bin` et la sauvegarde n'ont pas
  été modifiés.
- La base 2M clients / 3M pièces, les temps de chaque écran, la mémoire après 10 minutes,
  les gels UI, tous les plans EXPLAIN et la vérification manuelle des interactions n'ont
  pas été produits.
- Le test d'upgrade ne valide pas encore `CommandeService.Ajouter` sur le fichier migré,
  ni la comparaison exhaustive avec `AncienAlerteCalcul`.
- Les commits séparés par étape prescrits par le prompt n'ont pas été créés.

Les critères finaux du chantier ne sont donc pas tous atteints; le rapport ne déclare pas
le projet « 100 % vert ».

## Reprise V3 — mesures du 09/11/2026

### Dernière reprise — suite finale

`dotnet build GestionCoutureApp.Tests/GestionCoutureApp.Tests.csproj --no-restore
-p:BaseOutputPath=C:/Users/USER/AppData/Local/Temp/gca_build/ -v:q` réussit avec **0 erreur**
(2 avertissements NU1603 sur la résolution d'OpenCvSharp). `git diff --check` est propre.
La suite complète a ensuite réussi : **Réussi! — échec : 0, réussite : 403, ignorée(s) : 0,
total : 403, durée : 7 min 27 s** (TRX temporaire `full-suite-final.trx`). Les premiers
essais VSTest bloquaient parce que le bac à sable refusait le socket local; le test a passé
une fois le loopback autorisé.

Le calcul d'aperçu des commissions a été réécrit sans `Include`, avec des agrégats SQL par
commande et une projection des pièces candidates. Sur le jeu synthétique de charge (120 000
clients, 120 000 commandes, 180 000 pièces, 90 000 paiements), `P05_CommissionService` passe
en **1 009 ms**, au-dessus du critère V3 de 300 ms. Les scénarios métier existants sont verts,
mais le projet ne contient pas encore le test de comparaison ancien/nouveau demandé par V3.

`P04_TresorerieService_CalculerBilan` passe en **5 368 ms** sur ce même jeu synthétique, très
au-dessus du critère de 300 ms. Les contrôles de calcul métier restent verts; le coût de
performance reste bloquant. Les budgets d'alerte et de recherche passent dans le jeu de
charge : `ObtenirAlertesRisqueRetardAsync` environ **10–25 ms**, statistiques dashboard
**4–5 ms** (avec limite d'affichage 999+), recherche client **9–12 ms**; le plan B06 utilise
`IX_PiecesCommande_Active_DateRdv_Risque` et `IX_SuivisAlerteRetard_TypeCible_IdCible`, sans
scan de ces tables.

Les critères finaux d'acceptation restent ouverts : aucune exécution sur la sauvegarde de
référence 120k (indisponible dans les racines accessibles), aucune base 2M clients / 3M
pièces mesurée, aucun parcours UI manuel, aucune mesure mémoire après 10 minutes, pas de
preuve ancienne/nouvelle du calcul commission et le bilan dépasse sa cible de latence. Ces
mesures ne permettent pas de certifier le déploiement comme pleinement accepté.

La suite complète a été relancée après les changements en cours avec le répertoire de
sortie temporaire demandé. Résumé réel : **399 réussites, 3 échecs, 402 tests, 10 min
32 s**. Les échecs Perf observés sont : `ObtenirToutesAlertesAsync` 427 ms, risque retard
2 132 ms et statistiques retard 487 ms (budget de chacun : 300 ms). Ce passage confirme
que ces points ne sont pas encore acceptés.

Pour le prédicat « déjà prévenu », le SQL EF a maintenant deux branches `UNION ALL`, sans
`CASE`, avec égalité sur `DateRdvTraite = DateRdvEffective` et la clé indexée de la cible :

```sql
SELECT t0.IdPieceCommande, t0.DateRdvEffective, t0.IdCommande,
       t0.RendezVousException, t0.Statut, t0.TypeVetement
FROM (
  SELECT t.* FROM (
    SELECT p.IdPieceCommande, p.DateRdvEffective, p.IdCommande,
           p.RendezVousException, p.Statut, p.TypeVetement
    FROM PiecesCommande AS p
    WHERE p.CommandeActive AND p.Statut <> 'Terminee' AND p.Statut <> 'Livree'
      AND p.DateRdvEffective <= @seuil AND p.RendezVousException IS NULL
      AND NOT EXISTS (SELECT 1 FROM SuivisAlerteRetard AS s
        WHERE s.TypeCible = 'Commande' AND s.IdCible = p.IdCommande
          AND s.NbPreventions > 0 AND s.DateRdvTraite = p.DateRdvEffective)
    ORDER BY p.DateRdvEffective LIMIT @taille
  ) AS t
  UNION ALL
  -- seconde branche : RendezVousException IS NOT NULL, TypeCible='Piece', IdCible=IdPieceCommande
) AS t0
```

Le plan du `UNION ALL` diagnostique donne `SEARCH PiecesCommande USING INDEX
IX_PiecesCommande_Active_DateRdv_Statut` et, dans chaque branche, `SEARCH SuivisAlerteRetard
USING INDEX IX_SuivisAlerteRetard_TypeCible_IdCible (TypeCible=? AND IdCible=?)`; aucun
`SCAN` de ces tables n'a été observé. La requête de comptage simple est passée de 559 ms à
145 ms après ajout d'un index couvrant sur la base synthétique. Cependant, l'appel complet
EF B06 a encore pris 1 063 ms dans son diagnostic ciblé et 2 132 ms dans la suite complète.
Ce résultat montre que le plan SQL simplifié ne suffit pas à expliquer la latence de la
requête exécutée. L'index couvrant et la requête par branche restent à valider sur une
base fraîche créée par les migrations; aucune mesure 120k réelle n'est disponible.

La pagination des sections a aussi été corrigée pour appliquer `Take(taille)` après le
décalage de page, au lieu de faire croître la taille retournée à chaque page. La correction
n'a pas encore de test spécifique page 1/2/3 ni de validation complète distincte.

### Résultats définitifs de cette reprise (09/11/2026)

Les chiffres plus anciens de la section « Reprise V3 » ci-dessus sont historiques et sont
remplacés par le dernier run complet : **Réussi! — échec : 0, réussite : 403, ignorée(s) :
0, total : 403, durée : 7 min 27 s**. Après correction du chemin de repli InMemory des
statistiques dashboard, les tests `E7_18`, `E7_19` et `E7_20` passent également (3/3).

Le rapport TRX final (`%TEMP%\gca_testresults-final\full-suite-final.trx`) donne les durées
des appels de charge :

| Test | Résultat | Durée | Critère V3 |
|---|---:|---:|---:|
| `P04_TresorerieService_CalculerBilan` | réussi | 5 368 ms | < 300 ms — non atteint |
| `P05_CommissionService_CalculerApercu` | réussi | 1 009 ms | < 300 ms — non atteint |
| `B06_RetardAlerteService_ObtenirAlertesRisqueRetard_Budget` | réussi | 25 ms | < 300 ms — atteint |
| `B07_RetardAlerteService_ObtenirStatsDashboard_Budget` | réussi | 5 ms | < 300 ms — atteint |
| `B10_ClientService_RecherchePrefixe_BudgetEtNomsAccentes` | réussi | 10 ms | < 100 ms — atteint |

Le `dotnet build` après la correction InMemory réussit aussi avec **0 erreur** (2
avertissements NU1603). La réussite fonctionnelle complète n'efface pas les seuils de
performance non atteints ni l'absence de validation sur la base réelle et la base 2M; le
chantier V3 ne peut donc pas être certifié comme entièrement accepté.

Pour préparer la 2.1, les métadonnées projet, assembly et script Inno ont été alignées sur
`2.1.0`. Une publication autonome Release win-x64 a été produite et son exe vérifié à
`ProductVersion=2.1.0` / `FileVersion=2.1.0` dans `%TEMP%\gca_release_publish\` (113 MB).
Le compilateur Inno Setup (`ISCC.exe`) n'est pas installé sur cet environnement; le Setup
installable n'a donc pas été recompilé. Le dossier `publish` et le Setup existants du dépôt
n'ont pas été remplacés.
