using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;

#nullable disable

namespace LupiraGeoApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialGeo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "geo");

            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:pg_trgm", ",,")
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "AdminAreas",
                schema: "geo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Level = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    IsoCode = table.Column<string>(type: "text", nullable: true),
                    WithinAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    Centroid = table.Column<Point>(type: "geography (Point, 4326)", nullable: true),
                    GeonamesId = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminAreas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdminAreas_AdminAreas_WithinAreaId",
                        column: x => x.WithinAreaId,
                        principalSchema: "geo",
                        principalTable: "AdminAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Places",
                schema: "geo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CanonicalName = table.Column<string>(type: "text", nullable: false),
                    Kind = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "text", nullable: false),
                    Location = table.Column<Point>(type: "geography (Point, 4326)", nullable: true),
                    WithinAreaId = table.Column<Guid>(type: "uuid", nullable: true),
                    FormattedAddress = table.Column<string>(type: "text", nullable: true),
                    Source = table.Column<string>(type: "text", nullable: false),
                    Verified = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedByPrincipalId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Places", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Places_AdminAreas_WithinAreaId",
                        column: x => x.WithinAreaId,
                        principalSchema: "geo",
                        principalTable: "AdminAreas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "PlaceAliases",
                schema: "geo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Lang = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceAliases", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceAliases_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalSchema: "geo",
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PlaceExternalIds",
                schema: "geo",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Scheme = table.Column<string>(type: "text", nullable: false),
                    Value = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PlaceExternalIds", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PlaceExternalIds_Places_PlaceId",
                        column: x => x.PlaceId,
                        principalSchema: "geo",
                        principalTable: "Places",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAreas_Centroid",
                schema: "geo",
                table: "AdminAreas",
                column: "Centroid")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_AdminAreas_GeonamesId",
                schema: "geo",
                table: "AdminAreas",
                column: "GeonamesId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdminAreas_Name",
                schema: "geo",
                table: "AdminAreas",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_AdminAreas_WithinAreaId",
                schema: "geo",
                table: "AdminAreas",
                column: "WithinAreaId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceAliases_Name",
                schema: "geo",
                table: "PlaceAliases",
                column: "Name")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_PlaceAliases_PlaceId",
                schema: "geo",
                table: "PlaceAliases",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceExternalIds_PlaceId",
                schema: "geo",
                table: "PlaceExternalIds",
                column: "PlaceId");

            migrationBuilder.CreateIndex(
                name: "IX_PlaceExternalIds_Scheme_Value",
                schema: "geo",
                table: "PlaceExternalIds",
                columns: new[] { "Scheme", "Value" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Places_CanonicalName",
                schema: "geo",
                table: "Places",
                column: "CanonicalName")
                .Annotation("Npgsql:IndexMethod", "gin")
                .Annotation("Npgsql:IndexOperators", new[] { "gin_trgm_ops" });

            migrationBuilder.CreateIndex(
                name: "IX_Places_Category",
                schema: "geo",
                table: "Places",
                column: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Places_Location",
                schema: "geo",
                table: "Places",
                column: "Location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "IX_Places_WithinAreaId",
                schema: "geo",
                table: "Places",
                column: "WithinAreaId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PlaceAliases",
                schema: "geo");

            migrationBuilder.DropTable(
                name: "PlaceExternalIds",
                schema: "geo");

            migrationBuilder.DropTable(
                name: "Places",
                schema: "geo");

            migrationBuilder.DropTable(
                name: "AdminAreas",
                schema: "geo");
        }
    }
}
