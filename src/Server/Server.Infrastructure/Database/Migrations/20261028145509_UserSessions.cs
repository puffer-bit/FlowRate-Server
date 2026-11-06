using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class UserSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<byte[]>(
                name: "IdentityHash",
                table: "UserEntities",
                type: "BLOB",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0]);

            // Only the hash of an identity is kept from now on; sha256() comes from SqliteFunctions
            migrationBuilder.Sql("UPDATE \"UserEntities\" SET \"IdentityHash\" = sha256(\"UserIdentity\");");

            migrationBuilder.DropColumn(
                name: "UserIdentity",
                table: "UserEntities");

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    PreviousTokenHash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: true),
                    DeviceName = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    DevicePlatform = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastAddress = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserSessions_UserEntities_UserId",
                        column: x => x.UserId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserEntities_CoordinatorInstanceId_IdentityHash",
                table: "UserEntities",
                columns: new[] { "CoordinatorInstanceId", "IdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_CoordinatorInstanceId_UserId",
                table: "UserSessions",
                columns: new[] { "CoordinatorInstanceId", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_PreviousTokenHash",
                table: "UserSessions",
                column: "PreviousTokenHash");

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_TokenHash",
                table: "UserSessions",
                column: "TokenHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserSessions_UserId",
                table: "UserSessions",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The identities cannot be got back from their hashes: users log in as new ones after a downgrade
            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropIndex(
                name: "IX_UserEntities_CoordinatorInstanceId_IdentityHash",
                table: "UserEntities");

            migrationBuilder.DropColumn(
                name: "IdentityHash",
                table: "UserEntities");

            migrationBuilder.AddColumn<string>(
                name: "UserIdentity",
                table: "UserEntities",
                type: "TEXT",
                maxLength: 37,
                nullable: false,
                defaultValue: "");
        }
    }
}
