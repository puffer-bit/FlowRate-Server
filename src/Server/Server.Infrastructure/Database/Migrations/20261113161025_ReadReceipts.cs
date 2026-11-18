using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ReadReceipts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "ReactedAt",
                table: "ChatMessageReactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "ChatReadMarks",
                columns: table => new
                {
                    ChatId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    UpToMessageId = table.Column<int>(type: "INTEGER", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReadMarks", x => new { x.ChatId, x.UserId, x.UpToMessageId });
                    table.ForeignKey(
                        name: "FK_ChatReadMarks_ChatSessions_ChatId",
                        column: x => x.ChatId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadMarks_ChatId_UpToMessageId",
                table: "ChatReadMarks",
                columns: new[] { "ChatId", "UpToMessageId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatReadMarks");

            migrationBuilder.DropColumn(
                name: "ReactedAt",
                table: "ChatMessageReactions");
        }
    }
}
