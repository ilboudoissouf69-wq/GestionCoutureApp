# Guide du Système de Trésorerie - GestionCoutureApp

## Vue d'ensemble

Le système de trésorerie de GestionCoutureApp permet de suivre la santé financière de l'atelier en calculant le chiffre d'affaires, les dépenses et le bilan financier. Il utilise une **méthode de comptabilité de trésorerie** (cash basis) où les transactions sont enregistrées au moment de leur paiement effectif.

## Architecture

### Service : `ITresorerieService` / `TresorerieService`

Localisé dans `Services/TresorerieService.cs`, ce service centralise tous les calculs financiers.

**Enregistrement** : Le service est enregistré comme singleton dans `App.cs` :
```csharp
services.AddSingleton<ITresorerieService, TresorerieService>();
```

---

## Méthodes disponibles

### 1. `CalculerChiffreAffaires(DateTime debut, DateTime fin)`

Calcule le chiffre d'affaires total encaissé sur une période donnée.

**Logique** :
- Somme de tous les paiements **non annulés** dont la date se situe entre `debut` et `fin`
- Ne compte que les paiements effectivement encaissés (comptabilité de trésorerie)
- Exclut automatiquement les paiements annulés (colonne `EstAnnule = true`)

**Utilisation** :
```csharp
var tresorerieService = App.Services.GetRequiredService<ITresorerieService>();
DateTime debut = new DateTime(2026, 1, 1);
DateTime fin = new DateTime(2026, 12, 31);
decimal ca = await tresorerieService.CalculerChiffreAffaires(debut, fin);
Console.WriteLine($"CA 2026 : {ca:N0} FCFA");
```

**Retour** : `decimal` - montant total en FCFA

---

### 2. `CalculerDepensesTotales(DateTime debut, DateTime fin)`

Calcule le total des dépenses validées sur une période.

**Logique** :
- Somme de toutes les dépenses **validées** (`StatutValidation = "Validee"`)
- Période déterminée par la colonne `DateDepense`
- Exclut les dépenses en attente de validation ou annulées

**Utilisation** :
```csharp
decimal depenses = await tresorerieService.CalculerDepensesTotales(debut, fin);
Console.WriteLine($"Dépenses 2026 : {depenses:N0} FCFA");
```

**Retour** : `decimal` - montant total en FCFA

---

### 3. `CalculerCommissionsCouturieres(DateTime debut, DateTime fin)`

Calcule le total des commissions dues aux couturières sur une période.

**Logique** :
- Somme de toutes les commissions (`MontantCommission`) dont la commande associée a été livrée (`DateLivraison` non nulle)
- La période est déterminée par la date de livraison de la commande
- Ne compte que les commissions effectivement dues (commandes terminées et livrées)

**Utilisation** :
```csharp
decimal commissions = await tresorerieService.CalculerCommissionsCouturieres(debut, fin);
Console.WriteLine($"Commissions 2026 : {commissions:N0} FCFA");
```

**Retour** : `decimal` - montant total en FCFA

---

### 4. `ObtenirBilanFinancier(DateTime debut, DateTime fin)`

Génère un bilan financier complet avec tous les indicateurs clés.

**Structure retournée** : `BilanFinancier`
```csharp
public class BilanFinancier
{
    public decimal ChiffreAffaires { get; set; }      // Total encaissé
    public decimal DepensesTotales { get; set; }       // Dépenses validées
    public decimal CommissionsCouturieres { get; set; } // Salaires couturières
    public decimal BeneficeBrut { get; set; }          // CA - Dépenses
    public decimal BeneficeNet { get; set; }           // CA - Dépenses - Commissions
    public DateTime PeriodeDebut { get; set; }
    public DateTime PeriodeFin { get; set; }
}
```

**Calculs** :
- **Bénéfice Brut** = Chiffre d'Affaires - Dépenses Totales
- **Bénéfice Net** = Chiffre d'Affaires - Dépenses Totales - Commissions Couturières

