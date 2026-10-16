# ÉTAPE 5 — Deux améliorations de la création de commande

## A) BROUILLON AUTOMATIQUE (un seul par utilisateur)

### Fonctionnalités

1. **Nouvelle entité `BrouillonCommande`**
   - `IdEmploye` (unique) : un seul brouillon par utilisateur
   - `ContenuJson` : sérialisation JSON du formulaire complet
   - `DateMiseAJour` : horodatage de la dernière modification

2. **Sauvegarde automatique**
   - **Debounce de 3 secondes** : sauvegarde automatique après 3 secondes d'inactivité
   - **À la fermeture** : sauvegarde quand l'utilisateur quitte la page
   - **Non bloquante** : aucun impact sur l'UI, opération asynchrone
   - **Contenu sauvegardé** :
     - Client sélectionné
     - Toutes les pièces avec leurs détails
     - Mesures de chaque pièce
     - Prix et couturier
     - Dates et heures de RDV
     - Notes et descriptions
     - Matériaux/suppléments
     - **Chemins de photos UNIQUEMENT** (pas de base64 pour rester léger)

3. **Bandeau de restauration**
   - Affiché au chargement de la page si un brouillon existe
   - Format : "Vous avez une commande en cours (client X, date) — Reprendre / Abandonner"
   - **Reprendre** : restaure tout le formulaire dans son état exact
   - **Abandonner** : supprime définitivement le brouillon

4. **Suppression automatique**
   - Le brouillon est supprimé quand la commande est enregistrée
   - Le brouillon est supprimé quand l'utilisateur l'abandonne explicitement
   - Le brouillon survit à la fermeture de l'application (persisté en base)

5. **Garanties**
   - Le brouillon ne crée AUCUNE commande
   - Le brouillon ne génère AUCUN numéro
   - Le brouillon n'enregistre AUCUN paiement
   - Le brouillon n'affecte NI la trésorerie NI les statistiques
   - Pas de brouillon pour la modification d'une commande existante

### Implémentation technique

#### Fichiers créés
- `Models/BrouillonCommande.cs` : entité EF Core
- `Models/BrouillonCommandeDto.cs` : DTO pour sérialisation JSON
- `Services/IBrouillonCommandeService.cs` : interface du service
- `Services/BrouillonCommandeService.cs` : implémentation du service
- `Migrations/20261016000000_AjoutBrouillonCommande.cs` : migration EF Core
- `Views/CommandesView_Etape5.cs` : fichier partial avec méthodes de brouillon

#### Modifications
- `Data/ApplicationDbContext.cs` : ajout DbSet + index unique sur IdEmploye
- `App.cs` : enregistrement du service dans le conteneur DI
- `Views/CommandesView.cs` : ajout du champ `_brouillonService` et appels
- `Views/CommandesView.xaml` : ajout du bandeau de restauration

#### Méthodes principales
- `InitialiserBrouillonAutomatique()` : configure le timer et les hooks
- `DeclenencherSauvegardeBrouillon()` : démarre le debounce de 3s
- `SauvegarderBrouillonAsync()` : sauvegarde asynchrone en base
- `VerifierBrouillonExistantAsync()` : vérifie et affiche le bandeau au chargement
- `BtnReprendreBrouillon_Click()` : restaure le formulaire complet
- `BtnAbandonnerBrouillon_Click()` : supprime le brouillon
- `SupprimerBrouillonApresValidationAsync()` : nettoyage après validation de commande

## B) BOUTON « DUPLIQUER » SOUS CHAQUE PIÈCE

### Fonctionnalités

1. **Bouton de duplication**
   - Placé dans le bandeau de chaque carte de pièce (à côté du bouton supprimer)
   - Icône : 📋
   - Accessible en création ET en modification

2. **Mini-fenêtre de saisie**
   - Demande le nombre de copies (défaut : 1, max : 50)
   - Validation du nombre avant duplication
   - Annulation possible

3. **Contenu copié**
   - ✅ Type de vêtement
   - ✅ Mesures (toutes les valeurs)
   - ✅ Couturier assigné
   - ✅ Prix de confection (MontantCouture)
   - ✅ Description

4. **Contenu NON copié**
   - ❌ Photo (chaque pièce a sa propre photo)
   - ❌ Statut (nouvelle pièce = "A faire")
   - ❌ Commission (sera calculée à la validation)
   - ❌ DateTerminee (pièce neuve)

5. **Comportement**
   - Les copies s'ajoutent **directement sous la pièce d'origine**
   - Toutes les cartes sont renumérotées automatiquement
   - Le **total de la commande** se met à jour en temps réel
   - En mode création : déclenche la sauvegarde du brouillon

6. **Règles de verrouillage (Étape 2)**
   - Respecte les mêmes règles que l'ajout de pièces
   - **Impossible** de dupliquer sur une commande où l'ajout de pièces est interdit
   - Message d'erreur explicite avec le motif du blocage

### Implémentation technique

#### Méthodes principales
- `BtnDupliquerCartePiece_Click()` : handler du bouton, ouvre la mini-fenêtre
- `DupliquerPiece(Cartepiece, int)` : duplique N fois une carte
- `AjouterCarte(PieceCommande?, int?)` : surcharge avec position d'insertion
- `RenumeroterCartes()` : met à jour les numéros après insertion
- `RecalculerTotalCommande()` : recalcule le prix total
- `RafraichirAffichageMateriauxCarte()` : affiche la liste des matériaux

