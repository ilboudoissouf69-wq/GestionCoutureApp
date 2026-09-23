# GUIDE D'INTÉGRATION - AUDIT SÉCURITÉ FINANCIÈRE

## 📋 ÉTAPES D'INTÉGRATION

### 1. Enregistrer AuditService dans App.cs

**Fichier** : `App.cs`

**Localisation** : Dans la méthode `ConfigureServices()` ou équivalent

```csharp
// Ajouter après les autres services
services.AddSingleton<IAuditService, AuditService>();
```

**Exemple complet** :
```csharp
private void ConfigureServices(IServiceCollection services)
{
    // ... services existants ...
    
    services.AddSingleton<ICommandeService, CommandeService>();
    services.AddSingleton<IPaiementService, PaiementService>();
    services.AddSingleton<ITresorerieService, TresorerieService>();
    
    // ✅ NOUVEAU : Service d'audit de sécurité
    services.AddSingleton<IAuditService, AuditService>();
    
    // ... autres services ...
}
```

---

### 2. Appliquer la migration EF Core

```powershell
# Sauvegarder la base actuelle
Copy-Item gestion_couture.db gestion_couture_backup_$(Get-Date -Format yyyyMMdd).db

# Appliquer la migration
dotnet ef database update --project GestionCoutureApp.csproj

# Vérifier que la migration s'est bien passée
dotnet ef migrations list --project GestionCoutureApp.csproj
```

**Vérification SQL** :
```sql
-- Vérifier la table JournalAudit
SELECT name FROM sqlite_master WHERE type='table' AND name='JournalAudit';

-- Vérifier les nouveaux champs de Commande
PRAGMA table_info(Commandes);

-- Devrait afficher EstSupprimee, MotifSuppression, etc.
```

---

### 3. Configurer le numéro de supervision

**Option A : Via SQL direct**
```sql
INSERT INTO Parametres (Cle, Valeur) 
VALUES ('NumeroSupervisionAudit', '+221771234567')
ON CONFLICT(Cle) DO UPDATE SET Valeur = '+221771234567';
```

**Option B : Via ParametresView (recommandé)**
- Aller dans Paramètres (interface Boss uniquement)
- Ajouter un nouvel onglet "Sécurité & Audit"
- Champ : "Numéro de supervision externe (WhatsApp)"
- Format : +221XXXXXXXXX

---

### 4. Corrections des vues - CommandesView.cs

**Fichier** : `Views/CommandesView.cs`

#### 4.1 Injection de IAuditService

```csharp
public partial class CommandesView : UserControl
{
    private readonly ICommandeService _commandeService;
    private readonly IAuditService _auditService; // ✅ NOUVEAU
    
    // ... autres champs ...

    public CommandesView()
    {
        InitializeComponent();
        
        // Récupérer AuditService depuis le conteneur DI
        _auditService = App.Current.Services.GetService<IAuditService>();
    }
}
```

#### 4.2 Remplacer l'appel à Supprimer()

**AVANT (code actuel - DANGEREUX)** :
```csharp
private void BtnSupprimerCommande_Click(object sender, RoutedEventArgs e)
{
    if (CommandeSelectionnee == null) return;
    
    var confirmation = MessageBox.Show(
        "Voulez-vous vraiment supprimer cette commande ?",
        "Confirmation",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
    
    if (confirmation == MessageBoxResult.Yes)
    {
        try
        {
            _commandeService.Supprimer(CommandeSelectionnee.IdCommande);
            MessageBox.Show("Commande supprimée.", "Succès");
            ChargerCommandes();
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Erreur : {ex.Message}", "Erreur");
        }
    }
}
```

