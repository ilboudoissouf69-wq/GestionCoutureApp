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