**Utilisation** :
```csharp
var bilan = await tresorerieService.ObtenirBilanFinancier(debut, fin);

Console.WriteLine($"=== BILAN FINANCIER ===");
Console.WriteLine($"Période : {bilan.PeriodeDebut:dd/MM/yyyy} - {bilan.PeriodeFin:dd/MM/yyyy}");
Console.WriteLine($"Chiffre d'Affaires : {bilan.ChiffreAffaires:N0} FCFA");
Console.WriteLine($"Dépenses : {bilan.DepensesTotales:N0} FCFA");
Console.WriteLine($"Commissions : {bilan.CommissionsCouturieres:N0} FCFA");
Console.WriteLine($"Bénéfice Brut : {bilan.BeneficeBrut:N0} FCFA");
Console.WriteLine($"Bénéfice Net : {bilan.BeneficeNet:N0} FCFA");
```

---

## Intégration dans les vues

### Exemple : Afficher le CA du mois en cours

```csharp
public partial class DashboardView : Page
{
    private readonly ITresorerieService _tresorerieService;

    public DashboardView()
    {
        InitializeComponent();
        _tresorerieService = App.Services.GetRequiredService<ITresorerieService>();
        Loaded += async (s, e) => await ChargerIndicateurs();
    }

    private async Task ChargerIndicateurs()
    {
        var debutMois = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
        var finMois = debutMois.AddMonths(1).AddDays(-1);

        var bilan = await _tresorerieService.ObtenirBilanFinancier(debutMois, finMois);

        TxtCA.Text = $"{bilan.ChiffreAffaires:N0} FCFA";
        TxtDepenses.Text = $"{bilan.DepensesTotales:N0} FCFA";
        TxtBenefice.Text = $"{bilan.BeneficeNet:N0} FCFA";
        
        // Indicateur de santé financière
        if (bilan.BeneficeNet < 0)
            TxtBenefice.Foreground = Brushes.Red;
        else
            TxtBenefice.Foreground = Brushes.Green;
    }
}
```

### Exemple : Rapport mensuel

```csharp
private async void BtnRapportMensuel_Click(object sender, RoutedEventArgs e)
{
    var mois = DatePickerMois.SelectedDate ?? DateTime.Now;
    var debut = new DateTime(mois.Year, mois.Month, 1);
    var fin = debut.AddMonths(1).AddDays(-1);

    var bilan = await _tresorerieService.ObtenirBilanFinancier(debut, fin);

    // Affichage ou export CSV
    var rapport = new StringBuilder();
    rapport.AppendLine($"RAPPORT MENSUEL - {debut:MMMM yyyy}");
    rapport.AppendLine($"Chiffre d'Affaires : {bilan.ChiffreAffaires:N0} FCFA");
    rapport.AppendLine($"Dépenses : {bilan.DepensesTotales:N0} FCFA");
    rapport.AppendLine($"Commissions Couturières : {bilan.CommissionsCouturieres:N0} FCFA");
    rapport.AppendLine($"Bénéfice Net : {bilan.BeneficeNet:N0} FCFA");

    File.WriteAllText($"Rapport_{debut:yyyyMM}.txt", rapport.ToString());
    MessageBox.Show("Rapport généré avec succès !", "Export", 
                    MessageBoxButton.OK, MessageBoxImage.Information);
}
```

---

## Règles de gestion importantes

### 1. Comptabilité de trésorerie (Cash Basis)

Le système utilise la **comptabilité de trésorerie** :
- ✅ Les revenus sont comptabilisés **au moment du paiement effectif** (colonne `DatePaiement` dans `Paiements`)
- ✅ Les dépenses sont comptabilisées **au moment de la validation** (colonne `DateDepense` dans `Depenses`)
- ❌ Les commandes non payées ne comptent **pas** dans le CA
- ❌ Les dépenses non validées ne comptent **pas** dans les charges

**Pourquoi ce choix ?**
- Reflète la réalité de la trésorerie disponible
- Simple à comprendre pour les petites structures
- Évite les décalages entre facturation et encaissement

### 2. Exclusions automatiques

Le système exclut automatiquement :
- ❌ Paiements annulés (`EstAnnule = true`)
- ❌ Dépenses non validées (`StatutValidation != "Validee"`)
- ❌ Commissions pour commandes non livrées (`DateLivraison IS NULL`)

### 3. Gestion des périodes

- Les dates de début et fin sont **inclusives** : `[debut, fin]`
- Pour calculer un mois : `debut = 1er du mois, fin = dernier jour du mois`
- Pour une année : `debut = 1er janvier, fin = 31 décembre`

---

## Cas d'usage

### Cas 1 : Tableau de bord temps réel

