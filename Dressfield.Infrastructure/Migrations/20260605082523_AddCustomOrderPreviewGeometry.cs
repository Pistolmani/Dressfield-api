using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dressfield.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCustomOrderPreviewGeometry : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "CanvasHeight",
                table: "CustomOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "CanvasWidth",
                table: "CustomOrders",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ClothingSize",
                table: "CustomOrders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ColorHex",
                table: "CustomOrders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ProductTypeId",
                table: "CustomOrders",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<decimal>(
                name: "Angle",
                table: "CustomOrderDesigns",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ScaleX",
                table: "CustomOrderDesigns",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "ScaleY",
                table: "CustomOrderDesigns",
                type: "decimal(65,30)",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Side",
                table: "CustomOrderDesigns",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CanvasHeight",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "CanvasWidth",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "ClothingSize",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "ColorHex",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "ProductTypeId",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "Angle",
                table: "CustomOrderDesigns");

            migrationBuilder.DropColumn(
                name: "ScaleX",
                table: "CustomOrderDesigns");

            migrationBuilder.DropColumn(
                name: "ScaleY",
                table: "CustomOrderDesigns");

            migrationBuilder.DropColumn(
                name: "Side",
                table: "CustomOrderDesigns");
        }
    }
}
