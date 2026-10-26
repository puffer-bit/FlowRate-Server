using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class SettingsInDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "SfuCallTimeoutSeconds",
                table: "OrchestratorInstance",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "SfuConnectTimeoutSeconds",
                table: "OrchestratorInstance",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "SfuKeepAlivePingSeconds",
                table: "OrchestratorInstance",
                type: "INTEGER",
                nullable: false,
                defaultValue: 15);

            migrationBuilder.AddColumn<int>(
                name: "SfuKeepAliveTimeoutSeconds",
                table: "OrchestratorInstance",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "SfuPingIntervalSeconds",
                table: "OrchestratorInstance",
                type: "INTEGER",
                nullable: false,
                defaultValue: 5);

            migrationBuilder.AddColumn<int>(
                name: "MaxUnattachedUploadsPerUser",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 40);

            migrationBuilder.AddColumn<int>(
                name: "MediaTokenLifetimeMinutes",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 10);

            migrationBuilder.AddColumn<string>(
                name: "RateLimits",
                table: "CoordinatorInstances",
                type: "TEXT",
                nullable: false,
                // CoordinatorRateLimitSettings.Default as it was when the column was added
                defaultValue: "{\"Global\":{\"Count\":300,\"PeriodSeconds\":3},\"AuthenticationPerIp\":{\"Count\":5,\"PeriodSeconds\":60},\"SessionManagement\":{\"Count\":10,\"PeriodSeconds\":60},\"SessionLayout\":{\"Count\":20,\"PeriodSeconds\":10},\"SessionMedia\":{\"Count\":10,\"PeriodSeconds\":5},\"MicrophoneMute\":{\"Count\":10,\"PeriodSeconds\":5},\"InboundResume\":{\"Count\":30,\"PeriodSeconds\":5},\"Moderation\":{\"Count\":10,\"PeriodSeconds\":10},\"ChatWrite\":{\"Count\":10,\"PeriodSeconds\":10},\"ChatRead\":{\"Count\":20,\"PeriodSeconds\":4},\"PushRegistration\":{\"Count\":10,\"PeriodSeconds\":5},\"MediaToken\":{\"Count\":10,\"PeriodSeconds\":10},\"Query\":{\"Count\":200,\"PeriodSeconds\":4},\"Heartbeat\":{\"Count\":4,\"PeriodSeconds\":2},\"MaxRejectionsPerWindow\":20,\"RejectionWindowSeconds\":60,\"MaxConnectionsPerIp\":10}");

            migrationBuilder.AddColumn<int>(
                name: "UnattachedUploadLifetimeHours",
                table: "CoordinatorInstances",
                type: "INTEGER",
                nullable: false,
                defaultValue: 24);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "SfuCallTimeoutSeconds",
                table: "OrchestratorInstance");

            migrationBuilder.DropColumn(
                name: "SfuConnectTimeoutSeconds",
                table: "OrchestratorInstance");

            migrationBuilder.DropColumn(
                name: "SfuKeepAlivePingSeconds",
                table: "OrchestratorInstance");

            migrationBuilder.DropColumn(
                name: "SfuKeepAliveTimeoutSeconds",
                table: "OrchestratorInstance");

            migrationBuilder.DropColumn(
                name: "SfuPingIntervalSeconds",
                table: "OrchestratorInstance");

            migrationBuilder.DropColumn(
                name: "MaxUnattachedUploadsPerUser",
                table: "CoordinatorInstances");

            migrationBuilder.DropColumn(
                name: "MediaTokenLifetimeMinutes",
                table: "CoordinatorInstances");

            migrationBuilder.DropColumn(
                name: "RateLimits",
                table: "CoordinatorInstances");

            migrationBuilder.DropColumn(
                name: "UnattachedUploadLifetimeHours",
                table: "CoordinatorInstances");
        }
    }
}
