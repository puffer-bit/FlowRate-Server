using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class NormalizeChatMembership : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatSessionMembers",
                columns: table => new
                {
                    ChatSessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "TEXT", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    AccountId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessionMembers", x => new { x.ChatSessionId, x.UserId });
                    table.CheckConstraint("CK_ChatSessionMembers_AccountId", "\"AccountId\" IS NULL OR \"AccountId\" = \"UserId\"");
                    table.ForeignKey(
                        name: "FK_ChatSessionMembers_ChatSessions_ChatSessionId",
                        column: x => x.ChatSessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatSessionMembers_UserEntities_AccountId",
                        column: x => x.AccountId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionMembers_AccountId",
                table: "ChatSessionMembers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionMembers_UserId_ChatSessionId",
                table: "ChatSessionMembers",
                columns: new[] { "UserId", "ChatSessionId" });

            // Read the old column before SQLite rebuilds ChatSessions without it.
            migrationBuilder.Sql("""
                INSERT INTO ChatSessionMembers (ChatSessionId, UserId, JoinedAt, AccountId)
                SELECT DISTINCT chat.Id, CAST(member.value AS INTEGER), CURRENT_TIMESTAMP, account.Id
                FROM ChatSessions AS chat
                JOIN json_each(chat.MemberIdsJson) AS member
                LEFT JOIN UserEntities AS account ON account.Id = CAST(member.value AS INTEGER)
                WHERE chat.Kind IN (1, 2) AND member.type = 'integer' AND member.value > 0;
                """);

            migrationBuilder.DropColumn(
                name: "MemberIdsJson",
                table: "ChatSessions");

        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "MemberIdsJson",
                table: "ChatSessions",
                type: "TEXT",
                nullable: false,
                defaultValue: "[]");

            migrationBuilder.Sql("""
                UPDATE ChatSessions SET MemberIdsJson = (
                    SELECT json_group_array(UserId) FROM (
                        SELECT UserId FROM ChatSessionMembers
                        WHERE ChatSessionId = ChatSessions.Id ORDER BY UserId
                    )
                );
                """);

            migrationBuilder.DropTable(
                name: "ChatSessionMembers");
        }
    }
}
