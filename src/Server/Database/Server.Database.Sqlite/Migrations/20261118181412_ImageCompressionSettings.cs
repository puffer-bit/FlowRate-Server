using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.Sqlite.Migrations
{
    /// <inheritdoc />
    public partial class ImageCompressionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MediaAllowOriginalImages",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaImageMaxSide",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 2560);

            migrationBuilder.AddColumn<int>(
                name: "MediaImageQuality",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 85);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaAllowOriginalImages",
                table: "CoordinatorInstances");

            migrationBuilder.DropColumn(
                name: "MediaImageMaxSide",
                table: "CoordinatorInstances");

            migrationBuilder.DropColumn(
                name: "MediaImageQuality",
                table: "CoordinatorInstances");
        }
    }
}
