using System.Security.Cryptography;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using Server.Application.Coordinator.Authentication;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Media;
using Server.Database.Sqlite;
using Server.Database;
using Server.Database.Entities;
using Server.Database.Entities.TextChat;
using Server.Database.PostgreSql.Import;
using Server.Infrastructure.Database.Repositories;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Enums.WebRTC;
using Xunit;

namespace Server.Infrastructure.Tests.Database;

public sealed class PostgreSqlImportFactAttribute : FactAttribute
{
    public PostgreSqlImportFactAttribute()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("FLOWRATE_TEST_DATABASE_PROVIDER"), "PostgreSql", StringComparison.OrdinalIgnoreCase))
            Skip = "Requires the PostgreSQL integration test job.";
    }
}

[Trait("Category", "DatabaseProvider")]
public sealed class SqlitePostgreSqlImportTests : IDisposable
{
    private readonly RepositoryTestDatabase _target = new();
    private readonly string _path = Path.Combine(Path.GetTempPath(), $"flowrate-import-{Guid.NewGuid():N}.db");
    private static readonly DateTimeOffset Now = new(2026, 11, 19, 12, 0, 0, TimeSpan.Zero);
    private static readonly Guid SessionId = Guid.Parse("01234567-89ab-cdef-0123-456789abcdef");
    private static readonly Guid LoginCodeId = Guid.Parse("fedcba98-7654-3210-fedc-ba9876543210");
    private readonly SqlitePostgreSqlImporter _importer = new();

    public SqlitePostgreSqlImportTests()
    {
        using var source = Source();
        source.Database.Migrate();
        Seed(source);
        // Deleted IDs must not be reused after the import either.
        source.Database.ExecuteSqlRaw("UPDATE sqlite_sequence SET seq = 900");
    }

    private ServerDbContext Source() => new(new DbContextOptionsBuilder<ServerDbContext>()
        .UseServerSqlite(new SqliteConnectionStringBuilder { DataSource = _path, Pooling = false }.ConnectionString).Options);

    public void Dispose()
    {
        _target.Dispose();
        File.Delete(_path);
        File.Delete(_path + "-wal");
        File.Delete(_path + "-shm");
    }