**APRÈS (code sécurisé)** :
```csharp
private async void BtnSupprimerCommande_Click(object sender, RoutedEventArgs e)
{
    if (CommandeSelectionnee == null) return;
    
    // ✅ Vérifier que l'utilisateur est Boss
    if (_utilisateurConnecte == null || _utilisateurConnecte.Role != "Boss")
    {
        MessageBox.Show(
            "Seul le Boss peut supprimer une commande.",
            "Accès refusé",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return;
    }
    
    // ✅ Demander le motif OBLIGATOIRE
    var dialogMotif = new MotifSuppressionDialog
    {
        Owner = Window.GetWindow(this),
        Titre = "Suppression de commande",
        Message = $"Suppression de la commande #{CommandeSelectionnee.IdCommande}\n" +
                 $"Client : {CommandeSelectionnee.Client?.Prenom} {CommandeSelectionnee.Client?.Nom}\n\n" +
                 "⚠️ Cette action sera enregistrée dans le journal d'audit.\n" +
                 "⚠️ Une notification sera envoyée au numéro de supervision.\n\n" +
                 "Veuillez indiquer le motif de suppression :"
    };
    
    if (dialogMotif.ShowDialog() != true || string.IsNullOrWhiteSpace(dialogMotif.Motif))
    {
        MessageBox.Show("La suppression nécessite un motif.", "Annulé");
        return;
    }
    
    // ✅ Confirmation finale
    var confirmation = MessageBox.Show(
        $"Êtes-vous CERTAIN de vouloir supprimer cette commande ?\n\n" +
        $"Motif : {dialogMotif.Motif}\n\n" +
        "Cette action sera tracée de manière permanente.",
        "Confirmation définitive",
        MessageBoxButton.YesNo,
        MessageBoxImage.Warning);
    
    if (confirmation != MessageBoxResult.Yes) return;
    
    try
    {
        // ✅ Appel sécurisé avec traçabilité complète
        await _commandeService.SupprimerAsync(
            id: CommandeSelectionnee.IdCommande,
            idOperateur: _utilisateurConnecte.IdEmploye,
            nomOperateur: $"{_utilisateurConnecte.Prenom} {_utilisateurConnecte.Nom}",
            motif: dialogMotif.Motif.Trim(),
            auditService: _auditService
        );
        
        MessageBox.Show(
            "Commande supprimée (suppression logique).\n" +
            "Elle reste dans la base pour l'audit mais n'apparaîtra plus dans les listes.",
            "Succès",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        
        ChargerCommandes();
    }
    catch (UnauthorizedAccessException ex)
    {
        MessageBox.Show(
            $"Accès refusé : {ex.Message}",
            "Erreur de sécurité",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
    catch (InvalidOperationException ex)
    {
        MessageBox.Show(
            $"Impossible de supprimer :\n{ex.Message}",
            "Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Erreur inattendue : {ex.Message}",
            "Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
```

#### 4.3 Créer la boîte de dialogue MotifSuppressionDialog

**Nouveau fichier** : `Views/MotifSuppressionDialog.xaml`

```xml
<Window x:Class="GestionCoutureApp.Views.MotifSuppressionDialog"
        xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        Title="{Binding Titre}"
        Width="500" Height="300"
        WindowStartupLocation="CenterOwner"
        ResizeMode="NoResize">
    <Grid Margin="20">
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <TextBlock Grid.Row="0" 
                   Text="{Binding Message}"
                   TextWrapping="Wrap"
                   Margin="0,0,0,15"/>

        <TextBox Grid.Row="1"
                 x:Name="TxtMotif"
                 TextWrapping="Wrap"
                 AcceptsReturn="True"
                 VerticalScrollBarVisibility="Auto"
                 MaxLength="500"/>

        <StackPanel Grid.Row="2" 
                    Orientation="Horizontal" 
                    HorizontalAlignment="Right"
                    Margin="0,15,0,0">
            <Button Content="Annuler" 
                    Width="100" 
                    Margin="0,0,10,0"
                    Click="BtnAnnuler_Click"/>
            <Button Content="Confirmer" 
                    Width="100"
                    Click="BtnConfirmer_Click"
                    Style="{StaticResource DangerButtonStyle}"/>
        </StackPanel>
    </Grid>
</Window>
```

**Code-behind** : `Views/MotifSuppressionDialog.xaml.cs`

