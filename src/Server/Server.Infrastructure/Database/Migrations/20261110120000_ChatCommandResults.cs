using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations;

public partial class ChatCommandResults : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>("TextContent_CommandKind", "ChatMessages", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("TextContent_CommandMinimum", "ChatMessages", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("TextContent_CommandMaximum", "ChatMessages", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>("TextContent_CommandValue", "ChatMessages", type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn("TextContent_CommandKind", "ChatMessages");
        migrationBuilder.DropColumn("TextContent_CommandMinimum", "ChatMessages");
        migrationBuilder.DropColumn("TextContent_CommandMaximum", "ChatMessages");
        migrationBuilder.DropColumn("TextContent_CommandValue", "ChatMessages");
    }
}
