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
| (à mesurer)                      | —     | —     | —      |

### Étape 3 — Dashboard

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| (à mesurer)                      | —     | —     | —      |

### Étape 4 — Retours

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| (à mesurer)                      | —     | —     | —      |

### Étape 5 — Autres écrans

| Écran / Méthode                  | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| (à mesurer)                      | —     | —     | —      |

### Étape 6 — Recherche texte

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| (à mesurer)                      | —     | —     | —      |

### Étape 7 — Sauvegarde et maintenance

| Méthode                          | Avant | Après | Statut |
|----------------------------------|-------|-------|--------|
| (à mesurer)                      | —     | —     | —      |

## Corrections détaillées (Étape 5 et suivantes)

Sera rempli avec chaque occurrence corrigée (fichier, ligne, motif, correction).

## Critères d'acceptation

Sur une base **2 000 000 clients / 3 000 000 pièces** :

- [ ] Chaque écran : premier affichage < 1 s, données complètes < 2 s
- [ ] Aucune requête de service d'écran > 300 ms
- [ ] Aucune ne retourne plus de 50 lignes de détail (hors GROUP BY agrégés)
- [ ] Mémoire stable (< 300 Mo) en naviguant 10 min
- [ ] Thread UI ne gèle jamais > 100 ms
- [ ] Totaux / badges identiques à l'ancien calcul (tests non-régression)
- [ ] `dotnet build` sans avertissement nouveau
- [ ] `dotnet test` 100 % vert