    [PostgreSqlImportFact]
    public async Task Import_PreservesEveryTableIdsSecretsHistoryAndCollections_AndAdvancesSequences()
    {
        var before = SHA256.HashData(await File.ReadAllBytesAsync(_path));
        await using var target = _target.CreateDbContext();
        var result = await _importer.ImportAsync(_path, target);
        Assert.False(result.DryRun);
        Assert.All(result.Tables, table => Assert.True(table.Rows > 0, table.Name));
        Assert.Equal(target.Model.GetRelationalModel().Tables.Count(), result.Tables.Count);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(_path)));
        Assert.Equal(target.Database.GetMigrations(), await target.Database.GetAppliedMigrationsAsync());
        Assert.Equal(Permission.All, (await target.Roles.SingleAsync()).Permissions);
        Assert.Equal(["192.0.2.1", "::1"], (await target.CoordinatorInstances.SingleAsync()).AllowedIPs);
        Assert.Equal(["passkey"], (await target.Set<UserSecurityDataEntity>().SingleAsync()).UserPasskeyIds);
        Assert.Equal(SecretHash.Of("login"), (await target.UserEntities.SingleAsync()).IdentityHash);
        Assert.Equal("password-hash", (await target.Set<UserSecurityDataEntity>().SingleAsync()).PasswordHash);
        Assert.Equal(uint.MaxValue, (await target.Set<UserPasskeyEntity>().SingleAsync()).SignatureCounter);
        Assert.Equal(SessionId, (await target.UserSessions.SingleAsync()).Id);
        Assert.Equal(LoginCodeId, (await target.UserLoginCodes.SingleAsync()).Id);
        Assert.Equal(2, await target.ChatMessagesEntities.IgnoreQueryFilters().CountAsync());
        var message = await target.ChatMessagesEntities.Include(x => x.Reactions).Include(x => x.Videos).SingleAsync();
        Assert.Equal(401, message.Id);
        Assert.Equal(17, message.Revision);
        Assert.Equal("привет 🐡", message.TextContent!.Text);
        Assert.Equal(Now, message.PinnedAt);
        Assert.Equal("👍", Assert.Single(message.Reactions).Emoji);
        Assert.Equal(TimeSpan.FromSeconds(2), Assert.Single(message.Videos).Duration);
        Assert.Equal(["101", "deleted-user"], message.UsersIds);
        Assert.Equal(401, (await target.ChatReadStates.SingleAsync()).LastReadMessageId);
        Assert.Equal("upload", (await target.MediaUploads.SingleAsync()).Id);
        Assert.Equal(801, (await target.LinkPreviews.SingleAsync()).Id);
        Assert.Equal(901, await new RoleRepository(_target).AddAsync(
            Server.Core.Models.Roles.RoleModel.Create("coordinator", "next", 0, 1, Permission.None)));
        var next = new ChatSessionEntity { Id = 0, Name = "next", CoordinatorInstanceId = "coordinator" };
        target.ChatSessionEntities.Add(next);
        await target.SaveChangesAsync();
        Assert.Equal(901, next.Id);
    }

    [PostgreSqlImportFact]
    public async Task DryRun_RollsBackRowsAndSequenceChanges_AndCanBeFollowedByRealImport()
    {
        await using var target = _target.CreateDbContext();
        await target.Database.MigrateAsync();
        await target.Database.ExecuteSqlRawAsync("ALTER SEQUENCE \"Roles_Id_seq\" RESTART WITH 1200");
        var result = await _importer.ImportAsync(_path, target, dryRun: true);
        Assert.True(result.DryRun);
        Assert.Empty(await target.UserEntities.ToListAsync());
        Assert.Empty(await target.Sessions.ToListAsync());
        Assert.Equal(1200L, await target.Database.SqlQueryRaw<long>(
            "SELECT nextval(pg_get_serial_sequence('\"Roles\"', 'Id')) AS \"Value\"").SingleAsync());
        await _importer.ImportAsync(_path, target);
        Assert.Single(await target.UserEntities.ToListAsync());
    }

    [PostgreSqlImportFact]
    public async Task NonemptyTarget_IsRejectedWithoutChangingEitherDatabase()
    {
        await using var target = _target.CreateDbContext();
        await target.Database.MigrateAsync();
        target.LinkPreviews.Add(new LinkPreviewEntity { CoordinatorInstanceId = "existing", Url = "https://existing.test", FetchedAt = Now });
        await target.SaveChangesAsync();
        var before = SHA256.HashData(await File.ReadAllBytesAsync(_path));
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync(_path, target));
        Assert.Contains("not empty", error.Message);
        Assert.Equal("https://existing.test", (await target.LinkPreviews.SingleAsync()).Url);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(_path)));
    }

    [PostgreSqlImportFact]
    public async Task VerificationFailure_RollsBackAllPreviouslyCopiedTables()
    {
        await using var target = _target.CreateDbContext();
        await target.Database.MigrateAsync();
        await target.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION corrupt_import() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN NEW."Username" := 'changed'; RETURN NEW; END $$;
            CREATE TRIGGER corrupt_import BEFORE INSERT ON "UserEntities" FOR EACH ROW EXECUTE FUNCTION corrupt_import();
            """);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync(_path, target));
        Assert.Contains("Verification failed", error.Message);
        Assert.Empty(await target.UserEntities.ToListAsync());
        Assert.Empty(await target.CoordinatorInstances.ToListAsync());
        Assert.Empty(await target.LinkPreviews.ToListAsync());
    }

    [PostgreSqlImportFact]
    public async Task CancellationDuringCopy_RollsBackAllRows()
    {
        await using var target = _target.CreateDbContext();
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => _importer.ImportAsync(_path, target,
            progress: table => { if (table.Rows > 0) cancellation.Cancel(); }, ct: cancellation.Token));
        Assert.Empty(await target.CoordinatorInstances.ToListAsync());
        Assert.Empty(await target.GlobalBans.ToListAsync());
    }

    [PostgreSqlImportFact]
    public async Task InvalidSource_IsRejectedBeforeCreatingTargetTables()
    {
        await using (var source = Source())
        {
            await source.Database.OpenConnectionAsync();
            await source.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys = OFF");
            await source.Database.ExecuteSqlRawAsync("DELETE FROM UserEntities");
        }
        await using var target = _target.CreateDbContext();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync(_path, target));
        Assert.Contains("foreign keys", error.Message);
        Assert.Empty(await target.Database.GetAppliedMigrationsAsync());
    }

    [PostgreSqlImportFact]
    public async Task UnknownSourceTable_IsRejectedRatherThanSilentlyOmitted()
    {
        await using (var source = Source())
            await source.Database.ExecuteSqlRawAsync("CREATE TABLE ExtraData (Id INTEGER PRIMARY KEY)");
        await using var target = _target.CreateDbContext();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync(_path, target));
        Assert.Contains("ExtraData", error.Message);
        Assert.Empty(await target.Database.GetAppliedMigrationsAsync());
    }

    [PostgreSqlImportFact]
    public async Task OlderSourceSchema_IsRejectedWithoutUpgradingItOrCreatingTargetTables()
    {
        await using (var source = Source())
            await source.Database.ExecuteSqlRawAsync("DELETE FROM \"__EFMigrationsHistory\" WHERE MigrationId = (SELECT MAX(MigrationId) FROM \"__EFMigrationsHistory\")");
        var before = SHA256.HashData(await File.ReadAllBytesAsync(_path));
        await using var target = _target.CreateDbContext();
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync(_path, target));
        Assert.Contains("server version", error.Message);
        Assert.Empty(await target.Database.GetAppliedMigrationsAsync());
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(_path)));
    }

    [PostgreSqlImportFact]
    public async Task PostgreSqlConstraintFailure_RollsBackTheWholeImportWithoutTruncatingValues()
    {
        await using (var source = Source())
            await source.Database.ExecuteSqlRawAsync("UPDATE UserEntities SET Username = {0}", new string('a', 33));
        await using var target = _target.CreateDbContext();
        await Assert.ThrowsAsync<PostgresException>(() => _importer.ImportAsync(_path, target));
        Assert.Empty(await target.UserEntities.ToListAsync());
        Assert.Empty(await target.CoordinatorInstances.ToListAsync());
        Assert.Empty(await target.GlobalBans.ToListAsync());
    }

    [PostgreSqlImportFact]
    public async Task Import_ReadsCommittedWalRowsWithoutCheckpointingOrChangingSourceFile()
    {
        await using var source = Source();
        await source.Database.OpenConnectionAsync();
        await source.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL");
        await source.Database.ExecuteSqlRawAsync("UPDATE UserEntities SET Username = 'wal-user'");
        Assert.True(new FileInfo(_path + "-wal").Length > 0);
        var before = SHA256.HashData(await File.ReadAllBytesAsync(_path));
        await using var target = _target.CreateDbContext();
        await _importer.ImportAsync(_path, target);
        Assert.Equal("wal-user", (await target.UserEntities.SingleAsync()).Username);
        Assert.Equal(before, SHA256.HashData(await File.ReadAllBytesAsync(_path)));
    }

    [PostgreSqlImportFact]
    public async Task FinalVerification_DetectsChangesToPreviouslyCopiedTables()
    {
        await using var target = _target.CreateDbContext();
        await target.Database.MigrateAsync();
        await target.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION corrupt_previous_table() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN UPDATE "UserEntities" SET "Username" = 'changed'; RETURN NEW; END $$;
            CREATE TRIGGER corrupt_previous_table AFTER INSERT ON "UserSessions" FOR EACH ROW EXECUTE FUNCTION corrupt_previous_table();
            """);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => _importer.ImportAsync(_path, target));
        Assert.Contains("Verification failed for table 'UserEntities'", error.Message);
        Assert.Empty(await target.UserEntities.ToListAsync());
        Assert.Empty(await target.UserSessions.ToListAsync());
    }

    private static void Seed(ServerDbContext source)
    {
        var node = new SfuNodeEntity { Id = 11, Name = "sfu", Host = "mediasoup", Port = 50051, Username = "name", Key = "secret" };
        source.SfuNodes.Add(node);
        source.CoordinatorInstances.Add(new CoordinatorInstanceEntity
        {
            Id = "coordinator", Name = "server", Ip = "127.0.0.1", Port = 26666, PasswordHash = "coordinator-password",
            CreateTime = Now, FirstLaunchTime = Now, CurrentLaunchTime = Now,
            AllowedIPs = ["192.0.2.1", "::1"], BannedIPs = ["192.0.2.2"], RateLimits = CoordinatorRateLimitSettings.Default,
            SfuNodes = [node]
        });
        source.OrchestratorInstance.Add(new OrchestratorInstanceEntity { ServerVersion = "test", Ip = "127.0.0.1", FirstLaunchTime = Now, CurrentLaunchTime = Now });
        source.UserEntities.Add(new UserEntity
        {
            Id = 101, Username = "alice", IdentityHash = SecretHash.Of("login"), CoordinatorInstanceId = "coordinator",
            FirstConnectionTime = Now, LastConnectionTime = Now,
            SecurityData = new UserSecurityDataEntity { UserId = 101, PasswordHash = "password-hash", TwoFactorKey = "2fa-secret", UserPasskeyIds = ["passkey"] }
        });
        source.Set<UserPasskeyEntity>().Add(new UserPasskeyEntity { Id = 601, UserId = 101, CredentialId = [0, 128, 255], PublicKey = [1, 2], SignatureCounter = uint.MaxValue, CreatedAt = Now, AaGuid = SessionId });
        source.UserSessions.Add(new UserSessionEntity
        {
            Id = SessionId, UserId = 101, CoordinatorInstanceId = "coordinator", TokenHash = SecretHash.Of("token"),
            DeviceName = "desktop", DevicePlatform = "Linux", AppVersion = "1", CreatedAt = Now, LastUsedAt = Now, ExpiresAt = Now.AddDays(1)
        });
        source.UserLoginCodes.Add(new UserLoginCodeEntity
        {
            Id = LoginCodeId, CoordinatorInstanceId = "coordinator", UserId = 101, CodeHash = SecretHash.Of("code"),
            CreatedAt = Now, ExpiresAt = Now.AddHours(1)
        });
        source.Roles.Add(new RoleEntity { Id = 51, CoordinatorInstanceId = "coordinator", Name = "admin", Permissions = Permission.All });
        source.UserRoles.Add(new UserRoleEntity { UserId = 101, RoleId = 51 });
        source.ChatSessionEntities.Add(new ChatSessionEntity
        {
            Id = 31, Name = "chat", CoordinatorInstanceId = "coordinator", Kind = ChatKind.Group, LastRevision = 18,
            Members = [new ChatSessionMemberEntity { UserId = 101, AccountId = 101, JoinedAt = Now }]
        });
        source.VideoSessionsEntities.Add(new HybridSessionEntity { Id = 32, Name = "voice", CoordinatorInstanceId = "coordinator", EngineType = WebRTCEngine.Mediasoup, CreatedAt = Now });
        source.SessionCategories.Add(new SessionCategoryEntity { Id = 71, CoordinatorInstanceId = "coordinator", Name = "rooms" });
        source.SessionPlacements.Add(new SessionPlacementEntity { SessionId = 31, CategoryId = 71, CoordinatorInstanceId = "coordinator" });
        source.SessionPermissionOverrides.Add(new SessionPermissionOverrideEntity { SessionId = 31, RoleId = 51, Allow = Permission.All });
        source.LinkPreviews.Add(new LinkPreviewEntity { Id = 801, CoordinatorInstanceId = "coordinator", Url = "https://example.com", FetchedAt = Now });
        var message = new ChatMessageEntity
        {
            Id = 401, ChatId = 31, SenderUserId = 101, Revision = 17, SentTime = Now.AddTicks(7), PinnedAt = Now.AddTicks(7),
            TextContent = new ChatTextContentEntity { Text = "привет 🐡" }, UsersIds = ["101", "deleted-user"], LinkPreviewId = 801,
            Reactions = [new ChatReactionEntity { UserId = 101, Emoji = "👍", ReactedAt = Now }],
            Images = [new ChatImageContentEntity { Id = 411, MessageId = 401, FileName = "image", Size = 100, MimeType = "image/png", Width = 1, Height = 1, UploadId = "image", PreviewUploadId = "preview" }],
            Files = [new ChatFileContentEntity { Id = 421, MessageId = 401, FileName = "file", Size = 200, MimeType = "text/plain", UploadId = "upload" }],
            Videos = [new ChatVideoContentEntity { Id = 431, MessageId = 401, FileName = "video", Size = 300, MimeType = "video/mp4", Width = 1, Height = 1, Duration = TimeSpan.FromSeconds(2).Add(TimeSpan.FromTicks(7)), Messages = [] }]
        };
        source.ChatMessagesEntities.Add(message);
        source.ChatMessagesEntities.Add(new ChatMessageEntity { Id = 402, ChatId = 31, SenderUserId = 101, Revision = 18, SentTime = Now, IsDeleted = true });
        source.ChatReadStates.Add(new ChatReadStateEntity { UserId = 101, ChatId = 31, LastReadMessageId = 401 });
        source.ChatReadMarks.Add(new ChatReadMarkEntity { UserId = 101, ChatId = 31, UpToMessageId = 401, ReadAt = Now });
        source.MediaUploads.Add(new MediaUploadEntity { Id = "upload", CoordinatorInstanceId = "coordinator", OwnerUserId = 101, ChatId = 31, Kind = MediaUploadKind.File, MimeType = "text/plain", Size = 200, State = MediaUploadState.Attached, CreatedAt = Now, MessageId = 401 });
        source.PushDevices.Add(new PushDeviceEntity { Id = 501, CoordinatorInstanceId = "coordinator", UserId = 101, Token = "push-token", KeyId = new string('a', 32), Key = new byte[32], UpdatedTime = Now });
        source.GlobalBans.Add(new BanEntity { Id = SessionId, CoordinatorId = "coordinator", Kind = BanKind.Ip, Address = "192.0.2.2", Reason = "abuse", CreatedBy = 101, CreatedAt = Now, PreviousState = UserState.Normal });
        source.SaveChanges();
    }
}
