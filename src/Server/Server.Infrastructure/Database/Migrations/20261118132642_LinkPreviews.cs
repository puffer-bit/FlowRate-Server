using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class LinkPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsLinkPreviewDisabled",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<int>(
                name: "LinkPreviewId",
                table: "ChatMessages",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "LinkPreviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Url = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: false),
                    SiteName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "TEXT", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true),
                    ImageMimeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    ImageWidth = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageHeight = table.Column<int>(type: "INTEGER", nullable: false),
                    ImageSize = table.Column<long>(type: "INTEGER", nullable: false),
                    IsLargeImage = table.Column<bool>(type: "INTEGER", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkPreviews", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_LinkPreviewId",
                table: "ChatMessages",
                column: "LinkPreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_CoordinatorInstanceId_FetchedAt",
                table: "LinkPreviews",
                columns: new[] { "CoordinatorInstanceId", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_CoordinatorInstanceId_Url_FetchedAt",
                table: "LinkPreviews",
                columns: new[] { "CoordinatorInstanceId", "Url", "FetchedAt" });

            migrationBuilder.AddForeignKey(
                name: "FK_ChatMessages_LinkPreviews_LinkPreviewId",
                table: "ChatMessages",
                column: "LinkPreviewId",
                principalTable: "LinkPreviews",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatMessages_LinkPreviews_LinkPreviewId",
                table: "ChatMessages");

            migrationBuilder.DropTable(
                name: "LinkPreviews");

            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_LinkPreviewId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "IsLinkPreviewDisabled",
                table: "ChatMessages");

            migrationBuilder.DropColumn(
                name: "LinkPreviewId",
                table: "ChatMessages");
        }
    }
}
