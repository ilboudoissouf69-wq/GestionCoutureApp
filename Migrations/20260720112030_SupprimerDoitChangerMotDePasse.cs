using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GestionCoutureApp.Migrations
{
    /// <inheritdoc />
    public partial class SupprimerDoitChangerMotDePasse : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DoitChangerMotDePasse",
                table: "Employes");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "DoitChangerMotDePasse",
                table: "Employes",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);
        }
    }
}
