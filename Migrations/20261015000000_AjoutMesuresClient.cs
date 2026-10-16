using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <summary>
    /// Étape 4 — Mesures réutilisables.
    /// Crée la table MesuresClient : fiche de mesures par client et type de vêtement.
    ///
    /// ARCHITECTURE :
    ///   - Cette table est la SOURCE DE VÉRITÉ pour les mesures réutilisables.
    ///   - Les mesures dans PiecesCommande restent des COPIES FIGÉES (historique).
    ///   - Index unique (IdClient, TypeVetement, NomMesure) garantit une seule valeur
    ///     par triplet ; un UPSERT remplace la valeur au lieu d'insérer un doublon.
    ///
    /// NOTE ARCHITECTURE (pattern projet) : cette migration est un no-op dans Up().
    /// Le DDL réel est appliqué par AppliquerMigrationsManquantes() dans App.cs
    /// (via CREATE TABLE IF NOT EXISTS idempotent) pour contourner la limitation
    /// SQLite qui ne supporte pas CREATE TABLE IF NOT EXISTS dans EF Core Migrate().
    /// </summary>
    public partial class AjoutMesuresClient : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Intentionnellement vide — voir commentaire de classe.
            // Le DDL est appliqué par AppliquerMigrationsManquantes() dans App.cs.
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "MesuresClient");
        }
    }
}
