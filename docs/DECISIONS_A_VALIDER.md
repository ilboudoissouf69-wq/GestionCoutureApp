# Décisions métier et techniques à valider

> Ce fichier liste les décisions prises de façon conservative pendant les phases de
> correction. Chaque entrée indique l'option retenue par défaut, les alternatives et
> ce qui reste à confirmer avec le propriétaire.

---

## Phase 1 — Sécurité critique

### DEC-01 — Stockage du compteur d'échecs d'authentification

**Problème :** La spec demande de persister le compteur d'échecs en base pour résister
aux redémarrages de l'application.

**Option retenue (conservative) :** Ajout de colonnes sur la table `Employes` :
- `NbEchecConnexion INTEGER NOT NULL DEFAULT 0`
- `DateVerrouJusqua TEXT` (UTC)

**Alternative :** Nouvelle table `TentativesConnexion(IdEmploye, NbEchecs, DateVerrouJusqua)`.

**Justification du choix :** Une table séparée serait plus propre mais nécessite une
migration et une requête supplémentaire à chaque login. Les colonnes sur `Employes`
sont idiomatiques et l'ensemble ne contient pas de données personnelles sensibles.

**À valider :** Acceptez-vous l'ajout de ces deux colonnes sur `Employes` ?

---

### DEC-02 — Paliers de verrouillage progressif

**Option retenue :**
- 1er verrouillage (≥ 5 échecs) : 30 secondes
- 2e verrouillage (≥ 10 échecs cumulatifs) : 1 minute
- 3e verrouillage (≥ 15 échecs cumulatifs) : 5 minutes
- Au-delà (≥ 20 échecs) : 15 minutes

Les compteurs ne se réinitialisent pas après chaque verrouillage expiré ; ils ne
se réinitialisent qu'après une connexion réussie. Cela dissuade les attaques qui
espacent les essais pour contourner chaque palier.

**À valider :** Ces seuils sont-ils adaptés à l'usage quotidien ?
(une secrétaire qui fait régulièrement des fautes de frappe ne doit pas être bloquée)

---

### DEC-03 — Longueur minimale du mot de passe

**Option retenue :** 10 caractères (conformément à la spec).

**État actuel :** 6 caractères dans `AuthService.ChangerMotDePasse`.

**À valider :** Les utilisateurs existants avec un mot de passe de 6 à 9 caractères
pourront continuer à se connecter mais seront invités à changer leur mot de passe
lors du prochain changement forcé ou volontaire. OK ?

---

### DEC-04 — Mots de passe interdits (liste noire)

**Liste retenue (combinaisons rejetées si le mdp *est* ou *contient* ces chaînes) :**
`boss123`, `123456`, `password`, `motdepasse`, `admin`, `boss`, `secret`, `couture`,
`atelier`, `retouche`, `ilboudo`, `issouf`, `choco`.

**À valider :** Faut-il ajouter des mots spécifiques à l'atelier (nom client, etc.) ?

---

### DEC-05 — Vérification du rôle dans PaiementService.Ajouter

**Option retenue :** Seuls les employés avec le rôle Boss ou Secrétaire, et le statut
Actif, peuvent enregistrer un paiement. Un Couturier ou un employé inactif est rejeté.

**À valider :** Y a-t-il des cas où un Couturier devrait enregistrer un paiement ?

---

### DEC-06 — Fenêtre de configuration initiale au premier démarrage

**Option retenue :** Au premier lancement (base vide), une fenêtre `ConfigurationInitialeWindow`
demande l'identifiant et le mot de passe du compte Boss. Le mot de passe par défaut
`boss123` est supprimé. La fenêtre exige au minimum 10 caractères et vérifie la liste
noire.

**À valider :** L'identifiant `boss` est-il conservé par défaut ou le propriétaire
veut-il choisir son propre identifiant lors de la configuration initiale ?
*(Option retenue : le propriétaire choisit son propre identifiant.)*

---

## Phase 2 — Intégrité financière

*(À compléter lors de la Phase 2)*

---

## Phase 3 — Audit

*(À compléter lors de la Phase 3)*

---

*Dernière mise à jour : Phase 1*
