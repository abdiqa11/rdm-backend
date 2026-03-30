using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetVersionFiles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ContentHashSha256",
                table: "DatasetVersions");

            migrationBuilder.DropColumn(
                name: "SizeBytes",
                table: "DatasetVersions");

            migrationBuilder.DropColumn(
                name: "StorageKey",
                table: "DatasetVersions");

            migrationBuilder.CreateTable(
                name: "DatasetVersionFiles",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DatasetVersionId = table.Column<Guid>(type: "uuid", nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    RelativePath = table.Column<string>(type: "text", nullable: true),
                    ObjectKey = table.Column<string>(type: "text", nullable: false),
                    ContentHashSha256 = table.Column<string>(type: "text", nullable: true),
                    SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    ContentType = table.Column<string>(type: "text", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DatasetVersionFiles", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DatasetVersionFiles_DatasetVersions_DatasetVersionId",
                        column: x => x.DatasetVersionId,
                        principalTable: "DatasetVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_DatasetVersionFiles_DatasetVersionId",
                table: "DatasetVersionFiles",
                column: "DatasetVersionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DatasetVersionFiles");

            migrationBuilder.AddColumn<string>(
                name: "ContentHashSha256",
                table: "DatasetVersions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SizeBytes",
                table: "DatasetVersions",
                type: "bigint",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StorageKey",
                table: "DatasetVersions",
                type: "text",
                nullable: true);
        }
    }
}
