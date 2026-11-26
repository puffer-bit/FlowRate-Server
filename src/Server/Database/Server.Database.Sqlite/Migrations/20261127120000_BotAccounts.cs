using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.Sqlite.Migrations
{
    /// <summary>
    /// Bot accounts: the kind of every user (people stay 0) with the owner of a bot, and the limit of bots of every
    /// coordinator, 5 to start with. Written by hand, with no target model: the model snapshot is updated alongside.
    /// </summary>
    [DbContext(typeof(ServerDbContext))]
    [Migration("20261127120000_BotAccounts")]
    public partial class BotAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "Kind",
                table: "UserEntities",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "OwnerUserId",
                table: "UserEntities",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "MaxBots",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(name: "Kind", table: "UserEntities");
            migrationBuilder.DropColumn(name: "OwnerUserId", table: "UserEntities");
            migrationBuilder.DropColumn(name: "MaxBots", table: "CoordinatorInstances");
        }
    }
}
