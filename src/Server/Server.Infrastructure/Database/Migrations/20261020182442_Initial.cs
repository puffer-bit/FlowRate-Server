using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Server.Infrastructure.Database.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ChatFileContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatFileContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatVideoContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Thumbnail = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    Duration = table.Column<TimeSpan>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChatVideoContents", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "CoordinatorInstances",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    IsDefault = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ip = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    CreateTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    FirstLaunchTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentLaunchTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MaxOnlineUsers = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxUsers = table.Column<int>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    IsMOTDEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    MotdText = table.Column<string>(type: "TEXT", nullable: true),
                    AllowedIPs = table.Column<string>(type: "TEXT", nullable: false),
                    BannedIPs = table.Column<string>(type: "TEXT", nullable: false),
                    HeartbeatIntervalSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    HeartbeatTimeoutSeconds = table.Column<int>(type: "INTEGER", nullable: false),
                    MediaMaxFileSizeBytes = table.Column<long>(type: "INTEGER", nullable: false),
                    MediaStorageQuotaBytes = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CoordinatorInstances", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "MediaUploads",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    OwnerUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChatId = table.Column<int>(type: "INTEGER", nullable: true),
                    Kind = table.Column<int>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MediaUploads", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OrchestratorInstance",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ServerVersion = table.Column<string>(type: "TEXT", nullable: false),
                    LicenseKey = table.Column<string>(type: "TEXT", nullable: true),
                    Ip = table.Column<string>(type: "TEXT", nullable: false),
                    DnsName = table.Column<string>(type: "TEXT", nullable: true),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxCoordinators = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxOnlineUsers = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxUsers = table.Column<int>(type: "INTEGER", nullable: false),
                    TimeZoneId = table.Column<string>(type: "TEXT", nullable: true),
                    FirstLaunchTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CurrentLaunchTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    OperatingSystem = table.Column<string>(type: "TEXT", nullable: true),
                    CpuName = table.Column<string>(type: "TEXT", nullable: true),
                    DbProvider = table.Column<string>(type: "TEXT", nullable: true),
                    GpuName = table.Column<string>(type: "TEXT", nullable: true),
                    RamSizeMb = table.Column<int>(type: "INTEGER", nullable: false),
                    MaxDbSizeMb = table.Column<int>(type: "INTEGER", nullable: false),
                    RegionName = table.Column<string>(type: "TEXT", nullable: true),
                    RegionCode = table.Column<string>(type: "TEXT", nullable: true),
                    CityName = table.Column<string>(type: "TEXT", nullable: true),
                    Locale = table.Column<string>(type: "TEXT", nullable: true),
                    IsFirstLaunch = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrchestratorInstance", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PushDevices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    Token = table.Column<string>(type: "TEXT", maxLength: 512, nullable: false),
                    Platform = table.Column<int>(type: "INTEGER", nullable: false),
                    KeyId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Key = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: false),
                    ReplyTokenHash = table.Column<byte[]>(type: "BLOB", maxLength: 32, nullable: true),
                    UpdatedTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PushDevices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Roles",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Color = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false),
                    Permissions = table.Column<long>(type: "INTEGER", nullable: false),
                    IsEveryone = table.Column<bool>(type: "INTEGER", nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roles", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionCategories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionCategories", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SessionPlacements",
                columns: table => new
                {
                    SessionId = table.Column<int>(type: "INTEGER", nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    CategoryId = table.Column<int>(type: "INTEGER", nullable: true),
                    Position = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionPlacements", x => x.SessionId);
                });

            migrationBuilder.CreateTable(
                name: "Sessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: false),
                    Capacity = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Sessions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SfuNodes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Host = table.Column<string>(type: "TEXT", nullable: false),
                    Port = table.Column<int>(type: "INTEGER", nullable: false),
                    Username = table.Column<string>(type: "TEXT", nullable: false),
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    IsSecured = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SfuNodes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "UserEntities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserIdentity = table.Column<string>(type: "TEXT", maxLength: 37, nullable: false),
                    Username = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    Ip = table.Column<string>(type: "TEXT", maxLength: 45, nullable: true),
                    CoordinatorInstanceId = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    RegionName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    RegionCode = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Prefix = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    AvatarUrl = table.Column<string>(type: "TEXT", maxLength: 2048, nullable: true),
                    State = table.Column<int>(type: "INTEGER", nullable: false),
                    LastStatus = table.Column<int>(type: "INTEGER", nullable: false),
                    FirstConnectionTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    LastConnectionTime = table.Column<DateTime>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_UserEntities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ChatSessions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    CreatorUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    AdminUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastRevision = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    IconPath = table.Column<string>(type: "TEXT", nullable: true),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsArchived = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsAttachmentsAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVoiceMessagesAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsVideoMessagesAllowed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDelayedMessagesAllowed = table.Column<bool>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EngineType = table.Column<int>(type: "INTEGER", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "TEXT", nullable: true),
                    IsHostMustApprove = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    ConnectedUsersCount = table.Column<int>(type: "INTEGER", nullable: false),
                    HostId = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false),
                    HostPeerId = table.Column<string>(type: "TEXT", maxLength: 36, nullable: true)
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
                name: "CoordinatorSfuNodes",
                columns: table => new
                {
                    CoordinatorsId = table.Column<string>(type: "TEXT", nullable: false),
                    SfuNodesId = table.Column<int>(type: "INTEGER", nullable: false)
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
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    CredentialId = table.Column<byte[]>(type: "BLOB", nullable: false),
                    PublicKey = table.Column<byte[]>(type: "BLOB", nullable: false),
                    SignatureCounter = table.Column<uint>(type: "INTEGER", nullable: false),
                    Label = table.Column<string>(type: "TEXT", nullable: false),
                    AaGuid = table.Column<Guid>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsEnabled = table.Column<bool>(type: "INTEGER", nullable: false)
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
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    RoleId = table.Column<int>(type: "INTEGER", nullable: false)
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
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    PasswordHash = table.Column<string>(type: "TEXT", nullable: true),
                    TwoFactorKey = table.Column<string>(type: "TEXT", nullable: true),
                    UserPasskeyIds = table.Column<string>(type: "TEXT", nullable: false),
                    IsTwoFactorEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsPasswordEnabled = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsFIDO2Enabled = table.Column<bool>(type: "INTEGER", nullable: false)
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
                name: "ChatMessages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ChatId = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false),
                    Revision = table.Column<long>(type: "INTEGER", nullable: false),
                    SenderUserId = table.Column<int>(type: "INTEGER", nullable: false),
                    SentTime = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DelayedSentTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    DisposeTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    EditTime = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TextContent_Text = table.Column<string>(type: "TEXT", nullable: true),
                    ReplyToMessageId = table.Column<int>(type: "INTEGER", nullable: true),
                    UsersIds = table.Column<string>(type: "TEXT", nullable: false),
                    IsDelayed = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsEdited = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReposted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDelivered = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsReadOnly = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsSystem = table.Column<bool>(type: "INTEGER", nullable: false),
                    IsDisposable = table.Column<bool>(type: "INTEGER", nullable: false)
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
                });

            migrationBuilder.CreateTable(
                name: "ChatReadStates",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", nullable: false),
                    ChatId = table.Column<int>(type: "INTEGER", nullable: false),
                    LastReadMessageId = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "ChatImageContents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    MessageId = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false),
                    FileName = table.Column<string>(type: "TEXT", nullable: false),
                    Thumbnail = table.Column<string>(type: "TEXT", nullable: true),
                    Description = table.Column<string>(type: "TEXT", nullable: true),
                    Size = table.Column<long>(type: "INTEGER", nullable: false),
                    MimeType = table.Column<string>(type: "TEXT", nullable: false),
                    Width = table.Column<int>(type: "INTEGER", nullable: false),
                    Height = table.Column<int>(type: "INTEGER", nullable: false),
                    UploadId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false),
                    PreviewUploadId = table.Column<string>(type: "TEXT", maxLength: 32, nullable: false)
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

            migrationBuilder.CreateTable(
                name: "ChatMessageReactions",
                columns: table => new
                {
                    UserId = table.Column<int>(type: "INTEGER", maxLength: 36, nullable: false),
                    Emoji = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    MessageId = table.Column<int>(type: "INTEGER", nullable: false)
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
                    MessagesId = table.Column<int>(type: "INTEGER", nullable: false),
                    VideosId = table.Column<int>(type: "INTEGER", nullable: false)
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
                name: "IX_ChatImageContents_MessageId",
                table: "ChatImageContents",
                column: "MessageId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageFiles_MessagesId",
                table: "ChatMessageFiles",
                column: "MessagesId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessages_ChatId_Revision",
                table: "ChatMessages",
                columns: new[] { "ChatId", "Revision" });

            migrationBuilder.CreateIndex(
                name: "IX_ChatMessageVideos_VideosId",
                table: "ChatMessageVideos",
                column: "VideosId");

            migrationBuilder.CreateIndex(
                name: "IX_ChatReadStates_ChatId",
                table: "ChatReadStates",
                column: "ChatId");

            migrationBuilder.CreateIndex(
                name: "IX_CoordinatorSfuNodes_SfuNodesId",
                table: "CoordinatorSfuNodes",
                column: "SfuNodesId");

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
                name: "IX_SessionPlacements_CoordinatorInstanceId",
                table: "SessionPlacements",
                column: "CoordinatorInstanceId");

            migrationBuilder.CreateIndex(
                name: "IX_SfuNodes_Name",
                table: "SfuNodes",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_UserPasskeys_UserId",
                table: "UserPasskeys",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_UserRoles_RoleId",
                table: "UserRoles",
                column: "RoleId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChatImageContents");

            migrationBuilder.DropTable(
                name: "ChatMessageFiles");

            migrationBuilder.DropTable(
                name: "ChatMessageReactions");

            migrationBuilder.DropTable(
                name: "ChatMessageVideos");

            migrationBuilder.DropTable(
                name: "ChatReadStates");

            migrationBuilder.DropTable(
                name: "CoordinatorSfuNodes");

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
                name: "SessionPlacements");

            migrationBuilder.DropTable(
                name: "UserPasskeys");

            migrationBuilder.DropTable(
                name: "UserRoles");

            migrationBuilder.DropTable(
                name: "UserSecurityData");

            migrationBuilder.DropTable(
                name: "ChatFileContents");

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
                name: "Sessions");
        }
    }
}
