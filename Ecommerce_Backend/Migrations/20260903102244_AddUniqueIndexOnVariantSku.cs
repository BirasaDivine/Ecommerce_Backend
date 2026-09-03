using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Ecommerce_Backend.Migrations
{
    /// <inheritdoc />
    public partial class AddUniqueIndexOnVariantSku : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "Variants",
                type: "nvarchar(450)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(max)");

            migrationBuilder.CreateIndex(
                name: "IX_Variants_Sku",
                table: "Variants",
                column: "Sku",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Variants_Sku",
                table: "Variants");

            migrationBuilder.AlterColumn<string>(
                name: "Sku",
                table: "Variants",
                type: "nvarchar(max)",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(450)");
        }
    }
}
