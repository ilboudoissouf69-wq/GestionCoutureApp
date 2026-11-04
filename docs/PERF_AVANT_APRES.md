# Performance — Mesures AVANT / APRÈS

> Base de stress : `bin\StressProfile-20261027` (120 000 clients, 120 000 commandes,
> 180 000 pièces dont 89 000 en retard, 60 000 terminées).
> Objectif final : chaque écran < 1 s avec **2 000 000 clients / 3 000 000 pièces**.

## Diagnostic établi (mesuré, ne pas refaire)

Le SQL brut n'est PAS le goulot (toutes les requêtes < 1,5 s). Le temps est perdu dans la
**matérialisation de tables entières en mémoire** puis dans la **construction de dizaines
de milliers d'éléments WPF**.

## Temps AVANT (base de stress 120k/120k/180k)

| Écran / Méthode                          | Avant         | Cause                                                                                       |
|------------------------------------------|---------------|---------------------------------------------------------------------------------------------|
| Alertes RDV — premier affichage          | 1 à 10 min    | `ObtenirPiecesActivesAsync` charge 150k pièces + 150k `AlerteRendezVous` créés en mémoire    |
| Alertes RDV — section Risque de retard   | gel complet   | `ConstruireSectionRisqueRetard` : 89k Borders × 15 éléments = >1,3 M visuels sur thread UI   |
| Alertes RDV — timer 60 s                  | 10 min / tick | Recharge tout (pièces + suivis) toutes les 60 s, sur thread UI                               |
| Dashboard — échéances                     | ~150k objets  | `ChargerEcheancesAsync` : ObtenirRetards + ObtenirRendezVousSemaine (150k) pour Take(6)      |
| Dashboard — stats couturiers (CA)         | 180k pièces   | `ChargerStatsCouturiersDtoAsync` : `.AsEnumerable().Sum()` sur toutes les pièces             |
| Dashboard — stats retard                  | ~89k + table  | `ObtenirStatsDashboardAsync` : 89k candidats + toute la table `SuivisAlerteRetard` pour 1 int|
| Dashboard — sommes CA / Dépenses           | .AsEnum().Sum | `ChargerCartesStatsDtoAsync` : `.AsEnumerable().Sum()` sur paiements et dépenses            |
| Dashboard — graphique revenus             | 7j en mémoire | `ChargerGraphiqueRevenusDtoAsync` : `.AsEnumerable().GroupBy()`                              |
| Dashboard — timer 60 s                    | refresh complet| Rafraîchissement complet toutes les 60 s                                                     |
| Retours — `ChargerPerformanceQualite`     | synchrone UI  | `Include(p => p.Commande)` sur 90k pièces, `.Date` dans Where, comptage mémoire par couturier |
| Retours — badges                          | faux + lent   | `MettreAJourBadges` : compte sur la page courante (20) en mémoire, pas sur le filtre total    |
| Retours — `ObtenirPageAsync`              | `ToLower()`   | `ToLower().Contains()` sur 6 colonnes jointes → SCAN complet                                  |
| Retours — repli erreur                     | toute la table | `catch → ObtenirTous()` recharge toute la table en cas d'erreur                              |
| Retours — `_context` longue durée         | fuite         | DbContext créé dans le constructeur, gardé vie de la vue                                     |
| Démarrage — BackupService                 | 7 × 105 Mo    | Copie la base entière à chaque lancement (716 Mo de sauvegardes)                             |

## Temps APRÈS (à remplir au fur et à mesure)

### Étape 0 — Mesure et filet de sécurité

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| PerfLogger actif sur tous écrans | ✓     | ✓     | ✅     |
| Générateur `StressDataGenerator` | —     | créé  | ✅ `--clients 2000000` |
| Tests de charge ≥ 200k pièces    | —     | créé  | ✅ `Perf_BudgetTests` (Perf cat) |
| Tests existants (356)             | verts | verts | ✅     |

### Étape 1 — Date de RDV effective persistée

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| Colonnes DateRdvEffective + CommandeActive | —     | ajoutées | ✅ |
| Migration + backfill SQL          | —     | créé  | ✅ |
| Index (Statut, DateRdvEffective), (IdCouturier, Statut), Commandes(EstSupprimee, EstAnnulee, DateFin) | —     | créés | ✅ |
| Formule unifiée (HeureFin ?? 17:00) | HeureFin ?? HeureDebut | HeureFin ?? 17:00 | ✅ |
| Maintien dans Ajouter / ModifierInfosCommande / AjouterPiece / DupliquerPiece / Modifier / SupprimerAsync / AnnulerParSecretaireAsync / ReporterRendezVousAsync | — | ✅ | ✅ |
| Tests existants (356)             | verts | verts | ✅ |

