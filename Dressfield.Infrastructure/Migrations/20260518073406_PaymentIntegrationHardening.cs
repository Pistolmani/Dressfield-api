using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Dressfield.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class PaymentIntegrationHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Orders_BogOrderId",
                table: "Orders");

            migrationBuilder.AddColumn<int>(
                name: "MaxUses",
                table: "PromoCodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxUsesPerUser",
                table: "PromoCodes",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "UsedCount",
                table: "PromoCodes",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VariantId",
                table: "OrderItems",
                type: "int",
                nullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "BogOrderKey",
                table: "CustomOrders",
                type: "varchar(64)",
                maxLength: 64,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "BogOrderId",
                table: "CustomOrders",
                type: "varchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "longtext",
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CustomOrderStatusLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    CustomOrderId = table.Column<int>(type: "int", nullable: false),
                    FromStatus = table.Column<int>(type: "int", nullable: false),
                    ToStatus = table.Column<int>(type: "int", nullable: false),
                    ChangedByUserId = table.Column<string>(type: "longtext", nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Notes = table.Column<string>(type: "varchar(1000)", maxLength: 1000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ChangedAt = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CustomOrderStatusLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CustomOrderStatusLogs_CustomOrders_CustomOrderId",
                        column: x => x.CustomOrderId,
                        principalTable: "CustomOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BogOrderId",
                table: "Orders",
                column: "BogOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrders_BogOrderId",
                table: "CustomOrders",
                column: "BogOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrderStatusLogs_ChangedAt",
                table: "CustomOrderStatusLogs",
                column: "ChangedAt");

            migrationBuilder.CreateIndex(
                name: "IX_CustomOrderStatusLogs_CustomOrderId",
                table: "CustomOrderStatusLogs",
                column: "CustomOrderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CustomOrderStatusLogs");

            migrationBuilder.DropIndex(
                name: "IX_Orders_BogOrderId",
                table: "Orders");

            migrationBuilder.DropIndex(
                name: "IX_CustomOrders_BogOrderId",
                table: "CustomOrders");

            migrationBuilder.DropColumn(
                name: "MaxUses",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "MaxUsesPerUser",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "UsedCount",
                table: "PromoCodes");

            migrationBuilder.DropColumn(
                name: "VariantId",
                table: "OrderItems");

            migrationBuilder.AlterColumn<string>(
                name: "BogOrderKey",
                table: "CustomOrders",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(64)",
                oldMaxLength: 64,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AlterColumn<string>(
                name: "BogOrderId",
                table: "CustomOrders",
                type: "longtext",
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(100)",
                oldMaxLength: 100,
                oldNullable: true)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_Orders_BogOrderId",
                table: "Orders",
                column: "BogOrderId");
        }
    }
}
