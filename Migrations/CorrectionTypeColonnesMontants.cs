using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    /// <summary>
    /// CORRECTIF CRITIQUE (incohérence silencieuse entre le modèle C# et la base) :
    ///
    /// Tous les champs représentant de l'argent (Commande.MontantTotal,
    /// Paiement.MontantPaye / MontantTotalCommande / ResteAvantPaiement,
    /// Commission.BaseMontant / MontantCommission / Pourcentage,
    /// TypeVetement.PrixBase) sont déclarés en C# comme `decimal` avec
    /// `[Column(TypeName = "TEXT")]` — un choix explicite et commenté dans le
    /// code ("decimal : type exact pour l'argent, pas de dérive binaire comme
    /// double"). C'est la bonne pratique : SQLite n'a pas de type décimal
    /// natif, et le stocker en TEXT (chaîne exacte) évite les erreurs
    /// d'arrondi binaire inhérentes à REAL/double (ex: 0.1 + 0.2 != 0.3).
    ///
    /// MAIS la toute première migration (InitialCreate) a créé ces 8 colonnes
    /// avec le type physique SQLite "REAL" (double précision binaire), et
    /// aucune migration n'a jamais été générée après le passage à `decimal`
    /// dans les modèles pour mettre à jour le schéma réel en conséquence.
    ///
    /// Conséquence concrète et SILENCIEUSE (aucune exception n'est levée) :
    /// une colonne SQLite déclarée REAL a une "affinité REAL" — quand on lui
    /// insère une valeur textuelle qui ressemble à un nombre (ce que fait
    /// exactement le convertisseur decimal->TEXT d'EF Core), SQLite la
    /// convertit silencieusement en flottant binaire avant de la stocker.
    /// Autrement dit : malgré tout le soin apporté côté C# à utiliser
    /// `decimal` pour l'argent, chaque montant enregistré depuis le début
    /// est en réalité repassé par un flottant binaire au moment du stockage
    /// physique, ce qui peut produire des écarts de type 1499.9999999999998
    /// FCFA au lieu de 1500 FCFA sur des rapports/totaux cumulés — exactement
    /// le problème que le passage à `decimal` était censé éliminer.
    ///
    /// Cette migration force la reconstruction des tables concernées avec le
    /// type physique TEXT (affinité TEXT : SQLite conserve alors la chaîne
    /// exacte telle quelle, sans conversion). SQLite ne supportant pas
    /// ALTER COLUMN nativement, EF Core 8 gère cela automatiquement en
    /// reconstruisant chaque table (nouvelle table -> copie des données ->
    /// suppression de l'ancienne -> renommage), en conservant les données
    /// déjà présentes.
    ///
    /// IMPORTANT : cette migration corrige le type de stockage pour TOUTES
    /// les écritures FUTURES. Les valeurs déjà enregistrées AVANT cette
    /// migration ont pu accumuler un arrondi flottant historique qui n'est
    /// pas "réparé" rétroactivement par un simple changement de type (la
    /// valeur flottante déjà arrondie est copiée telle quelle, seulement
    /// reformatée en texte). Il est fortement recommandé de :
    ///   1) faire une sauvegarde de la base AVANT d'appliquer cette migration
    ///      (voir Services/BackupService.cs, ou une copie manuelle du fichier
    ///      .db dans %LOCALAPPDATA%\GestionCoutureApp),
    ///   2) vérifier après coup les totaux de quelques commandes/paiements
    ///      anciens pour repérer d'éventuels écarts hérités et les corriger
    ///      manuellement si besoin.
    /// </summary>
    public partial class CorrectionTypeColonnesMontants : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<decimal>(
                name: "PrixBase",
                table: "TypesVetements",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "Pourcentage",
                table: "Commissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "MontantCommission",
                table: "Commissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "BaseMontant",
                table: "Commissions",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "MontantTotal",
                table: "Commandes",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "ResteAvantPaiement",
                table: "Paiements",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "MontantTotalCommande",
                table: "Paiements",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");

            migrationBuilder.AlterColumn<decimal>(
                name: "MontantPaye",
                table: "Paiements",
                type: "TEXT",
                nullable: false,
                oldClrType: typeof(double),
                oldType: "REAL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<double>(
                name: "PrixBase",
                table: "TypesVetements",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "Pourcentage",
                table: "Commissions",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "MontantCommission",
                table: "Commissions",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "BaseMontant",
                table: "Commissions",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "MontantTotal",
                table: "Commandes",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "ResteAvantPaiement",
                table: "Paiements",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "MontantTotalCommande",
                table: "Paiements",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");

            migrationBuilder.AlterColumn<double>(
                name: "MontantPaye",
                table: "Paiements",
                type: "REAL",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "TEXT");
        }
    }
}