### Étape 2 — Alertes RDV

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| ObtenirPiecesActivesAsync (150k pièces chargées) | 150k entités | supprimé | ✅ |
| ObtenirResumeAsync (badges) | 150k pièces | COUNT SQL pur | ✅ |
| ObtenirSectionAsync (pages de 50) | 89k alertes | ORDER BY LIMIT 50 | ✅ |
| ObtenirAlertesRisqueRetardPageAsync | 89k + table Suivis | NOT EXISTS SQL | ✅ |
| ObtenirStatsDashboardAsync | 89k + table Suivis | COUNT NOT EXISTS SQL | ✅ |
| ConstruireSectionRisqueRetard/Ligne | >1M visuels | ListBox virtualisée DataTemplate | ✅ |
| Timer 60s | refresh complet | badges only si vue non visible | ✅ |
| CancellationToken | absent | annulation à Unloaded | ✅ |

### Étape 3 — Dashboard

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| ChargerEcheancesAsync (150k → Take(6)) | 150k pièces | SQL LIMIT 6 sur DateRdvEffective | ✅ |
| ChargerStatsCouturiersDtoAsync CA | .AsEnumerable().Sum() | GROUP BY + SUM SQL | ✅ |
| ChargerCartesStatsDtoAsync sommes | .AsEnumerable().Sum() | SUM SQL (double) | ✅ |
| ChargerGraphiqueRevenusDtoAsync | .AsEnumerable().GroupBy() | GROUP BY SQL | ✅ |

### Étape 4 — Retours

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| ChargerPerformanceQualite (sync UI) | synchrone, Include 90k | async, GROUP BY SQL | ✅ |
| MettreAJourBadges (faux) | page courante 20 items | COUNT SQL GROUP BY Statut | ✅ |
| _context longue durée | fuite | DbContext par opération | ✅ |
| catch → ObtenirTous() | recharge tout | supprimé | ✅ |

### Étape 5 — TresorerieService

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| CalculerChiffreAffaires | .AsEnumerable().Sum() | SUM SQL double | ✅ |
| CalculerTotalDepenses | .AsEnumerable().Sum() | SUM SQL double | ✅ |
| .Date dans les Where (non-indexable) | partout | bornes >= / < AddDays(1) | ✅ |

### Étape 6 — Recherche texte

> Non implémentée (FTS5) — les colonnes `NomRecherche` et l'index préfixe
> sont la prochaine étape prioritaire. Les `ToLower().Contains()` actuels
> restent et font un SCAN — acceptable pour les volumes actuels (120k),
> mais à corriger avant 2M de clients.

### Étape 7 — Sauvegarde et maintenance

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| BackupService (7 copies au démarrage) | immédiat, 4h, 15 max | 30s delay, 1x/jour, 7 max | ✅ |
| PRAGMA optimize | absent | à la fermeture (Dispose) | ✅ |

## Corrections détaillées (Étape 5 et suivantes)

Sera rempli avec chaque occurrence corrigée (fichier, ligne, motif, correction).

## Critères d'acceptation

Sur une base **2 000 000 clients / 3 000 000 pièces** :

- [x] Alertes RDV : section Risque de retard virtualisée (DataTemplate XAML, fini les 1M+ visuels)
- [x] Alertes RDV : badges par COUNT SQL pur (~5ms)
- [x] Alertes RDV : sections par ORDER BY LIMIT 50 SQL
- [x] Dashboard : ChargerEcheancesAsync — SQL LIMIT 6 (fini les 150k pièces)
- [x] Dashboard : sommes par SUM SQL (fini les .AsEnumerable().Sum())
- [x] Retours : ChargerPerformanceQualite async GROUP BY (fini le sync UI + 90k Include)
- [x] TresorerieService : SUM SQL, bornes indexables
- [x] BackupService : 1 sauvegarde/jour max, 30s délai, 7 backups rétention
- [x] PRAGMA optimize à la fermeture
- [x] DateRdvEffective persistée et indexée — toutes les requêtes d'alertes l'utilisent
- [x] CommandeActive persistée — filtrage sans jointure Commandes
- [x] `dotnet build` sans erreur nouvelle
- [x] `dotnet test` 100% vert (365 tests)
- [ ] FTS5 / préfixe pour recherche 2M clients (Étape 6 à faire)
- [ ] Mesure sur base 2M/3M pièces (StressDataGenerator prêt, à exécuter)
