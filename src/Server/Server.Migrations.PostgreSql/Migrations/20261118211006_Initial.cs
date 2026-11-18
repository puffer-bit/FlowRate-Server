using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Server.Migrations.PostgreSql.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatVideoContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    Thumbnail = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "interval", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatVideoContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoordinatorInstances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "text", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    IsDefault = table.Column<bool>(type: "boolean", nullable: false),
                    Ip = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    FirstLaunchTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentLaunchTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MaxOnlineUsers = table.Column<int>(type: "integer", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    IsMOTDEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    MotdText = table.Column<string>(type: "text", nullable: true),
                    AllowedIPs = table.Column<List<string>>(type: "text[]", nullable: false),
                    BannedIPs = table.Column<List<string>>(type: "text[]", nullable: false),
                    HeartbeatIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    HeartbeatTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    MediaMaxFileSizeBytes = table.Column<long>(type: "bigint", nullable: false),
                    MediaStorageQuotaBytes = table.Column<long>(type: "bigint", nullable: false),
                    MediaImageMaxSide = table.Column<int>(type: "integer", nullable: false),
                    MediaImageQuality = table.Column<int>(type: "integer", nullable: false),
                    MediaAllowOriginalImages = table.Column<bool>(type: "boolean", nullable: false),
                    MaxUnattachedUploadsPerUser = table.Column<int>(type: "integer", nullable: false),
                    UnattachedUploadLifetimeHours = table.Column<int>(type: "integer", nullable: false),
                    MediaTokenLifetimeMinutes = table.Column<int>(type: "integer", nullable: false),
                    RateLimits = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoordinatorInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "GlobalBans",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoordinatorId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: true),
                    Address = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    Reason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                    CreatedBy = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PreviousState = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GlobalBans", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LinkPreviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                    SiteName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Title = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: true),
                    Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                    ImageMimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ImageWidth = table.Column<int>(type: "integer", nullable: false),
                    ImageHeight = table.Column<int>(type: "integer", nullable: false),
                    ImageSize = table.Column<long>(type: "bigint", nullable: false),
                    IsLargeImage = table.Column<bool>(type: "boolean", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LinkPreviews", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaUploads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    OwnerUserId = table.Column<int>(type: "integer", nullable: false),
                    ChatId = table.Column<int>(type: "integer", nullable: true),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    State = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    MessageId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrchestratorInstance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ServerVersion = table.Column<string>(type: "text", nullable: false),
                    LicenseKey = table.Column<string>(type: "text", nullable: true),
                    Ip = table.Column<string>(type: "text", nullable: false),
                    DnsName = table.Column<string>(type: "text", nullable: true),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    MaxCoordinators = table.Column<int>(type: "integer", nullable: false),
                    MaxOnlineUsers = table.Column<int>(type: "integer", nullable: false),
                    MaxUsers = table.Column<int>(type: "integer", nullable: false),
                    SfuConnectTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    SfuKeepAlivePingSeconds = table.Column<int>(type: "integer", nullable: false),
                    SfuKeepAliveTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    SfuCallTimeoutSeconds = table.Column<int>(type: "integer", nullable: false),
                    SfuPingIntervalSeconds = table.Column<int>(type: "integer", nullable: false),
                    TimeZoneId = table.Column<string>(type: "text", nullable: true),
                    FirstLaunchTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    CurrentLaunchTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    OperatingSystem = table.Column<string>(type: "text", nullable: true),
                    CpuName = table.Column<string>(type: "text", nullable: true),
                    DbProvider = table.Column<string>(type: "text", nullable: true),
                    GpuName = table.Column<string>(type: "text", nullable: true),
                    RamSizeMb = table.Column<int>(type: "integer", nullable: false),
                    MaxDbSizeMb = table.Column<int>(type: "integer", nullable: false),
                    RegionName = table.Column<string>(type: "text", nullable: true),
                    RegionCode = table.Column<string>(type: "text", nullable: true),
                    CityName = table.Column<string>(type: "text", nullable: true),
                    Locale = table.Column<string>(type: "text", nullable: true),
                    IsFirstLaunch = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestratorInstance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    Token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    Platform = table.Column<int>(type: "integer", nullable: false),
                    KeyId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Key = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    ReplyTokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    UpdatedTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Color = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false),
                    Permissions = table.Column<long>(type: "bigint", nullable: false),
                    IsEveryone = table.Column<bool>(type: "boolean", nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionPlacements",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    CategoryId = table.Column<int>(type: "integer", nullable: true),
                    Position = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPlacements", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                    Capacity = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SfuNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Host = table.Column<string>(type: "text", nullable: false),
                    Port = table.Column<int>(type: "integer", nullable: false),
                    Username = table.Column<string>(type: "text", nullable: false),
                    Key = table.Column<string>(type: "text", nullable: false),
                    IsSecured = table.Column<bool>(type: "boolean", nullable: false),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SfuNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    IdentityHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    Username = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    Ip = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    RegionName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    RegionCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    DisplayName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    Prefix = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: true),
                    AvatarUrl = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    State = table.Column<int>(type: "integer", nullable: false),
                    LastStatus = table.Column<int>(type: "integer", nullable: false),
                    FirstConnectionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastConnectionTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    Kind = table.Column<int>(type: "integer", nullable: false),
                    DirectKey = table.Column<string>(type: "text", nullable: true),
                    CreatorUserId = table.Column<int>(type: "integer", nullable: false),
                    AdminUserId = table.Column<int>(type: "integer", nullable: false),
                    LastRevision = table.Column<long>(type: "bigint", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    IconPath = table.Column<string>(type: "text", nullable: true),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false),
                    IsAttachmentsAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsVoiceMessagesAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsVideoMessagesAllowed = table.Column<bool>(type: "boolean", nullable: false),
                    IsDelayedMessagesAllowed = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatSessions_Sessions_Id",
                        column: x => x.Id,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HybridSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    EngineType = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsHostMustApprove = table.Column<bool>(type: "boolean", nullable: false),
                    CameraCodec = table.Column<int>(type: "integer", nullable: false),
                    ScreenCodec = table.Column<int>(type: "integer", nullable: false),
                    AudioCodec = table.Column<int>(type: "integer", nullable: false),
                    IsVideoNackEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsVideoRtxEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    KeyFrameRequestDelayMs = table.Column<int>(type: "integer", nullable: false),
                    IsAudioNackEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsOpusFecEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    OpusPacketLossPercent = table.Column<int>(type: "integer", nullable: false),
                    IsVoiceDtxEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    VoiceJitterBufferMs = table.Column<int>(type: "integer", nullable: false),
                    VideoJitterBufferMs = table.Column<int>(type: "integer", nullable: false),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    ConnectedUsersCount = table.Column<int>(type: "integer", nullable: false),
                    HostId = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    HostPeerId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HybridSessions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HybridSessions_Sessions_Id",
                        column: x => x.Id,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SessionPermissionOverrides",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false),
                    Allow = table.Column<long>(type: "bigint", nullable: false),
                    Deny = table.Column<long>(type: "bigint", nullable: false)
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

            migrationBuilder.CreateTable(
                name: "CoordinatorSfuNodes",
                columns: table => new
                {
                    CoordinatorsId = table.Column<string>(type: "text", nullable: false),
                    SfuNodesId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoordinatorSfuNodes", x => new { x.CoordinatorsId, x.SfuNodesId });
                    table.ForeignKey(
                        name: "FK_CoordinatorSfuNodes_CoordinatorInstances_CoordinatorsId",
                        column: x => x.CoordinatorsId,
                        principalTable: "CoordinatorInstances",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CoordinatorSfuNodes_SfuNodes_SfuNodesId",
                        column: x => x.SfuNodesId,
                        principalTable: "SfuNodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserPasskeys",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "bytea", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "bytea", nullable: false),
                    SignatureCounter = table.Column<long>(type: "bigint", nullable: false),
                    Label = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    AaGuid = table.Column<Guid>(type: "uuid", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    IsEnabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserPasskeys", x => x.Id);
                    table.ForeignKey(
                        name: "FK_UserPasskeys_UserEntities_UserId",
                        column: x => x.UserId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserRoles",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    RoleId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserRoles", x => new { x.UserId, x.RoleId });
                    table.ForeignKey(
                        name: "FK_UserRoles_Roles_RoleId",
                        column: x => x.RoleId,
                        principalTable: "Roles",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_UserRoles_UserEntities_UserId",
                        column: x => x.UserId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSecurityData",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    PasswordHash = table.Column<string>(type: "text", nullable: true),
                    TwoFactorKey = table.Column<string>(type: "text", nullable: true),
                    UserPasskeyIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsTwoFactorEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsPasswordEnabled = table.Column<bool>(type: "boolean", nullable: false),
                    IsFIDO2Enabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserSecurityData", x => x.UserId);
                    table.ForeignKey(
                        name: "FK_UserSecurityData_UserEntities_UserId",
                        column: x => x.UserId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "UserSessions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    TokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: false),
                    PreviousTokenHash = table.Column<byte[]>(type: "bytea", maxLength: 32, nullable: true),
                    DeviceName = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    DevicePlatform = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    AppVersion = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastUsedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    LastAddress = table.Column<string>(type: "character varying(45)", maxLength: 45, nullable: true)
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

            migrationBuilder.CreateTable(
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ChatId = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    Revision = table.Column<long>(type: "bigint", nullable: false),
                    SenderUserId = table.Column<int>(type: "integer", nullable: false),
                    SentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    DelayedSentTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    DisposeTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    EditTime = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PinnedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    PinnedByUserId = table.Column<int>(type: "integer", nullable: true),
                    TextContent_Text = table.Column<string>(type: "text", nullable: true),
                    TextContent_CommandKind = table.Column<int>(type: "integer", nullable: true),
                    TextContent_CommandMinimum = table.Column<int>(type: "integer", nullable: true),
                    TextContent_CommandMaximum = table.Column<int>(type: "integer", nullable: true),
                    TextContent_CommandValue = table.Column<int>(type: "integer", nullable: true),
                    ReplyToMessageId = table.Column<int>(type: "integer", nullable: true),
                    UsersIds = table.Column<List<string>>(type: "text[]", nullable: false),
                    IsDelayed = table.Column<bool>(type: "boolean", nullable: false),
                    IsEdited = table.Column<bool>(type: "boolean", nullable: false),
                    IsReposted = table.Column<bool>(type: "boolean", nullable: false),
                    IsDelivered = table.Column<bool>(type: "boolean", nullable: false),
                    IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "boolean", nullable: false),
                    IsSystem = table.Column<bool>(type: "boolean", nullable: false),
                    SystemEventKind = table.Column<int>(type: "integer", nullable: true),
                    SystemActorUserId = table.Column<int>(type: "integer", nullable: true),
                    SystemSubjectUserId = table.Column<int>(type: "integer", nullable: true),
                    SystemCallId = table.Column<Guid>(type: "uuid", nullable: true),
                    SystemCallEndReason = table.Column<int>(type: "integer", nullable: true),
                    SystemCallDurationSeconds = table.Column<int>(type: "integer", nullable: true),
                    IsDisposable = table.Column<bool>(type: "boolean", nullable: false),
                    LinkPreviewId = table.Column<int>(type: "integer", nullable: true),
                    IsLinkPreviewDisabled = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatMessages_ChatSessions_ChatId",
                        column: x => x.ChatId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessages_LinkPreviews_LinkPreviewId",
                        column: x => x.LinkPreviewId,
                        principalTable: "LinkPreviews",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChatReadMarks",
                columns: table => new
                {
                    ChatId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    UpToMessageId = table.Column<int>(type: "integer", nullable: false),
                    ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReadMarks", x => new { x.ChatId, x.UserId, x.UpToMessageId });
                    table.ForeignKey(
                        name: "FK_ChatReadMarks_ChatSessions_ChatId",
                        column: x => x.ChatId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatReadStates",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    ChatId = table.Column<int>(type: "integer", nullable: false),
                    LastReadMessageId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatReadStates", x => new { x.UserId, x.ChatId });
                    table.ForeignKey(
                        name: "FK_ChatReadStates_ChatSessions_ChatId",
                        column: x => x.ChatId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessionMembers",
                columns: table => new
                {
                    ChatSessionId = table.Column<int>(type: "integer", nullable: false),
                    UserId = table.Column<int>(type: "integer", nullable: false),
                    JoinedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "CURRENT_TIMESTAMP"),
                    AccountId = table.Column<int>(type: "integer", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatSessionMembers", x => new { x.ChatSessionId, x.UserId });
                    table.CheckConstraint("CK_ChatSessionMembers_AccountId", "\"AccountId\" IS NULL OR \"AccountId\" = \"UserId\"");
                    table.ForeignKey(
                        name: "FK_ChatSessionMembers_ChatSessions_ChatSessionId",
                        column: x => x.ChatSessionId,
                        principalTable: "ChatSessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatSessionMembers_UserEntities_AccountId",
                        column: x => x.AccountId,
                        principalTable: "UserEntities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ChatFileContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    UploadId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFileContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatFileContents_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatImageContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", maxLength: 36, nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    MessageId = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "text", nullable: false),
                    Thumbnail = table.Column<string>(type: "text", nullable: true),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Size = table.Column<long>(type: "bigint", nullable: false),
                    MimeType = table.Column<string>(type: "text", nullable: false),
                    Width = table.Column<int>(type: "integer", nullable: false),
                    Height = table.Column<int>(type: "integer", nullable: false),
                    UploadId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PreviewUploadId = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatImageContents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChatImageContents_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageReactions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "integer", maxLength: 36, nullable: false),
                    Emoji = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    MessageId = table.Column<int>(type: "integer", nullable: false),
                    ReactedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageReactions", x => new { x.MessageId, x.UserId, x.Emoji });
                    table.ForeignKey(
                        name: "FK_ChatMessageReactions_ChatMessages_MessageId",
                        column: x => x.MessageId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChatMessageVideos",
                columns: table => new
                {
                    MessagesId = table.Column<int>(type: "integer", nullable: false),
                    VideosId = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatMessageVideos", x => new { x.MessagesId, x.VideosId });
                    table.ForeignKey(
                        name: "FK_ChatMessageVideos_ChatMessages_MessagesId",
                        column: x => x.MessagesId,
                        principalTable: "ChatMessages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ChatMessageVideos_ChatVideoContents_VideosId",
                        column: x => x.VideosId,
                        principalTable: "ChatVideoContents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChatFileContents_MessageId",
                table: "ChatFileContents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatImageContents_MessageId",
                table: "ChatImageContents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatId_Id",
                table: "ChatMessages",
                columns: new[] { "ChatId", "Id" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatId_PinnedAt",
                table: "ChatMessages",
                columns: new[] { "ChatId", "PinnedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatId_Revision",
                table: "ChatMessages",
                columns: new[] { "ChatId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_LinkPreviewId",
                table: "ChatMessages",
                column: "LinkPreviewId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageVideos_VideosId",
                table: "ChatMessageVideos",
                column: "VideosId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadMarks_ChatId_UpToMessageId",
                table: "ChatReadMarks",
                columns: new[] { "ChatId", "UpToMessageId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadStates_ChatId",
                table: "ChatReadStates",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionMembers_AccountId",
                table: "ChatSessionMembers",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessionMembers_UserId_ChatSessionId",
                table: "ChatSessionMembers",
                columns: new[] { "UserId", "ChatSessionId" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatSessions_DirectKey",
                table: "ChatSessions",
                column: "DirectKey",
                unique: true,
                filter: "\"DirectKey\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatorSfuNodes_SfuNodesId",
                table: "CoordinatorSfuNodes",
                column: "SfuNodesId");

            migrationBuilder.CreateIndex(
                name: "IX_GlobalBans_CoordinatorId_Kind_Address",
                table: "GlobalBans",
                columns: new[] { "CoordinatorId", "Kind", "Address" });

            migrationBuilder.CreateIndex(
                name: "IX_GlobalBans_CoordinatorId_Kind_UserId",
                table: "GlobalBans",
                columns: new[] { "CoordinatorId", "Kind", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_CoordinatorInstanceId_FetchedAt",
                table: "LinkPreviews",
                columns: new[] { "CoordinatorInstanceId", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_LinkPreviews_CoordinatorInstanceId_Url_FetchedAt",
                table: "LinkPreviews",
                columns: new[] { "CoordinatorInstanceId", "Url", "FetchedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaUploads_ChatId",
                table: "MediaUploads",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_MediaUploads_CoordinatorInstanceId_State_CreatedAt",
                table: "MediaUploads",
                columns: new[] { "CoordinatorInstanceId", "State", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_MediaUploads_MessageId",
                table: "MediaUploads",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_CoordinatorInstanceId_ReplyTokenHash",
                table: "PushDevices",
                columns: new[] { "CoordinatorInstanceId", "ReplyTokenHash" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PushDevices_CoordinatorInstanceId_Token",
                table: "PushDevices",
                columns: new[] { "CoordinatorInstanceId", "Token" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roles_CoordinatorInstanceId",
                table: "Roles",
                column: "CoordinatorInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionCategories_CoordinatorInstanceId",
                table: "SessionCategories",
                column: "CoordinatorInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPermissionOverrides_RoleId",
                table: "SessionPermissionOverrides",
                column: "RoleId");

            migrationBuilder.CreateIndex(
                name: "IX_SessionPlacements_CoordinatorInstanceId",
                table: "SessionPlacements",
                column: "CoordinatorInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SfuNodes_Name",
                table: "SfuNodes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserEntities_CoordinatorInstanceId_IdentityHash",
                table: "UserEntities",
                columns: new[] { "CoordinatorInstanceId", "IdentityHash" });

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_CredentialId",
                table: "UserPasskeys",
                column: "CredentialId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_UserId",
                table: "UserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");

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
            migrationBuilder.DropTable(
                name: "ChatFileContents");

            migrationBuilder.DropTable(
                name: "ChatImageContents");

            migrationBuilder.DropTable(
                name: "ChatMessageReactions");

            migrationBuilder.DropTable(
                name: "ChatMessageVideos");

            migrationBuilder.DropTable(
                name: "ChatReadMarks");

            migrationBuilder.DropTable(
                name: "ChatReadStates");

            migrationBuilder.DropTable(
                name: "ChatSessionMembers");

            migrationBuilder.DropTable(
                name: "CoordinatorSfuNodes");

            migrationBuilder.DropTable(
                name: "GlobalBans");

            migrationBuilder.DropTable(
                name: "HybridSessions");

            migrationBuilder.DropTable(
                name: "MediaUploads");

            migrationBuilder.DropTable(
                name: "OrchestratorInstance");

            migrationBuilder.DropTable(
                name: "PushDevices");

            migrationBuilder.DropTable(
                name: "SessionCategories");

            migrationBuilder.DropTable(
                name: "SessionPermissionOverrides");

            migrationBuilder.DropTable(
                name: "SessionPlacements");

            migrationBuilder.DropTable(
                name: "UserPasskeys");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserSecurityData");

            migrationBuilder.DropTable(
                name: "UserSessions");

            migrationBuilder.DropTable(
                name: "ChatMessages");

            migrationBuilder.DropTable(
                name: "ChatVideoContents");

            migrationBuilder.DropTable(
                name: "CoordinatorInstances");

            migrationBuilder.DropTable(
                name: "SfuNodes");

            migrationBuilder.DropTable(
                name: "Roles");

            migrationBuilder.DropTable(
                name: "UserEntities");

            migrationBuilder.DropTable(
                name: "ChatSessions");

            migrationBuilder.DropTable(
                name: "LinkPreviews");

            migrationBuilder.DropTable(
                name: "Sessions");
        }
    }
}