Afficher les indicateurs financiers du mois en cours avec actualisation automatique.

```csharp
// Dans DashboardView.xaml.cs
private async void Page_Loaded(object sender, RoutedEventArgs e)
{
    await ActualiserKPI();
}

private async Task ActualiserKPI()
{
    var debut = new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
    var fin = DateTime.Now;

    var bilan = await _tresorerieService.ObtenirBilanFinancier(debut, fin);
    
    TxtCAMoisCourant.Text = $"{bilan.ChiffreAffaires:N0} FCFA";
    TxtBeneficeMoisCourant.Text = $"{bilan.BeneficeNet:N0} FCFA";
}
```

### Cas 2 : Comparaison mensuelle

Comparer les performances du mois actuel avec le mois précédent.

```csharp
private async void BtnComparerMois_Click(object sender, RoutedEventArgs e)
{
    var maintenant = DateTime.Now;
    var debutMoisCourant = new DateTime(maintenant.Year, maintenant.Month, 1);
    var finMoisCourant = maintenant;

    var debutMoisPrecedent = debutMoisCourant.AddMonths(-1);
    var finMoisPrecedent = debutMoisCourant.AddDays(-1);

    var bilanCourant = await _tresorerieService.ObtenirBilanFinancier(
        debutMoisCourant, finMoisCourant);
    var bilanPrecedent = await _tresorerieService.ObtenirBilanFinancier(
        debutMoisPrecedent, finMoisPrecedent);

    var evolution = bilanCourant.BeneficeNet - bilanPrecedent.BeneficeNet;
    var evolutionPourcent = bilanPrecedent.BeneficeNet != 0 
        ? (evolution / bilanPrecedent.BeneficeNet) * 100 
        : 0;

    MessageBox.Show(
        $"Évolution : {evolution:N0} FCFA ({evolutionPourcent:+0.0;-0.0}%)",
        "Comparaison",
        MessageBoxButton.OK,
        MessageBoxImage.Information);
}
```

### Cas 3 : Rapport annuel

Générer un rapport annuel avec analyse par trimestre.

```csharp
private async void BtnRapportAnnuel_Click(object sender, RoutedEventArgs e)
{
    int annee = (int)NumAnnee.Value;
    var rapport = new StringBuilder();
    rapport.AppendLine($"=== RAPPORT ANNUEL {annee} ===\n");

    for (int trimestre = 1; trimestre <= 4; trimestre++)
    {
        int moisDebut = (trimestre - 1) * 3 + 1;
        var debut = new DateTime(annee, moisDebut, 1);
        var fin = debut.AddMonths(3).AddDays(-1);

        var bilan = await _tresorerieService.ObtenirBilanFinancier(debut, fin);

        rapport.AppendLine($"TRIMESTRE {trimestre}");
        rapport.AppendLine($"CA : {bilan.ChiffreAffaires:N0} FCFA");
        rapport.AppendLine($"Bénéfice Net : {bilan.BeneficeNet:N0} FCFA\n");
    }

    // Bilan annuel total
    var bilanAnnuel = await _tresorerieService.ObtenirBilanFinancier(
        new DateTime(annee, 1, 1),
        new DateTime(annee, 12, 31));

    rapport.AppendLine("TOTAL ANNUEL");
    rapport.AppendLine($"CA : {bilanAnnuel.ChiffreAffaires:N0} FCFA");
    rapport.AppendLine($"Dépenses : {bilanAnnuel.DepensesTotales:N0} FCFA");
    rapport.AppendLine($"Commissions : {bilanAnnuel.CommissionsCouturieres:N0} FCFA");
    rapport.AppendLine($"Bénéfice Net : {bilanAnnuel.BeneficeNet:N0} FCFA");

    TxtRapport.Text = rapport.ToString();
}
```

---

## Sécurité et Performance

### Transactions asynchrones

Toutes les méthodes sont **asynchrones** (`async Task<T>`) pour éviter le blocage de l'interface utilisateur lors de calculs longs.

```csharp
// ✅ Bon
var bilan = await _tresorerieService.ObtenirBilanFinancier(debut, fin);

// ❌ Mauvais (bloque l'UI)
var bilan = _tresorerieService.ObtenirBilanFinancier(debut, fin).Result;
```

### Gestion du DbContext

Le service utilise `IDbContextFactory` pour créer des contextes à courte durée de vie :

