using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdmApi.Migrations
{
    /// <inheritdoc />
    public partial class AddDatasetOwnership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "OwnerEmail",
                table: "Datasets",
                type: "character varying(320)",
                maxLength: 320,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "OwnerId",
                table: "Datasets",
                type: "character varying(200)",
                maxLength: 200,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "OwnerEmail",
                table: "Datasets");

            migrationBuilder.DropColumn(
                name: "OwnerId",
                table: "Datasets");
        }
    }
}
