using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class GlobalBans : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GlobalBans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CoordinatorId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: true),
                    Address = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    Reason = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    PreviousState = table.Column<int>(type: "INTEGER", nullable: false),
                    RevokedBy = table.Column<int>(type: "INTEGER", nullable: true),
                    RevokedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalBans", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalBans_CoordinatorId_Kind_Address",
                table: "GlobalBans",
                columns: new[] { "CoordinatorId", "Kind", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalBans_CoordinatorId_Kind_UserId",
                table: "GlobalBans",
                columns: new[] { "CoordinatorId", "Kind", "UserId" });
            // Legacy records have no author, reason or creation time; label them explicitly rather than inventing one.
            migrationBuilder.Sql("""
                INSERT INTO GlobalBans (Id, CoordinatorId, Kind, UserId, Reason, CreatedBy, CreatedAt, PreviousState)
                SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' ||
                    hex(randomblob(2)) || '-' || hex(randomblob(6))), CoordinatorInstanceId, 0, Id,
                    'Legacy account ban (reason was not recorded)', -1, datetime('now'), 3
                FROM UserEntities WHERE State = 1;
                INSERT INTO GlobalBans (Id, CoordinatorId, Kind, Address, Reason, CreatedBy, CreatedAt, PreviousState)
                SELECT upper(hex(randomblob(4)) || '-' || hex(randomblob(2)) || '-' || hex(randomblob(2)) || '-' ||
                    hex(randomblob(2)) || '-' || hex(randomblob(6))), c.Id, 1, normalize_ip(j.value),
                    'Legacy IP ban (reason was not recorded)', -1, datetime('now'), 3
                FROM CoordinatorInstances c, json_each(c.BannedIPs) j WHERE normalize_ip(j.value) IS NOT NULL
                GROUP BY c.Id, normalize_ip(j.value);
                UPDATE CoordinatorInstances SET BannedIPs = '[]';
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GlobalBans");
        }
    }
}
