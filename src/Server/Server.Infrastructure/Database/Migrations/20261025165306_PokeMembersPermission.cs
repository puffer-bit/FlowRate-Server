using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class PokeMembersPermission : Migration
    {
        // Permission.PokeMembers, which the everyone role gains as part of Permission.Default
        private const long PokeMembers = 131072;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"UPDATE Roles SET Permissions = Permissions | {PokeMembers} WHERE IsEveryone = 1;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql($"UPDATE Roles SET Permissions = Permissions & ~{PokeMembers};");
        }
    }
}