```csharp
using System.Windows;

namespace GestionCoutureApp.Views
{
    public partial class MotifSuppressionDialog : Window
    {
        public string Titre { get; set; } = "Confirmation";
        public string Message { get; set; } = "";
        public string Motif => TxtMotif.Text;

        public MotifSuppressionDialog()
        {
            InitializeComponent();
            DataContext = this;
        }

        private void BtnAnnuler_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnConfirmer_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtMotif.Text))
            {
                MessageBox.Show(
                    "Le motif est obligatoire.",
                    "Erreur",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            DialogResult = true;
            Close();
        }
    }
}
```

---

### 5. Masquer le bouton Supprimer pour la secrétaire

**Fichier** : `Views/CommandesView.xaml`

```xml
<!-- Bouton de suppression - Visible uniquement pour le Boss -->
<Button x:Name="BtnSupprimerCommande"
        Content="Supprimer"
        Click="BtnSupprimerCommande_Click"
        Style="{StaticResource DangerButtonStyle}"
        Visibility="{Binding RoleUtilisateur, 
                     Converter={StaticResource RoleToBossVisibilityConverter}}"/>
```

**Ou en code-behind** (si pas de binding) :
```csharp
private void ConfigurerVisibiliteSelonRole()
{
    if (_utilisateurConnecte == null) return;
    
    // Masquer le bouton de suppression pour tous sauf Boss
    BtnSupprimerCommande.Visibility = _utilisateurConnecte.Role == "Boss" 
        ? Visibility.Visible 
        : Visibility.Collapsed;
}
```

---

### 6. Corrections PaiementsView.cs (Annulation)

**Fichier** : `Views/PaiementsView.cs`

#### 6.1 Méthode d'annulation avec motif obligatoire

```csharp
private void BtnAnnulerPaiement_Click(object sender, RoutedEventArgs e)
{
    if (PaiementSelectionne == null) return;
    
    // ✅ Vérifier que l'utilisateur est Boss
    if (_utilisateurConnecte == null || _utilisateurConnecte.Role != "Boss")
    {
        MessageBox.Show(
            "Seul le Boss peut annuler un paiement.",
            "Accès refusé",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return;
    }
    
    // ✅ Demander le motif OBLIGATOIRE
    var dialogMotif = new MotifAnnulationDialog
    {
        Owner = Window.GetWindow(this),
        Titre = "Annulation de paiement",
        Message = $"Annulation du paiement {PaiementSelectionne.RecuNumero}\n" +
                 $"Montant : {PaiementSelectionne.MontantPaye:N0} FCFA\n\n" +
                 "⚠️ Cette action sera enregistrée dans le journal d'audit.\n" +
                 "⚠️ Une notification sera envoyée au numéro de supervision.\n\n" +
                 "Veuillez indiquer le motif d'annulation :"
    };
    
    if (dialogMotif.ShowDialog() != true || string.IsNullOrWhiteSpace(dialogMotif.Motif))
    {
        MessageBox.Show("L'annulation nécessite un motif.", "Annulé");
        return;
    }
    
    try
    {
        // ✅ L'annulation est déjà sécurisée dans PaiementService
        // (RequireRoleByIdEnum vérifie Boss)
        _paiementService.Annuler(
            idPaiement: PaiementSelectionne.IdPaiement,
            motif: dialogMotif.Motif.Trim(),
            idAnnulateur: _utilisateurConnecte.IdEmploye,
            nomAnnulateur: $"{_utilisateurConnecte.Prenom} {_utilisateurConnecte.Nom}"
        );
        
        // ✅ Enregistrer dans le journal d'audit
        if (_auditService != null)
        {
            await _auditService.EnregistrerActionAsync(
                idOperateur: _utilisateurConnecte.IdEmploye,
                nomOperateur: $"{_utilisateurConnecte.Prenom} {_utilisateurConnecte.Nom}",
                roleOperateur: "Boss",
                typeAction: "PAIEMENT_ANNULE",
                entite: "Paiement",
                idEntite: PaiementSelectionne.IdPaiement,
                valeursAvant: new
                {
                    PaiementSelectionne.RecuNumero,
                    PaiementSelectionne.MontantPaye,
                    PaiementSelectionne.ModePaiement,
                    PaiementSelectionne.DatePaiement
                },
                valeursApres: null,
                motif: dialogMotif.Motif.Trim(),
                envoyerNotification: true // Notification WhatsApp automatique
            );
        }
        
        MessageBox.Show(
            "Paiement annulé avec succès.\n" +
            "Le reçu est marqué ANNULÉ mais reste dans l'historique.",
            "Succès",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
        
        ChargerPaiements();
    }
    catch (UnauthorizedAccessException ex)
    {
        MessageBox.Show(
            $"Accès refusé : {ex.Message}",
            "Erreur de sécurité",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
    catch (Exception ex)
    {
        MessageBox.Show(
            $"Erreur : {ex.Message}",
            "Erreur",
            MessageBoxButton.OK,
            MessageBoxImage.Error);
    }
}
```

