using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Database.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class VideoPreviews : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsVideo",
                table: "ChatFileContents",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "PreviewUploadId",
                table: "ChatFileContents",
                type: "character varying(32)",
                maxLength: 32,
                nullable: true);

            migrationBuilder.AddColumn<TimeSpan>(
                name: "VideoDuration",
                table: "ChatFileContents",
                type: "interval",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "VideoHeight",
                table: "ChatFileContents",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "VideoWidth",
                table: "ChatFileContents",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsVideo",
                table: "ChatFileContents");

            migrationBuilder.DropColumn(
                name: "PreviewUploadId",
                table: "ChatFileContents");

            migrationBuilder.DropColumn(
                name: "VideoDuration",
                table: "ChatFileContents");

            migrationBuilder.DropColumn(
                name: "VideoHeight",
                table: "ChatFileContents");

            migrationBuilder.DropColumn(
                name: "VideoWidth",
                table: "ChatFileContents");
        }
    }
}
