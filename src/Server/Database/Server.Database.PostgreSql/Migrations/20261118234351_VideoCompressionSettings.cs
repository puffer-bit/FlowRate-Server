using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class VideoCompressionSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MediaAllowOriginalVideos",
                table: "CoordinatorInstances",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "MediaVideoMaxSide",
                table: "CoordinatorInstances",
                type: "integer",
                nullable: false,
                defaultValue: 720);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MediaAllowOriginalVideos",
                table: "CoordinatorInstances");

            migrationBuilder.DropColumn(
                name: "MediaVideoMaxSide",
                table: "CoordinatorInstances");
        }
    }
}
