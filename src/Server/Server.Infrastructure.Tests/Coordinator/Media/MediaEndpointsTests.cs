using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Coordinator.Media;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Application.Options;
using Server.Core.Models;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.TextChat;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.TextChat.Messages.Content;
using Server.Core.Models.User;
using Server.Infrastructure.Coordinator.Connection.Gateway;
using Server.Infrastructure.Coordinator.Media;
using Server.Infrastructure.Coordinator.Services;
using Server.Infrastructure.Database;
using Server.Infrastructure.Database.Entities.TextChat;
using Server.Infrastructure.Database.Repositories;
using Server.Infrastructure.Options;
using Server.Infrastructure.Security;
using Server.Infrastructure.Tests.Coordinator.Connection.Gateway;
using Shared.Models.Media;
using Shared.Serialization;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.Media;

public sealed class MediaEndpointsTests : IDisposable
{
    private const int UserId = 5;
    private const string Coordinator = "coordinator";

    private static readonly byte[] Png =
        [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, .. Enumerable.Range(0, 5000).Select(i => (byte)i)];
    private static readonly byte[] Jpeg = [0xFF, 0xD8, 0xFF, 0xE0, .. Enumerable.Range(0, 300).Select(i => (byte)i)];

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"media-{Guid.NewGuid():N}");
    private readonly string _certificatePath = Path.GetTempFileName();
    private readonly string _keyPath = Path.GetTempFileName();
    private readonly GatewayService _gateway;
    private readonly MediaService _media;
    private readonly MessagesRepository _messages;
    private readonly MediaUploadRepository _uploads;
    private readonly HttpClient _http;
    private readonly int _chatId;
    private bool _attachmentsAllowed = true;
    // Users missing here are normal users; null marks a deleted one
    private readonly Dictionary<int, UserModel?> _users = new();

    public MediaEndpointsTests()
    {
        Directory.CreateDirectory(_root);
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(_certificatePath, certificate.ExportCertificatePem());
            File.WriteAllText(_keyPath, rsa.ExportPkcs8PrivateKeyPem());
        }

        var factory = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
            .UseSqlite($"Data Source={Path.Combine(_root, "server.db")}")
            .Options);
        using (var context = factory.CreateDbContext())
        {
            context.Database.Migrate();
            var chat = new ChatSessionEntity { Id = 0, Name = "general", CoordinatorInstanceId = Coordinator };
            context.ChatSessionEntities.Add(chat);
            context.SaveChanges();
            _chatId = chat.Id;
        }

        _messages = new MessagesRepository(factory);
        _uploads = new MediaUploadRepository(factory);

        var chats = new Mock<IChatSessionManager>();
        chats.Setup(c => c.TryGetSession(It.IsAny<int>(), out It.Ref<ChatSessionModel?>.IsAny))
            .Returns((int id, out ChatSessionModel? chat) =>
            {
                chat = id == _chatId
                    ? new ChatSessionModel(id, Coordinator, "general", SessionModel.NoCapacityLimit, UserId, UserId,
                        new ChatSessionOptions(IsAttachmentsAllowed: _attachmentsAllowed))
                    : null;
                return chat != null;
            });

        var users = new Mock<IUserManager>();
        users.Setup(u => u.GetUserAsync(It.IsAny<int>()))
            .ReturnsAsync((int id) => _users.TryGetValue(id, out var user)
                ? user
                : UserModel.Create(Coordinator, $"user{id}", null, DateTime.UtcNow));

        var storage = new TusDiskMediaStorage(Path.Combine(_root, "files"));
        _media = new MediaService(NullLogger.Instance, _uploads, storage, chats.Object,
            new CoordinatorUploadSettings(4, 24, 10), new CoordinatorMediaSettings(100_000, 250_000),
            Coordinator, users: users.Object);

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();

        var json = ProtocolJsonContext.Default.Options;
        _gateway = new GatewayService(NullLogger.Instance, json, new MessageParser(NullLogger.Instance, json),
            new ServerCertificate(
                Microsoft.Extensions.Options.Options.Create(new CertificateOptions { Path = _certificatePath, KeyPath = _keyPath }),
                Microsoft.Extensions.Options.Options.Create(new StorageOptions()),
                NullLogger<ServerCertificate>.Instance),
            Coordinator, "127.0.0.1", port, new MediaHttpEndpoints(NullLogger.Instance, _media, storage));
        _gateway.StartAsync(new FakeRouter()).GetAwaiter().GetResult();

        _http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}")
        };
        Authorize(UserId);
    }

    public void Dispose()
    {
        _http.Dispose();
        _gateway.Dispose();
        _media.Dispose();
        SqliteConnection.ClearAllPools();
        Directory.Delete(_root, true);
        File.Delete(_certificatePath);
        File.Delete(_keyPath);
    }

    private void Authorize(int userId) =>
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _media.IssueToken(userId).Token);

    private static string Encode(string value) => Convert.ToBase64String(Encoding.UTF8.GetBytes(value));

    private async Task<HttpResponseMessage> CreateAsync(long length, string kind = MediaUploadMetadata.KindImage,
        string mimeType = "image/png", int? chatId = null, bool withChat = true)
    {
        var request = new HttpRequestMessage(HttpMethod.Post, MediaEndpoints.Uploads);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Length", length.ToString());
        request.Headers.Add("Upload-Metadata",
            (withChat ? $"{MediaUploadMetadata.ChatId} {Encode((chatId ?? _chatId).ToString())}," : string.Empty) +
            $"{MediaUploadMetadata.MimeType} {Encode(mimeType)},{MediaUploadMetadata.Kind} {Encode(kind)}");
        return await _http.SendAsync(request);
    }

    private async Task<HttpResponseMessage> PatchAsync(Uri location, long offset, byte[] bytes)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, location) { Content = new ByteArrayContent(bytes) };
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/offset+octet-stream");
        request.Headers.Add("Tus-Resumable", "1.0.0");
        request.Headers.Add("Upload-Offset", offset.ToString());
        return await _http.SendAsync(request);
    }

    private async Task<long> OffsetAsync(Uri location)
    {
        var request = new HttpRequestMessage(HttpMethod.Head, location);
        request.Headers.Add("Tus-Resumable", "1.0.0");
        using var response = await _http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return long.Parse(response.Headers.GetValues("Upload-Offset").Single());
    }

    private async Task<string> UploadAsync(byte[] bytes, string kind, string mimeType)
    {
        using var created = await CreateAsync(bytes.Length, kind, mimeType);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;

        using var patched = await PatchAsync(location, 0, bytes);
        Assert.Equal(HttpStatusCode.NoContent, patched.StatusCode);
        return location.ToString().Split('/').Last();
    }

    private async Task<string> UploadAvatarAsync(byte[] bytes, string mimeType = "image/png")
    {
        using var created = await CreateAsync(bytes.Length, MediaUploadMetadata.KindAvatar, mimeType, withChat: false);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;

        using var patched = await PatchAsync(location, 0, bytes);
        Assert.Equal(HttpStatusCode.NoContent, patched.StatusCode);
        return location.ToString().Split('/').Last();
    }

    private async Task<int> SendImageMessageAsync(string uploadId, string previewId)
    {
        var message = ChatMessageModel.Create(_chatId, UserId, DateTime.UtcNow, null,
        [
            new ChatImageContentModel
            {
                Id = 0, FileName = "cat.png", Size = Png.Length, MimeType = "image/png", Width = 64, Height = 48,
                UploadId = uploadId, PreviewUploadId = previewId
            }
        ]);
        return (await _messages.AddAsync(message)).Images.Single().Id;
    }

    private async Task<int> SendFileMessageAsync(string uploadId, string fileName, long size)
    {
        var message = ChatMessageModel.Create(_chatId, UserId, DateTime.UtcNow, null, files:
        [
            new ChatFileContentModel
            {
                Id = 0, FileName = fileName, Size = size, MimeType = "text/html", UploadId = uploadId
            }
        ]);
        return (await _messages.AddAsync(message)).Files.Single().Id;
    }

    [Fact]
    public async Task File_OfAnyType_IsUploadedAttachedAndServedForDownload()
    {
        // Not an image, and not what it claims to be: a file is taken as it is
        byte[] bytes = [.. Encoding.UTF8.GetBytes("<script>alert(1)</script>"), .. Jpeg];
        var uploadId = await UploadAsync(bytes, MediaUploadMetadata.KindFile, "text/html");
        Assert.NotNull(await _uploads.GetAsync(uploadId));

        var fileId = await SendFileMessageAsync(uploadId, "отчёт 1.html", bytes.Length);

        var response = await _http.GetAsync($"{MediaEndpoints.Files}/{fileId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/octet-stream", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("отчёт 1.html", response.Content.Headers.ContentDisposition.FileNameStar);
        Assert.Equal(bytes, await response.Content.ReadAsByteArrayAsync());

        // An image path does not lead to a file, nor a file path to an image
        Assert.Equal(HttpStatusCode.NotFound,
            (await _http.GetAsync($"{MediaEndpoints.Images}/{fileId}?variant=original")).StatusCode);
    }

    [Fact]
    public async Task File_WithAMalformedType_IsRefused()
    {
        using var response = await CreateAsync(10, MediaUploadMetadata.KindFile, "text/html; charset=utf-8");
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
    }

    [Fact]
    public async Task File_OfADeletedMessage_IsGone()
    {
        var uploadId = await UploadAsync(Png, MediaUploadMetadata.KindFile, "application/zip");
        var fileId = await SendFileMessageAsync(uploadId, "a.zip", Png.Length);

        var messageId = (await _messages.GetBeforeAsync(_chatId, null, 1)).Single().Id;
        await _messages.DeleteAsync(messageId);

        Assert.Equal(HttpStatusCode.NotFound, (await _http.GetAsync($"{MediaEndpoints.Files}/{fileId}")).StatusCode);
    }

    [Fact]
    public async Task Image_IsUploadedInPartsAttachedAndServed()
    {
        using var created = await CreateAsync(Png.Length);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!;
        var uploadId = location.ToString().Split('/').Last();
        Assert.Equal(32, uploadId.Length);

        using (var first = await PatchAsync(location, 0, Png[..2000]))
            Assert.Equal(HttpStatusCode.NoContent, first.StatusCode);
        Assert.Equal(2000, await OffsetAsync(location));
        using (var second = await PatchAsync(location, 2000, Png[2000..]))
            Assert.Equal(HttpStatusCode.NoContent, second.StatusCode);

        var previewId = await UploadAsync(Jpeg, MediaUploadMetadata.KindImagePreview, "image/jpeg");
        var imageId = await SendImageMessageAsync(uploadId, previewId);

        var original = await _http.GetAsync($"{MediaEndpoints.Images}/{imageId}?variant=original");
        Assert.Equal(HttpStatusCode.OK, original.StatusCode);
        Assert.Equal("image/png", original.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await original.Content.ReadAsByteArrayAsync());

        var preview = await _http.GetAsync($"{MediaEndpoints.Images}/{imageId}?variant=preview");
        Assert.Equal("image/jpeg", preview.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Jpeg, await preview.Content.ReadAsByteArrayAsync());

        var ranged = new HttpRequestMessage(HttpMethod.Get, $"{MediaEndpoints.Images}/{imageId}?variant=original");
        ranged.Headers.Range = new RangeHeaderValue(100, 199);
        var part = await _http.SendAsync(ranged);
        Assert.Equal(HttpStatusCode.PartialContent, part.StatusCode);
        Assert.Equal(Png[100..200], await part.Content.ReadAsByteArrayAsync());
    }

    [Fact]
    public async Task Avatar_IsUploadedWithoutAChatAttachedAndServedToEveryUser()
    {
        var uploadId = await UploadAvatarAsync(Png);

        using (var early = await _http.GetAsync($"{MediaEndpoints.Avatars}/{uploadId}"))
            Assert.Equal(HttpStatusCode.NotFound, early.StatusCode);

        Assert.False(await _media.AttachAvatarAsync(uploadId, UserId + 1));
        Assert.True(await _media.AttachAvatarAsync(uploadId, UserId));
        Assert.False(await _media.AttachAvatarAsync(uploadId, UserId));

        Authorize(UserId + 1);
        using var served = await _http.GetAsync($"{MediaEndpoints.Avatars}/{uploadId}");
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("image/png", served.Content.Headers.ContentType!.MediaType);
        Assert.Equal(Png, await served.Content.ReadAsByteArrayAsync());

        await _media.RemoveAvatarAsync(uploadId);

        using var removed = await _http.GetAsync($"{MediaEndpoints.Avatars}/{uploadId}");
        Assert.Equal(HttpStatusCode.NotFound, removed.StatusCode);
        Assert.Null(await _uploads.GetAsync(uploadId));
    }

    [Fact]
    public async Task FrozenUser_ReadsButCannotUpload_AndBannedOrDeletedUserCannotDoEither()
    {
        var uploadId = await UploadAvatarAsync(Png);
        Assert.True(await _media.AttachAvatarAsync(uploadId, UserId));
        var avatar = $"{MediaEndpoints.Avatars}/{uploadId}";

        var frozen = UserModel.Create(Coordinator, "frozen", null, DateTime.UtcNow);
        frozen.Suspend();
        _users[UserId] = frozen;
        using (var read = await _http.GetAsync(avatar))
            Assert.Equal(HttpStatusCode.OK, read.StatusCode);
        using (var upload = await CreateAsync(Png.Length))
            Assert.Equal(HttpStatusCode.Forbidden, upload.StatusCode);

        var banned = UserModel.Create(Coordinator, "banned", null, DateTime.UtcNow);
        banned.Ban();
        _users[UserId] = banned;
        using (var read = await _http.GetAsync(avatar))
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);

        _users[UserId] = null;
        using (var read = await _http.GetAsync(avatar))
            Assert.Equal(HttpStatusCode.Forbidden, read.StatusCode);
    }

    [Fact]
    public async Task Avatar_IsRefused_WhenItBreaksTheRules()
    {
        using var withChat = await CreateAsync(Png.Length, MediaUploadMetadata.KindAvatar);
        Assert.Equal(HttpStatusCode.BadRequest, withChat.StatusCode);

        using var tooLarge = await CreateAsync(Shared.Validation.Limits.AvatarMaxSize + 1,
            MediaUploadMetadata.KindAvatar, withChat: false);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);

        using var notAnImage = await CreateAsync(Png.Length, MediaUploadMetadata.KindAvatar, "text/plain", withChat: false);
        Assert.Equal(HttpStatusCode.UnsupportedMediaType, notAnImage.StatusCode);

        using var imageWithoutChat = await CreateAsync(Png.Length, withChat: false);
        Assert.Equal(HttpStatusCode.BadRequest, imageWithoutChat.StatusCode);

        using var badId = await _http.GetAsync($"{MediaEndpoints.Avatars}/nothex");
        Assert.Equal(HttpStatusCode.BadRequest, badId.StatusCode);
    }

    [Fact]
    public async Task Image_OfAMessage_CannotBeSetAsAnAvatar()
    {
        var imageId = await UploadAsync(Png, MediaUploadMetadata.KindImage, "image/png");

        Assert.False(await _media.AttachAvatarAsync(imageId, UserId));
    }

    [Fact]
    public async Task Upload_CannotBeAttachedTwice()
    {
        var uploadId = await UploadAsync(Png, MediaUploadMetadata.KindImage, "image/png");
        var previewId = await UploadAsync(Jpeg, MediaUploadMetadata.KindImagePreview, "image/jpeg");
        await SendImageMessageAsync(uploadId, previewId);

        await Assert.ThrowsAsync<Server.Application.Abstractions.Persistence.MediaUploadUnavailableException>(
            () => SendImageMessageAsync(uploadId, previewId));
        Assert.Equal(1, await _messages.CountChangesAsync(_chatId, 0));
    }

    [Fact]
    public async Task Requests_WithoutAToken_AreRefused()
    {
        _http.DefaultRequestHeaders.Authorization = null;

        using var created = await CreateAsync(Png.Length);
        Assert.Equal(HttpStatusCode.Unauthorized, created.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized,
            (await _http.GetAsync($"{MediaEndpoints.Images}/1?variant=original")).StatusCode);
    }

    [Fact]
    public async Task Upload_OfAnotherUser_CannotBeContinued()
    {
        using var created = await CreateAsync(Png.Length);
        Authorize(UserId + 1);

        using var patched = await PatchAsync(created.Headers.Location!, 0, Png);
        Assert.Equal(HttpStatusCode.Forbidden, patched.StatusCode);
    }

    [Fact]
    public async Task Upload_IsRefused_WhenItBreaksTheRules()
    {
        using (var tooLarge = await CreateAsync(100_001))
            Assert.Equal(HttpStatusCode.RequestEntityTooLarge, tooLarge.StatusCode);
        using (var notAnImage = await CreateAsync(10, mimeType: "application/zip"))
            Assert.Equal(HttpStatusCode.UnsupportedMediaType, notAnImage.StatusCode);
        using (var unknownKind = await CreateAsync(10, kind: "video"))
            Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);
        using (var unknownChat = await CreateAsync(10, chatId: _chatId + 1))
            Assert.Equal(HttpStatusCode.NotFound, unknownChat.StatusCode);

        _attachmentsAllowed = false;
        using (var notAllowed = await CreateAsync(10))
            Assert.Equal(HttpStatusCode.Forbidden, notAllowed.StatusCode);
    }

    [Fact]
    public async Task Upload_PastTheQuotaOfTheCoordinator_IsRefused()
    {
        using (var first = await CreateAsync(100_000))
            Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using (var second = await CreateAsync(100_000))
            Assert.Equal(HttpStatusCode.Created, second.StatusCode);

        using var third = await CreateAsync(100_000);
        Assert.Equal(HttpStatusCode.InsufficientStorage, third.StatusCode);
    }

    [Fact]
    public async Task Upload_ThatIsNotWhatItClaims_IsRemoved()
    {
        var uploadId = await UploadAsync(Jpeg, MediaUploadMetadata.KindImage, "image/png");

        Assert.Null(await _uploads.GetAsync(uploadId));
        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "files")));
    }

    [Fact]
    public async Task Uploads_NeverAttached_AreLimitedAndCleanedUp()
    {
        for (var i = 0; i < 4; i++)
            await UploadAsync(Png, MediaUploadMetadata.KindImage, "image/png");

        using (var fifth = await CreateAsync(Png.Length))
            Assert.Equal(HttpStatusCode.TooManyRequests, fifth.StatusCode);

        await using (var context = new TestContextFactory(new DbContextOptionsBuilder<ServerDbContext>()
                         .UseSqlite($"Data Source={Path.Combine(_root, "server.db")}").Options).CreateDbContext())
        {
            await context.MediaUploads.ExecuteUpdateAsync(s => s.SetProperty(x => x.CreatedAt, DateTime.UtcNow.AddDays(-2)));
        }

        await _media.CleanUpAsync();

        Assert.Empty(Directory.EnumerateFiles(Path.Combine(_root, "files")));
        using var afterCleanUp = await CreateAsync(Png.Length);
        Assert.Equal(HttpStatusCode.Created, afterCleanUp.StatusCode);
    }

    [Fact]
    public async Task Image_OfADeletedMessage_IsGone()
    {
        var uploadId = await UploadAsync(Png, MediaUploadMetadata.KindImage, "image/png");
        var previewId = await UploadAsync(Jpeg, MediaUploadMetadata.KindImagePreview, "image/jpeg");
        var imageId = await SendImageMessageAsync(uploadId, previewId);

        var messageId = (await _messages.GetBeforeAsync(_chatId, null, 1)).Single().Id;
        await _messages.DeleteAsync(messageId);

        Assert.Equal(HttpStatusCode.NotFound,
            (await _http.GetAsync($"{MediaEndpoints.Images}/{imageId}?variant=original")).StatusCode);
    }

    private sealed class TestContextFactory(DbContextOptions<ServerDbContext> options) : IDbContextFactory<ServerDbContext>
    {
        public ServerDbContext CreateDbContext() => new(options);
    }
}
