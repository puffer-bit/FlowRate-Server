using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations;

public partial class RemoveBanHistory : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Restore accounts before removing the records needed to recover their previous state.
        migrationBuilder.Sql("""
            UPDATE UserEntities AS u
            SET State = COALESCE((
                SELECT CASE WHEN b.PreviousState = 2 THEN 2 ELSE 3 END
                FROM GlobalBans b
                WHERE b.CoordinatorId = u.CoordinatorInstanceId AND b.Kind = 0 AND b.UserId = u.Id
                ORDER BY b.CreatedAt DESC LIMIT 1
            ), 3)
            WHERE u.State = 1
                AND EXISTS (SELECT 1 FROM GlobalBans b
                    WHERE b.CoordinatorId = u.CoordinatorInstanceId AND b.Kind = 0 AND b.UserId = u.Id)
                AND NOT EXISTS (SELECT 1 FROM GlobalBans b
                    WHERE b.CoordinatorId = u.CoordinatorInstanceId AND b.Kind = 0 AND b.UserId = u.Id
                        AND b.RevokedAt IS NULL
                        AND (b.ExpiresAt IS NULL OR julianday(b.ExpiresAt) > julianday('now')));
            DELETE FROM GlobalBans
            WHERE RevokedAt IS NOT NULL OR julianday(ExpiresAt) <= julianday('now');
            """);
        migrationBuilder.DropColumn(name: "RevokedAt", table: "GlobalBans");
        migrationBuilder.DropColumn(name: "RevokedBy", table: "GlobalBans");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<DateTime>(name: "RevokedAt", table: "GlobalBans", type: "TEXT", nullable: true);
        migrationBuilder.AddColumn<int>(name: "RevokedBy", table: "GlobalBans", type: "INTEGER", nullable: true);
    }
}
