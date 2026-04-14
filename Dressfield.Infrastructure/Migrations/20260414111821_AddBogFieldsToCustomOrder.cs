using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dressfield.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBogFieldsToCustomOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BogOrderId",
                table: "CustomOrders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "BogOrderKey",
                table: "CustomOrders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BogOrderId",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "BogOrderKey",
                table: "CustomOrders");
        }
    }
}
