using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SessionPermissionOverrides : Migration
    {
        // ViewSessions, SendMessages, AttachFiles, JoinSessions, Speak and ShareVideo as of this migration
        private const long SessionPermissions = 129024;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SessionPermissionOverrides",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false),
                    Allow = table.Column<long>(type: "INTEGER", nullable: false),
                    Deny = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPermissionOverrides", x => new { x.SessionId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_SessionPermissionOverrides_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_SessionPermissionOverrides_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SessionPermissionOverrides_RoleId",
                table: "SessionPermissionOverrides",
                column: "RoleId");

            // The session permissions did not exist before, so everyone had them: the everyone role keeps them
            migrationBuilder.Sql($"UPDATE Roles SET Permissions = Permissions | {SessionPermissions} WHERE IsEveryone = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"UPDATE Roles SET Permissions = Permissions & ~{SessionPermissions};");

            migrationBuilder.DropTable(
                name: "SessionPermissionOverrides");
        }
    }
}