#### Contrôles ajoutés (XAML)
- Bandeau coloré en haut de chaque carte avec boutons
- Bouton "Dupliquer" avec tooltip explicatif
- Badge numéro de pièce (mis à jour dynamiquement)

### Tests

#### Tests brouillon (`Etape5_BrouillonDuplicationTests.cs`)
- ✅ `Brouillon01_SauvegarderEtCharger` : cycle complet save/load
- ✅ `Brouillon02_UnSeulBrouillonParEmploye` : remplacement automatique
- ✅ `Brouillon03_BrouillonsIndependantsParEmploye` : isolation par utilisateur
- ✅ `Brouillon04_SupprimerBrouillon` : suppression correcte
- ✅ `Brouillon05_ContenuComplet` : tous les champs sauvegardés
- ✅ `Brouillon06_PasDePhotoBase64` : chemins uniquement, pas de données volumineuses

#### Tests duplication
- ✅ `Duplication01_CopieSimple` : vérifie ce qui est copié/non copié
- ✅ `Duplication02_MultiplesCopies` : maximum 50 copies
- ✅ `Duplication03_InsertionSousOrigine` : position correcte
- ✅ `Duplication04_TotalRecalcule` : recalcul automatique
- ✅ `Duplication05_VerrouillageRespect` : respecte les règles de l'Étape 2

## Cas d'usage

### Scénario 1 : Secrétaire commence une commande, s'interrompt
1. La secrétaire ouvre "Nouvelle Commande"
2. Sélectionne le client, ajoute 2 pièces avec mesures
3. Son téléphone sonne, elle ferme l'application
4. ✅ Le brouillon est automatiquement sauvegardé
5. Elle revient 1 heure plus tard
6. ✅ Bandeau "Vous avez une commande en cours (Mme Diop, 14:35)"
7. Elle clique sur "Reprendre"
8. ✅ Tout le formulaire est restauré : client, pièces, mesures, prix

### Scénario 2 : Commande de 10 pantalons identiques
1. Le client commande 10 pantalons même modèle, même mesures
2. La secrétaire crée la première pièce avec toutes les mesures
3. Elle clique sur "📋 Dupliquer" → saisit "9"
4. ✅ 9 copies sont créées instantanément avec les mêmes mesures
5. ✅ Le total passe de 10 000 à 100 000 FCFA automatiquement
6. Elle enregistre la commande
7. ✅ Le brouillon est automatiquement supprimé

### Scénario 3 : Boss modifie une commande terminée
1. Le Boss ouvre une commande déjà terminée (Étape 2 : verrouillée)
2. Il veut dupliquer une pièce
3. ✅ Le bouton "Dupliquer" refuse l'action
4. ✅ Message : "Impossible de dupliquer : pièces déjà livrées ou en commission"

## Architecture

### Base de données
```sql
CREATE TABLE BrouillonsCommande (
    IdBrouillon INTEGER PRIMARY KEY AUTOINCREMENT,
    IdEmploye INTEGER NOT NULL UNIQUE,  -- Un seul brouillon par utilisateur
    ContenuJson TEXT NOT NULL,          -- Sérialisation JSON complète
    DateMiseAJour TEXT NOT NULL         -- DateTime SQLite
);
```

### JSON du brouillon (exemple)
```json
{
  "idClient": 42,
  "nomClient": "Madame Diop",
  "acompte": 15000,
  "dateFin": "2026-10-23T00:00:00",
  "heureFin": "14:00:00",
  "notes": "",
  "pieces": [
    {
      "idTypeVetement": 1,
      "typeVetement": "Pantalon (10000 FCFA)",
      "idCouturier": 3,
      "prix": 12000,
      "description": "Coupe slim avec poches",
      "cheminPhoto": "C:\\Photos\\pantalon_ref.jpg",
      "mesures": [
        { "nom": "Longueur", "valeur": "102" },
        { "nom": "Tour de taille", "valeur": "88" }
      ],
      "materiaux": [
        { "nom": "Boutons dorés", "prix": 500, "quantite": 4 }
      ]
    }
  ]
}
```

## Points d'attention

### Performance
- ✅ Débounce de 3s évite les sauvegardes intempestives
- ✅ Opération asynchrone, pas de freeze de l'UI
- ✅ Pas de photos en base64 (seulement les chemins)

### Fiabilité
- ✅ Sauvegarde à la fermeture de la page (hook Unloaded)
- ✅ Un seul brouillon par utilisateur (index unique en base)
- ✅ Brouillon isolé par employé (pas de conflit multi-utilisateur)

### Sécurité
- ✅ Pas de création fantôme de commande
- ✅ Pas d'impact sur la trésorerie ou les stats
- ✅ Respecte les règles de verrouillage de l'Étape 2

## Migration

Pour appliquer l'Étape 5 :

1. **Compiler** le projet (la migration est incluse)
2. **Démarrer** l'application (migration automatique)
3. **Vérifier** : aller dans Commandes → commencer une commande → fermer
4. **Rouvrir** : le bandeau "Reprendre / Abandonner" doit apparaître
5. **Tester** la duplication : créer une pièce → cliquer 📋 → saisir "3"

Aucune action manuelle requise, tout est automatique ! 🎉