---

### 7. Ajouter une vue Audit pour le Boss

**Nouveau fichier** : `Views/AuditView.xaml`

```xml
<UserControl x:Class="GestionCoutureApp.Views.AuditView"
             xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="Auto"/>
            <RowDefinition Height="*"/>
            <RowDefinition Height="Auto"/>
        </Grid.RowDefinitions>

        <!-- En-tête -->
        <TextBlock Grid.Row="0" 
                   Text="📊 Journal d'Audit (Lecture seule)"
                   FontSize="24" FontWeight="Bold"
                   Margin="0,0,0,20"/>

        <!-- Filtres -->
        <StackPanel Grid.Row="1" Orientation="Horizontal" Margin="0,0,0,10">
            <DatePicker x:Name="DateDebut" Width="150" Margin="0,0,10,0"/>
            <DatePicker x:Name="DateFin" Width="150" Margin="0,0,10,0"/>
            <Button Content="Filtrer" Click="BtnFiltrer_Click" Width="100" Margin="0,0,10,0"/>
            <Button Content="Vérifier intégrité" Click="BtnVerifierIntegrite_Click" Width="150"/>
        </StackPanel>

        <!-- Liste des entrées -->
        <DataGrid Grid.Row="2"
                  x:Name="DgAudit"
                  AutoGenerateColumns="False"
                  IsReadOnly="True"
                  SelectionMode="Single">
            <DataGrid.Columns>
                <DataGridTextColumn Header="ID" Binding="{Binding IdJournal}" Width="60"/>
                <DataGridTextColumn Header="Date/Heure" Binding="{Binding DateHeureLocale}" Width="150"/>
                <DataGridTextColumn Header="Opérateur" Binding="{Binding NomOperateur}" Width="150"/>
                <DataGridTextColumn Header="Rôle" Binding="{Binding RoleOperateur}" Width="100"/>
                <DataGridTextColumn Header="Action" Binding="{Binding ActionAffichee}" Width="200"/>
                <DataGridTextColumn Header="Entité" Binding="{Binding Entite}" Width="100"/>
                <DataGridTextColumn Header="ID Entité" Binding="{Binding IdEntite}" Width="80"/>
                <DataGridTextColumn Header="Motif" Binding="{Binding Motif}" Width="*"/>
                <DataGridTextColumn Header="Hash" Binding="{Binding HashCourant}" Width="120">
                    <DataGridTextColumn.ElementStyle>
                        <Style TargetType="TextBlock">
                            <Setter Property="Text" Value="{Binding HashCourant, Converter={StaticResource HashTruncateConverter}}"/>
                            <Setter Property="FontFamily" Value="Consolas"/>
                            <Setter Property="FontSize" Value="10"/>
                        </Style>
                    </DataGridTextColumn.ElementStyle>
                </DataGridTextColumn>
            </DataGrid.Columns>
        </DataGrid>

        <!-- Statistiques -->
        <StackPanel Grid.Row="3" Orientation="Horizontal" Margin="0,10,0,0">
            <TextBlock x:Name="TxtStatistiques" FontWeight="Bold"/>
        </StackPanel>
    </Grid>
</UserControl>
```

