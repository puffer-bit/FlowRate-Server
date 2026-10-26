using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class ChatFileUploads : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatMessageFiles");

            migrationBuilder.AddColumn<string>(
                name: "UploadId",
                table: "ChatFileContents",
                type: "TEXT",
                maxLength: 32,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateIndex(
                name: "IX_ChatFileContents_MessageId",
                table: "ChatFileContents",
                column: "MessageId");

            migrationBuilder.AddForeignKey(
                name: "FK_ChatFileContents_ChatMessages_MessageId",
                table: "ChatFileContents",
                column: "MessageId",
                principalTable: "ChatMessages",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_ChatFileContents_ChatMessages_MessageId",
                table: "ChatFileContents");

            migrationBuilder.DropIndex(
                name: "IX_ChatFileContents_MessageId",
                table: "ChatFileContents");

            migrationBuilder.DropColumn(
                name: "UploadId",
                table: "ChatFileContents");

            migrationBuilder.CreateTable(
                name: "ChatMessageFiles",
                columns: table => new
                {
                    FilesId = table.Column<int>(type: "INTEGER", nullable: false),
                    MessagesId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageFiles", x => new { x.FilesId, x.MessagesId });
                    table.ForeignKey(
                        name: "FK_ChatMessageFiles_ChatFileContents_FilesId",
                        column: x => x.FilesId,
                        principalTable: "ChatFileContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessageFiles_ChatMessages_MessagesId",
                        column: x => x.MessagesId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFiles_MessagesId",
                table: "ChatMessageFiles",
                column: "MessagesId");
        }
    }
}
