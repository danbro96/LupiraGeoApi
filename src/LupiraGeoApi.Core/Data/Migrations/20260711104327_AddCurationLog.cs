using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace LupiraGeoApi.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddCurationLog : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CurationLog",
                schema: "geo",
                columns: table => new
                {
                    Seq = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PlaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Action = table.Column<string>(type: "text", nullable: false),
                    ActorPrincipalId = table.Column<Guid>(type: "uuid", nullable: true),
                    At = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    RelatedPlaceId = table.Column<Guid>(type: "uuid", nullable: true),
                    Detail = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CurationLog", x => x.Seq);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CurationLog_At",
                schema: "geo",
                table: "CurationLog",
                column: "At");

            migrationBuilder.CreateIndex(
                name: "IX_CurationLog_PlaceId",
                schema: "geo",
                table: "CurationLog",
                column: "PlaceId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CurationLog",
                schema: "geo");
        }
    }
}
