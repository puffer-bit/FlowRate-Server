using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class HybridSessionMediaSettings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Sessions saved before keep what they used: VP8 = 3, H264 = 1, Opus = 1

            migrationBuilder.AddColumn<int>(
                name: "AudioCodec",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "CameraCodec",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 3);

            migrationBuilder.AddColumn<bool>(
                name: "IsAudioNackEnabled",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsOpusFecEnabled",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVideoNackEnabled",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVideoRtxEnabled",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsVoiceDtxEnabled",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<int>(
                name: "KeyFrameRequestDelayMs",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1000);

            migrationBuilder.AddColumn<int>(
                name: "OpusPacketLossPercent",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<int>(
                name: "ScreenCodec",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "VideoJitterBufferMs",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 200);

            migrationBuilder.AddColumn<int>(
                name: "VoiceJitterBufferMs",
                table: "HybridSessions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 100);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "AudioCodec",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "CameraCodec",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "IsAudioNackEnabled",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "IsOpusFecEnabled",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "IsVideoNackEnabled",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "IsVideoRtxEnabled",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "IsVoiceDtxEnabled",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "KeyFrameRequestDelayMs",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "OpusPacketLossPercent",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "ScreenCodec",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "VideoJitterBufferMs",
                table: "HybridSessions");

            migrationBuilder.DropColumn(
                name: "VoiceJitterBufferMs",
                table: "HybridSessions");
        }
    }
}
