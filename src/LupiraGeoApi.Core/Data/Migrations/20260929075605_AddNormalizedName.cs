using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraGeoApi.Core.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddNormalizedName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                schema: "geo",
                table: "Places",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "NormalizedName",
                schema: "geo",
                table: "PlaceAliases",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_Places_NormalizedName",
                schema: "geo",
                table: "Places",
                column: "NormalizedName");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceAliases_NormalizedName",
                schema: "geo",
                table: "PlaceAliases",
                column: "NormalizedName");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Places_NormalizedName",
                schema: "geo",
                table: "Places");

            migrationBuilder.DropIndex(
                name: "IX_PlaceAliases_NormalizedName",
                schema: "geo",
                table: "PlaceAliases");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                schema: "geo",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "NormalizedName",
                schema: "geo",
                table: "PlaceAliases");
        }
    }
}
