using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations;

public partial class DirectCallOutcomes : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(name: "SystemCallEndReason", table: "ChatMessages", type: "INTEGER", nullable: true);
        migrationBuilder.AddColumn<int>(name: "SystemCallDurationSeconds", table: "ChatMessages", type: "INTEGER", nullable: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "SystemCallEndReason", table: "ChatMessages");
        migrationBuilder.DropColumn(name: "SystemCallDurationSeconds", table: "ChatMessages");
    }
}
