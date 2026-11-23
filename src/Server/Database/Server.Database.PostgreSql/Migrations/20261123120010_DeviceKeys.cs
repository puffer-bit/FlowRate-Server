using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.PostgreSql.Migrations
{
    /// <summary>
    /// Devices log in with their own keys: the recovery key of the account, the public key of each device and
    /// one-time login codes. The identity and session tokens of the earlier protocol 3 stay until accounts move over.
    /// </summary>
    public partial class DeviceKeys : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<byte[]>(
                name: "IdentityHash",
                table: "UserEntities",
                type: "bytea",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldMaxLength: 32);

            migrationBuilder.AddColumn<byte[]>(
                name: "AccountPublicKey",
                table: "UserEntities",
                type: "bytea",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<byte[]>(
                name: "RecoveryKeyHash",
                table: "UserEntities",
                type: "bytea",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "TokenHash",
                table: "UserSessions",
                type: "bytea",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldMaxLength: 32);

            // The sessions there are come from the earlier protocol 3 (DeviceLoginMethod.Legacy)
            migrationBuilder.AddColumn<int>(
                name: "AddedBy",
                table: "UserSessions",
                type: "integer",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<bool>(
                name: "IsTemporary",
                table: "UserSessions",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<byte[]>(
                name: "PublicKey",
                table: "UserSessions",
                type: "bytea",
                maxLength: 128,
                nullable: true);

            migrationBuilder.CreateTable(
                name: "UserLoginCodes",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CodeHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserLoginCodes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserLoginCodes_UserEntities_UserId",
                        column: x => x.UserId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_UserEntities_CoordinatorInstanceId_RecoveryKeyHash",
                table: "UserEntities",
                columns: new[] { "CoordinatorInstanceId", "RecoveryKeyHash" });

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginCodes_CodeHash",
                table: "UserLoginCodes",
                column: "CodeHash",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserLoginCodes_UserId",
                table: "UserLoginCodes",
                column: "UserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "UserLoginCodes");

            migrationBuilder.DropIndex(
                name: "IX_UserEntities_CoordinatorInstanceId_RecoveryKeyHash",
                table: "UserEntities");

            // Devices with keys cannot go back to session tokens
            migrationBuilder.Sql("DELETE FROM \"UserSessions\" WHERE \"TokenHash\" IS NULL;");

            migrationBuilder.DropColumn(
                name: "AddedBy",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "IsTemporary",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "PublicKey",
                table: "UserSessions");

            migrationBuilder.DropColumn(
                name: "AccountPublicKey",
                table: "UserEntities");

            migrationBuilder.DropColumn(
                name: "RecoveryKeyHash",
                table: "UserEntities");

            migrationBuilder.AlterColumn<byte[]>(
                name: "TokenHash",
                table: "UserSessions",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldMaxLength: 32,
                oldNullable: true);

            migrationBuilder.AlterColumn<byte[]>(
                name: "IdentityHash",
                table: "UserEntities",
                type: "bytea",
                maxLength: 32,
                nullable: false,
                defaultValue: new byte[0],
                oldClrType: typeof(byte[]),
                oldType: "bytea",
                oldMaxLength: 32,
                oldNullable: true);
        }
    }
}