```csharp
using var context = _contextFactory.CreateDbContext();
// ... requêtes ...
// Le context est automatiquement disposed à la fin du using
```

**Pourquoi ?** Évite les fuites mémoire et les conflits de concurrence.

### Requêtes optimisées

Les calculs utilisent des requêtes SQL optimisées avec :
- Projections `.Select()` pour ne charger que les colonnes nécessaires
- Filtres `.Where()` appliqués en base de données
- Agrégations `.Sum()` effectuées côté serveur

---

## Tests et Validation

### Test unitaire exemple

```csharp
[Fact]
public async Task CalculerChiffreAffaires_ExclutPaiementsAnnules()
{
    // Arrange
    var context = CreateTestContext();
    var service = new TresorerieService(context);
    
    // Ajouter un paiement valide
    context.Paiements.Add(new Paiement 
    { 
        Montant = 50000, 
        DatePaiement = new DateTime(2026, 6, 15),
        EstAnnule = false 
    });
    
    // Ajouter un paiement annulé
    context.Paiements.Add(new Paiement 
    { 
        Montant = 30000, 
        DatePaiement = new DateTime(2026, 6, 20),
        EstAnnule = true 
    });
    
    await context.SaveChangesAsync();

    // Act
    var ca = await service.CalculerChiffreAffaires(
        new DateTime(2026, 6, 1), 
        new DateTime(2026, 6, 30));

    // Assert
    Assert.Equal(50000, ca); // Ne compte que le paiement non annulé
}
```

### Validation manuelle

Pour vérifier les calculs :

1. **Exporter les données brutes** :
   - Export CSV des paiements sur la période
   - Export CSV des dépenses sur la période
   - Export CSV des commissions sur la période

2. **Calculer manuellement** dans Excel :
   - `=SOMME(Colonne_Paiements)` → doit égaler ChiffreAffaires
   - `=SOMME(Colonne_Depenses)` → doit égaler DepensesTotales
   - `=SOMME(Colonne_Commissions)` → doit égaler CommissionsCouturieres

---

## Dépannage

### Problème : Les montants ne correspondent pas

**Vérifications** :
1. Les dates sont-elles dans la bonne plage ?
2. Y a-t-il des paiements annulés à exclure ?
3. Les dépenses sont-elles toutes validées ?
4. Les commissions concernent-elles des commandes livrées ?

**Solution** : Activer les logs détaillés temporairement :
```csharp
// Dans App.cs, changer temporairement
logging.SetMinimumLevel(LogLevel.Debug);
```

### Problème : Performance lente

**Vérifications** :
1. La base de données est-elle indexée correctement ?
2. La période analysée est-elle trop large (> 1 an) ?

**Solution** : Utiliser des périodes plus courtes ou créer des rapports en cache.

---

## Évolutions futures possibles

### 1. Prévisions de trésorerie

Ajouter une méthode pour projeter le CA sur les mois à venir basée sur l'historique :

```csharp
Task<decimal> PrevoirChiffreAffaires(DateTime moisCible);
```

### 2. Analyse par catégorie

Détailler les dépenses par catégorie (matériaux, salaires, loyer, etc.) :

```csharp
Task<Dictionary<string, decimal>> AnalyserDepensesParCategorie(DateTime debut, DateTime fin);
```

### 3. Seuils d'alerte

Déclencher des notifications si le bénéfice passe en négatif ou si les dépenses dépassent un seuil :

```csharp
Task<List<AlerteFinanciere>> VerifierSeuilsFinanciers(DateTime debut, DateTime fin);
```

### 4. Export graphique

Générer des graphiques d'évolution du CA et des dépenses :

```csharp
Task<byte[]> GenererGraphiqueEvolution(DateTime debut, DateTime fin, TypeGraphique type);
```

---

## Conclusion

Le système de trésorerie de GestionCoutureApp offre une vue claire et fiable de la santé financière de l'atelier. En utilisant une comptabilité de trésorerie simple et des calculs cohérents, il permet aux gérants de prendre des décisions éclairées sur la gestion de leur activité.

Pour toute question ou amélioration, consultez le code source dans `Services/TresorerieService.cs`.

---

**Auteur** : Équipe GestionCoutureApp  
**Dernière mise à jour** : Septembre 2026  
**Version** : 2.0 - Retouche Choco
