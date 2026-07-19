using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clients",
                columns: table => new
                {
                    IdClient = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", nullable: false),
                    Prenom = table.Column<string>(type: "TEXT", nullable: false),
                    Telephone = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clients", x => x.IdClient);
                });

            migrationBuilder.CreateTable(
                name: "Employes",
                columns: table => new
                {
                    IdEmploye = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", nullable: false),
                    Prenom = table.Column<string>(type: "TEXT", nullable: false),
                    Identifiant = table.Column<string>(type: "TEXT", nullable: false),
                    MotDePasse = table.Column<string>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", nullable: false, defaultValue: "Actif")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employes", x => x.IdEmploye);
                });

            migrationBuilder.CreateTable(
                name: "TypesVetements",
                columns: table => new
                {
                    IdTypeVetement = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Nom = table.Column<string>(type: "TEXT", nullable: false),
                    PrixBase = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TypesVetements", x => x.IdTypeVetement);
                });

            migrationBuilder.CreateTable(
                name: "Commissions",
                columns: table => new
                {
                    IdCommission = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdEmploye = table.Column<int>(type: "INTEGER", nullable: false),
                    NomEmployeSnapshot = table.Column<string>(type: "TEXT", nullable: false),
                    DateDebutPeriode = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateFinPeriode = table.Column<DateTime>(type: "TEXT", nullable: false),
                    BaseCalcul = table.Column<string>(type: "TEXT", nullable: false),
                    Pourcentage = table.Column<double>(type: "REAL", nullable: false),
                    BaseMontant = table.Column<double>(type: "REAL", nullable: false),
                    MontantCommission = table.Column<double>(type: "REAL", nullable: false),
                    NbCommandes = table.Column<int>(type: "INTEGER", nullable: false),
                    DateCalcul = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IdOperateur = table.Column<int>(type: "INTEGER", nullable: false),
                    NomOperateur = table.Column<string>(type: "TEXT", nullable: false),
                    EstAnnulee = table.Column<bool>(type: "INTEGER", nullable: false),
                    MotifAnnulation = table.Column<string>(type: "TEXT", nullable: true),
                    DateAnnulation = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NomAnnulateur = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commissions", x => x.IdCommission);
                    table.ForeignKey(
                        name: "FK_Commissions_Employes_IdEmploye",
                        column: x => x.IdEmploye,
                        principalTable: "Employes",
                        principalColumn: "IdEmploye",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "DescriptionsCourantes",
                columns: table => new
                {
                    IdDescription = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Texte = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    IdTypeVetement = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DescriptionsCourantes", x => x.IdDescription);
                    table.ForeignKey(
                        name: "FK_DescriptionsCourantes_TypesVetements_IdTypeVetement",
                        column: x => x.IdTypeVetement,
                        principalTable: "TypesVetements",
                        principalColumn: "IdTypeVetement",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MesuresRequises",
                columns: table => new
                {
                    IdMesureRequise = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdTypeVetement = table.Column<int>(type: "INTEGER", nullable: false),
                    NomMesure = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MesuresRequises", x => x.IdMesureRequise);
                    table.ForeignKey(
                        name: "FK_MesuresRequises_TypesVetements_IdTypeVetement",
                        column: x => x.IdTypeVetement,
                        principalTable: "TypesVetements",
                        principalColumn: "IdTypeVetement",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Commandes",
                columns: table => new
                {
                    IdCommande = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdClient = table.Column<int>(type: "INTEGER", nullable: false),
                    IdCouturier = table.Column<int>(type: "INTEGER", nullable: true),
                    TypeVetement = table.Column<string>(type: "TEXT", nullable: false),
                    DescriptionPrecision = table.Column<string>(type: "TEXT", nullable: false),
                    CheminPhoto = table.Column<string>(type: "TEXT", nullable: false),
                    DateDebut = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DateFin = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Statut = table.Column<string>(type: "TEXT", nullable: false),
                    MontantTotal = table.Column<double>(type: "REAL", nullable: false),
                    HeureDebut = table.Column<TimeSpan>(type: "TEXT", nullable: false),
                    HeureFin = table.Column<TimeSpan>(type: "TEXT", nullable: true),
                    IdCommission = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Commandes", x => x.IdCommande);
                    table.ForeignKey(
                        name: "FK_Commandes_Clients_IdClient",
                        column: x => x.IdClient,
                        principalTable: "Clients",
                        principalColumn: "IdClient",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commandes_Commissions_IdCommission",
                        column: x => x.IdCommission,
                        principalTable: "Commissions",
                        principalColumn: "IdCommission",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Commandes_Employes_IdCouturier",
                        column: x => x.IdCouturier,
                        principalTable: "Employes",
                        principalColumn: "IdEmploye");
                });

            migrationBuilder.CreateTable(
                name: "Mesures",
                columns: table => new
                {
                    IdMesure = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdCommande = table.Column<int>(type: "INTEGER", nullable: false),
                    NomMesure = table.Column<string>(type: "TEXT", nullable: false),
                    Valeur = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Mesures", x => x.IdMesure);
                    table.ForeignKey(
                        name: "FK_Mesures_Commandes_IdCommande",
                        column: x => x.IdCommande,
                        principalTable: "Commandes",
                        principalColumn: "IdCommande",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Paiements",
                columns: table => new
                {
                    IdPaiement = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdCommande = table.Column<int>(type: "INTEGER", nullable: false),
                    MontantPaye = table.Column<double>(type: "REAL", nullable: false),
                    DatePaiement = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ModePaiement = table.Column<string>(type: "TEXT", nullable: false),
                    RecuNumero = table.Column<string>(type: "TEXT", nullable: false),
                    IdOperateur = table.Column<int>(type: "INTEGER", nullable: true),
                    NomOperateur = table.Column<string>(type: "TEXT", nullable: false),
                    EstAnnule = table.Column<bool>(type: "INTEGER", nullable: false),
                    MotifsAnnulation = table.Column<string>(type: "TEXT", nullable: true),
                    DateAnnulation = table.Column<DateTime>(type: "TEXT", nullable: true),
                    NomAnnulateur = table.Column<string>(type: "TEXT", nullable: true),
                    MontantTotalCommande = table.Column<double>(type: "REAL", nullable: false),
                    ResteAvantPaiement = table.Column<double>(type: "REAL", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Paiements", x => x.IdPaiement);
                    table.ForeignKey(
                        name: "FK_Paiements_Commandes_IdCommande",
                        column: x => x.IdCommande,
                        principalTable: "Commandes",
                        principalColumn: "IdCommande",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_IdClient",
                table: "Commandes",
                column: "IdClient");

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_IdCommission",
                table: "Commandes",
                column: "IdCommission");

            migrationBuilder.CreateIndex(
                name: "IX_Commandes_IdCouturier",
                table: "Commandes",
                column: "IdCouturier");

            migrationBuilder.CreateIndex(
                name: "IX_Commissions_IdEmploye",
                table: "Commissions",
                column: "IdEmploye");

            migrationBuilder.CreateIndex(
                name: "IX_DescriptionsCourantes_IdTypeVetement",
                table: "DescriptionsCourantes",
                column: "IdTypeVetement");

            migrationBuilder.CreateIndex(
                name: "IX_Employes_Identifiant",
                table: "Employes",
                column: "Identifiant",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Mesures_IdCommande",
                table: "Mesures",
                column: "IdCommande");

            migrationBuilder.CreateIndex(
                name: "IX_MesuresRequises_IdTypeVetement",
                table: "MesuresRequises",
                column: "IdTypeVetement");

            migrationBuilder.CreateIndex(
                name: "IX_Paiements_IdCommande",
                table: "Paiements",
                column: "IdCommande");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DescriptionsCourantes");

            migrationBuilder.DropTable(
                name: "Mesures");

            migrationBuilder.DropTable(
                name: "MesuresRequises");

            migrationBuilder.DropTable(
                name: "Paiements");

            migrationBuilder.DropTable(
                name: "TypesVetements");

            migrationBuilder.DropTable(
                name: "Commandes");

            migrationBuilder.DropTable(
                name: "Clients");

            migrationBuilder.DropTable(
                name: "Commissions");

            migrationBuilder.DropTable(
                name: "Employes");
        }
    }
}
