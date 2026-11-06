using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations;

public partial class PinnedChatMessages : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>("PinnedAt", "ChatMessages", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>("PinnedByUserId", "ChatMessages", type: "INTEGER", nullable: true);
        migrationBuilder.CreateIndex("IX_ChatMessages_ChatId_Id", "ChatMessages", new[] { "ChatId", "Id" });
        migrationBuilder.CreateIndex("IX_ChatMessages_ChatId_PinnedAt", "ChatMessages", new[] { "ChatId", "PinnedAt" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex("IX_ChatMessages_ChatId_Id", "ChatMessages");
        migrationBuilder.DropIndex("IX_ChatMessages_ChatId_PinnedAt", "ChatMessages");
        migrationBuilder.DropColumn("PinnedAt", "ChatMessages");
        migrationBuilder.DropColumn("PinnedByUserId", "ChatMessages");
    }
}
