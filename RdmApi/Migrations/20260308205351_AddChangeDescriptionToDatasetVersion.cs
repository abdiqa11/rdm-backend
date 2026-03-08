using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace RdmApi.Migrations
{
    public partial class AddChangeDescriptionToDatasetVersion : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ChangeDescription",
                table: "DatasetVersions",
                type: "text",
                nullable: true);
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ChangeDescription",
                table: "DatasetVersions");
        }
    }
}