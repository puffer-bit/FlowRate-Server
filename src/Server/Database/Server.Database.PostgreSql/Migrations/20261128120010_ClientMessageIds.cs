using System;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.PostgreSql.Migrations
{
    /// <summary>
    /// The id the author's client gives a message, unique per chat and author, so a message sent again is
    /// found instead of stored twice. Older messages keep NULL. Written by hand, with no target model: the
    /// model snapshot is updated alongside.
    /// </summary>
    [DbContext(typeof(ServerDbContext))]
    [Migration("20261128120010_ClientMessageIds")]
    public partial class ClientMessageIds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientMessageId",
                table: "ChatMessages",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatId_SenderUserId_ClientMessageId",
                table: "ChatMessages",
                columns: new[] { "ChatId", "SenderUserId", "ClientMessageId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ChatMessages_ChatId_SenderUserId_ClientMessageId",
                table: "ChatMessages");

            migrationBuilder.DropColumn(name: "ClientMessageId", table: "ChatMessages");
        }
    }
}
