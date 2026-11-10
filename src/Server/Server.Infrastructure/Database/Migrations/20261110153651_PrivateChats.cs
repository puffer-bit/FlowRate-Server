using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class PrivateChats : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DirectKey",
                table: "ChatSessions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "ChatSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "MemberIdsJson",
                table: "ChatSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_DirectKey",
                table: "ChatSessions",
                column: "DirectKey",
                unique: true,
                filter: "\"DirectKey\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatSessions_DirectKey",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "DirectKey",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "Kind",
                table: "ChatSessions");

            migrationBuilder.DropColumn(
                name: "MemberIdsJson",
                table: "ChatSessions");
        }
    }
}