**Code-behind** : `Views/AuditView.xaml.cs`

```csharp
using System;
using System.Windows;
using System.Windows.Controls;
using GestionCoutureApp.Services;

namespace GestionCoutureApp.Views
{
    public partial class AuditView : UserControl
    {
        private readonly IAuditService _auditService;

        public AuditView()
        {
            InitializeComponent();
            _auditService = App.Current.Services.GetService<IAuditService>();
            
            // Charger le dernier mois par défaut
            DateDebut.SelectedDate = DateTime.Now.AddMonths(-1);
            DateFin.SelectedDate = DateTime.Now;
            
            ChargerAudit();
        }

        private void ChargerAudit()
        {
            if (_auditService == null) return;

            var debut = DateDebut.SelectedDate ?? DateTime.Now.AddMonths(-1);
            var fin = DateFin.SelectedDate ?? DateTime.Now;

            var entrees = _auditService.ObtenirParPeriode(debut, fin);
            DgAudit.ItemsSource = entrees;

            var stats = _auditService.ObtenirStatistiques(debut, fin);
            TxtStatistiques.Text = $"Total : {stats.NombreTotalActions} actions | " +
                                   $"Suppressions/Annulations : {stats.NombreActionsSuppressionAnnulation} | " +
                                   $"Notifications envoyées : {stats.NombreNotificationsEnvoyees}";
        }

        private void BtnFiltrer_Click(object sender, RoutedEventArgs e)
        {
            ChargerAudit();
        }

        private void BtnVerifierIntegrite_Click(object sender, RoutedEventArgs e)
        {
            if (_auditService == null) return;

            var (integre, message) = _auditService.VerifierIntegriteChaine();

            var icon = integre ? MessageBoxImage.Information : MessageBoxImage.Warning;
            var titre = integre ? "✅ Intégrité vérifiée" : "⚠️ ALERTE SÉCURITÉ";

            MessageBox.Show(message, titre, MessageBoxButton.OK, icon);
        }
    }
}
```

---

### 8. Ajouter l'onglet Audit dans MainWindow

**Fichier** : `Views/MainWindow.xaml`

```xml
<!-- Dans la navigation principale -->
<TabItem Header="📊 Audit" Visibility="{Binding RoleUtilisateur, 
         Converter={StaticResource RoleToBossVisibilityConverter}}">
    <views:AuditView/>
</TabItem>
```

---

## ✅ CHECKLIST DE DÉPLOIEMENT

- [ ] Sauvegarder la base de données actuelle
- [ ] Appliquer la migration EF Core
- [ ] Enregistrer `IAuditService` dans `App.cs`
- [ ] Configurer le `NumeroSupervisionAudit`
- [ ] Mettre à jour `CommandesView.cs` avec `SupprimerAsync()`
- [ ] Créer `MotifSuppressionDialog.xaml`
- [ ] Mettre à jour `PaiementsView.cs` pour tracer les annulations
- [ ] Créer `AuditView.xaml` pour consultation du journal
- [ ] Ajouter l'onglet Audit dans `MainWindow.xaml`
- [ ] Exécuter les tests de sécurité : `dotnet test`
- [ ] Vérifier l'intégrité initiale de la chaîne d'audit
- [ ] Former le Boss sur le nouveau système

---

## 🔐 RAPPEL SÉCURITÉ

**Ce qui est maintenant IMPOSSIBLE même pour le Boss :**
- ❌ Modifier une entrée du journal d'audit
- ❌ Supprimer une entrée du journal d'audit
- ❌ Supprimer une commande avec historique de paiements
- ❌ Annuler un paiement sans motif
- ❌ Effectuer une action sensible sans notification externe

**Ce qui reste possible pour le Boss (avec traçabilité totale) :**
- ✅ Supprimer logiquement une commande (avec motif)
- ✅ Annuler un paiement (avec motif + notification)
- ✅ Consulter le journal d'audit (lecture seule)
- ✅ Vérifier l'intégrité de la chaîne d'audit
