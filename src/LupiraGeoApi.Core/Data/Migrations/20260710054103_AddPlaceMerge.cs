using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace LupiraGeoApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddPlaceMerge : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "MergedIntoId",
                schema: "geo",
                table: "Places",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Places_MergedIntoId",
                schema: "geo",
                table: "Places",
                column: "MergedIntoId");

            migrationBuilder.AddForeignKey(
                name: "FK_Places_Places_MergedIntoId",
                schema: "geo",
                table: "Places",
                column: "MergedIntoId",
                principalSchema: "geo",
                principalTable: "Places",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Places_Places_MergedIntoId",
                schema: "geo",
                table: "Places");

            migrationBuilder.DropIndex(
                name: "IX_Places_MergedIntoId",
                schema: "geo",
                table: "Places");

            migrationBuilder.DropColumn(
                name: "MergedIntoId",
                schema: "geo",
                table: "Places");
        }
    }
}
