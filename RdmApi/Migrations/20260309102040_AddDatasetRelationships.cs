using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace RdmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DatasetRelationships",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    SourceDatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    TargetDatasetId = table.Column<Guid>(type: "uuid", nullable: false),
                    RelationType = table.Column<string>(type: "text", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetRelationships_Datasets_SourceDatasetId",
                        column: x => x.SourceDatasetId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DatasetRelationships_Datasets_TargetDatasetId",
                        column: x => x.TargetDatasetId,
                        principalTable: "Datasets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRelationships_SourceDatasetId",
                table: "DatasetRelationships",
                column: "SourceDatasetId");

            migrationBuilder.CreateIndex(
                name: "IX_DatasetRelationships_TargetDatasetId",
                table: "DatasetRelationships",
                column: "TargetDatasetId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetRelationships");
        }
    }
}
