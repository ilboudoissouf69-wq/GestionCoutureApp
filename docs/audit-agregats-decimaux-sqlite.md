# Audit des agrégats décimaux SQLite / EF Core

Recherche effectuée hors du projet de tests après correction de `PaiementsView.ChargerCommandes()`.

## Agrégations décimales

| Emplacement | Source | Exécution |
|---|---|---|
| `Models/Commande.cs` (`ResteAPayer`, `MontantEncaisse`, `MontantTotalCalcule`, `MontantTotalAvecMateriaux`, `TotalMateriaux`) | Collections de navigation déjà chargées | Mémoire ; propriétés `[NotMapped]` |
| `Views/CommandesView.cs`, `Views/CommandesView_Etape5.cs` | Listes de pièces et buffers de la vue | Mémoire |
| `Views/CommissionsView.cs` | `_dernierApercu` | Mémoire |
| `Views/CouturierDashboardView.cs` | `commandes` est matérialisé par `ToList()` avant `SelectMany` / `Sum` | Mémoire |
| `Views/DashboardView.cs` | Les paiements/dépenses passent par `AsEnumerable()` avant `Sum`; les agrégations graphiques et les sommes de pièces se font sur des collections | Mémoire |
| `Views/DepensesView.cs`, `Views/MaterielsView.cs` | Listes déjà chargées / ItemsSource | Mémoire |
| `Views/FenetreRecu.cs` | Collections de la commande chargée | Mémoire |
| `Views/PaiementsView.cs` / `Services/CommandesEncaissablesService.cs` | Montants bruts et paiements valides matérialisés par `ToList()` | Mémoire ; plus aucun `Sum(decimal)` SQL |
| `Services/DepenseService.cs` | Requêtes financières matérialisées avec `AsEnumerable()` avant `Sum`; commandes chargées avec leurs navigations avant les calculs | Mémoire |
| `Services/CommissionService.cs` | Pièces chargées par `ToList()`, puis agrégées dans les listes et navigations | Mémoire |
| `Services/CommandeService.cs` | Collections `Pieces` / `Paiements` de commandes chargées avant les validations financières | Mémoire |
| `Services/MaterielService.cs`, `Services/PaiementService.cs` | `AsEnumerable()` avant `Sum` | Mémoire |
| `Services/RetourService.cs`, `Services/WhatsAppService.cs` | Calculs sur les commandes et navigations déjà chargées | Mémoire |
| `Services/TresorerieService.cs` | Requêtes décimales basculées avec `AsEnumerable()` ou matérialisées avec `ToList()` avant les calculs | Mémoire |

Aucun autre `Sum`, `Average`, `Min` ou `Max` sur une expression décimale n’est resté dans `IQueryable` après la correction. Les `Max` / `Min` restants portent sur des dates ou des entiers.

## Comparaisons et tris décimaux en SQL

Aucun `OrderBy` / `ThenBy` sur une colonne décimale ni comparaison décimale SQL (`>`, `<`, `>=`, `<=`) n’a été trouvé. Le filtre `ResteAPayer > 0.01m` de la liste des paiements s’exécute après matérialisation du résultat et reste inchangé. Aucun changement de schéma n’a été nécessaire.
