using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdmApi.Migrations
{
    /// <inheritdoc />
    public partial class RepairDatasetStatusAndTags : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Status",
                table: "Datasets",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string[]>(
                name: "Tags",
                table: "Datasets",
                type: "text[]",
                nullable: false,
                defaultValue: new string[0]);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "Tags",
                table: "Datasets");
        }
    }
}
