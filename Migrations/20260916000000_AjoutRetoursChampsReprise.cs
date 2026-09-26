using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class AjoutRetoursChampsReprise : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // ── NO-OP INTENTIONNEL ────────────────────────────────────────────
            //
            // CONTEXTE DU BUG CORRIGÉ (2026-09-26) :
            //
            // L'implémentation originale utilisait une méthode locale
            // AjouterColonneSiAbsente() basée sur suppressTransaction:true.
            // suppressTransaction ne supprime PAS les erreurs SQLite : il sort
            // simplement la commande de la transaction ambiante d'EF Core.
            // Conséquence : si un ALTER TABLE échouait (ex. "duplicate column
            // name: EstAnnule" parce qu'une exécution précédente partielle avait
            // déjà créé la colonne hors transaction), les colonnes déjà ajoutées
            // restaient committées en base MAIS la migration entière était marquée
            // "non appliquée" dans __EFMigrationsHistory. Au prochain lancement,
            // EF Core rejouait toute la migration depuis le début et plantait sur
            // le premier ALTER TABLE ADD COLUMN EstAnnule.
            //
            // DÉCISION :
            // La logique réelle d'ajout de ces colonnes vit maintenant à UN SEUL
            // endroit : App.cs → AppliquerMigrationsManquantes(), qui :
            //   1. utilise une connexion ADO.NET séparée (pas la connexion EF Core
            //      partagée qui peut avoir des locks)
            //   2. enveloppe chaque ALTER dans un try/catch individuel et silencieux
            //   3. enregistre la migration dans __EFMigrationsHistory uniquement
            //      après que TOUTES les colonnes ont été ajoutées avec succès
            //
            // Cette migration reste dans __EFMigrationsHistory pour compatibilité
            // avec les bases existantes. Elle n'exécute plus rien en Up().
            //
            // POUR UN UTILISATEUR EN PRODUCTION dont la migration est marquée
            // "non appliquée" malgré des colonnes partiellement présentes :
            //
            //   1. Ouvrez le fichier gestion_couture.db avec DB Browser for SQLite
            //      (https://sqlitebrowser.org/).
            //   2. Exécutez : SELECT * FROM __EFMigrationsHistory ORDER BY MigrationId;
            //   3. Si "20260916000000_AjoutRetoursChampsReprise" est absent mais que
            //      les colonnes EstAnnule / CheminPhotoDefaut existent sur la table
            //      Retours (vérifiable via PRAGMA table_info('Retours')), insérez
            //      manuellement :
            //        INSERT INTO __EFMigrationsHistory VALUES
            //          ('20260916000000_AjoutRetoursChampsReprise', '8.0.11');
            //   4. Relancez l'application — AppliquerMigrationsManquantes() appliquera
            //      les colonnes manquantes restantes en toute sécurité.
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // SQLite ne supporte pas DROP COLUMN de façon fiable sur toutes les versions.
            // Laissé vide intentionnellement — cohérent avec le Up() no-op.
        }
    }
}
