using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Media;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Events;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Tests.Fakes;
using Server.Core.Models.Coordinator.Settings;
using Server.Core.Models.Media;
using Server.Core.Models.TextChat.Messages;
using Server.Core.Models.User;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.DataTransferObjects.ChatSession.Messages.Content;
using Shared.Models.Requests.SessionActions.ChatSession.Models;
using Shared.Models.Requests.SessionActions.Generic.Models;
using Shared.Models.Requests.SessionActions.Generic.Models.SessionData;

namespace Server.Application.Tests.Coordinator;

public class ChatSessionManagerAttachmentTests
{
    private const int AuthorId = 1;
    private const string Coordinator = "coordinator";
    private static readonly string OriginalId = new('a', 32);
    private static readonly string PreviewId = new('b', 32);

    private readonly Mock<IChatSessionRepository> _chats = new();
    private readonly Mock<IMessagesRepository> _messages = new();
    private readonly Mock<IMediaUploadRepository> _uploads = new();
    private readonly Mock<IMediaStorage> _storage = new();
    private readonly RecordingEventBus _events = new();
    private readonly Dictionary<string, MediaUploadModel> _storedUploads = new();
    private readonly List<ChatMessageModel> _added = [];
    private readonly ChatSessionManager _manager;
    private CoordinatorMediaSettings _media = CoordinatorMediaSettings.Default;

    public ChatSessionManagerAttachmentTests()
    {
        _chats.Setup(r => r.AddAsync(It.IsAny<Core.Models.TextChat.ChatSessionModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(100);
        _uploads.Setup(r => r.GetAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((string id, CancellationToken _) => _storedUploads.GetValueOrDefault(id));
        _messages.Setup(r => r.AddAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ChatMessageModel message, CancellationToken _) =>
            {
                _added.Add(message);
                message.Id = 7;
                message.MarkChanged(1);
                return message;
            });

        _manager = new ChatSessionManager(NullLogger.Instance, _events, _chats.Object, _messages.Object,
            _uploads.Object, _storage.Object, Coordinator, () => _media);
    }

    private async Task<int> CreateChat(bool attachmentsAllowed = true)
    {
        var (_, chat) = await _manager.CreateAsync(
            new CreateSessionModel("general", 10, new ChatSessionCreateData(IsAttachmentsAllowed: attachmentsAllowed)),
            AuthorId);
        return chat!.Id;
    }

    private void StoreUploads(int chatId, int ownerId = AuthorId, MediaUploadState state = MediaUploadState.Completed)
    {
        _storedUploads[OriginalId] = new MediaUploadModel(OriginalId, Coordinator, ownerId, chatId,
            MediaUploadKind.Image, "image/png", 5000, state, DateTime.UtcNow);
        _storedUploads[PreviewId] = new MediaUploadModel(PreviewId, Coordinator, ownerId, chatId,
            MediaUploadKind.ImagePreview, "image/jpeg", 300, MediaUploadState.Completed, DateTime.UtcNow);
    }

    private static NewChatMessageModel MessageWithImage(int chatId) => new()
    {
        ChatId = chatId,
        Images =
        [
            new NewChatImageModel
            {
                UploadId = OriginalId, PreviewUploadId = PreviewId, FileName = "cat.png", Width = 640, Height = 480
            }
        ]
    };

    [Fact]
    public async Task ImageMessage_TakesSizeAndTypeFromTheUpload()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId);

        var result = await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId);

        Assert.Equal(null, result);
        var image = Assert.Single(Assert.Single(_added).Images);
        Assert.Equal(5000, image.Size);
        Assert.Equal("image/png", image.MimeType);
        Assert.Equal(OriginalId, image.UploadId);
        Assert.Equal(PreviewId, image.PreviewUploadId);
        Assert.Single(_events.Of<ChatSessionMessageAddedEvent>());
    }

    [Fact]
    public async Task ImageMessage_IsAnnouncedWithTheIdsItsImagesWereStoredUnder()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId);
        var stored = ChatMessageModel.Create(chatId, AuthorId, DateTime.UtcNow, null,
            [new Core.Models.TextChat.Messages.Content.ChatImageContentModel
            {
                Id = 55, FileName = "cat.png", Size = 5000, MimeType = "image/png", Width = 640, Height = 480,
                UploadId = OriginalId, PreviewUploadId = PreviewId
            }]);
        stored.Id = 7;
        _messages.Setup(r => r.AddAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(stored);

        await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId);

        var announced = Assert.Single(_events.Of<ChatSessionMessageAddedEvent>());
        Assert.Equal(55, Assert.Single(announced.Message.Images).Id);
    }

    [Fact]
    public async Task ImageMessage_InAChatWithoutAttachments_IsRefused()
    {
        var chatId = await CreateChat(attachmentsAllowed: false);
        StoreUploads(chatId);

        Assert.Equal(ErrorCode.AttachmentsNotAllowed,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task ImageMessage_WithAnUploadOfAnotherUser_IsRefused()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId, ownerId: 2);

        Assert.Equal(ErrorCode.AttachmentInvalid,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
    }

    [Fact]
    public async Task ImageMessage_WithAnUploadOfAnotherChat_IsRefused()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId + 1);

        Assert.Equal(ErrorCode.AttachmentInvalid,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
    }

    [Fact]
    public async Task ImageMessage_WithAnUnfinishedUpload_IsNotReady()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId, state: MediaUploadState.Uploading);

        Assert.Equal(ErrorCode.AttachmentNotReady,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
    }

    [Fact]
    public async Task ImageMessage_WithAnUploadAttachedAlready_IsRefused()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId, state: MediaUploadState.Attached);

        Assert.Equal(ErrorCode.AttachmentInvalid,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
    }

    [Fact]
    public async Task ImageMessage_WhoseUploadIsTakenMeanwhile_IsNotReady()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId);
        _messages.Setup(r => r.AddAsync(It.IsAny<ChatMessageModel>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new MediaUploadUnavailableException());

        Assert.Equal(ErrorCode.AttachmentNotReady,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
        Assert.Empty(_events.Of<ChatSessionMessageAddedEvent>());
    }

    private static readonly string FileId = new('c', 32);

    private void StoreFile(int chatId, MediaUploadKind kind = MediaUploadKind.File,
        MediaUploadState state = MediaUploadState.Completed) =>
        _storedUploads[FileId] = new MediaUploadModel(FileId, Coordinator, AuthorId, chatId, kind,
            "application/pdf", 12345, state, DateTime.UtcNow);

    private static NewChatMessageModel MessageWithFile(int chatId) => new()
    {
        ChatId = chatId,
        Files = [new NewChatFileModel { UploadId = FileId, FileName = "report.pdf" }]
    };

    [Fact]
    public async Task FileMessage_TakesSizeAndTypeFromTheUpload()
    {
        var chatId = await CreateChat();
        StoreFile(chatId);

        Assert.Equal(null, await _manager.AddMessageAsync(chatId, MessageWithFile(chatId), AuthorId));

        var file = Assert.Single(Assert.Single(_added).Files);
        Assert.Equal("report.pdf", file.FileName);
        Assert.Equal(12345, file.Size);
        Assert.Equal("application/pdf", file.MimeType);
        Assert.Equal(FileId, file.UploadId);
    }

    [Fact]
    public async Task FileMessage_WithAnImageUpload_IsRefused()
    {
        var chatId = await CreateChat();
        StoreFile(chatId, kind: MediaUploadKind.Image);

        Assert.Equal(ErrorCode.AttachmentInvalid,
            await _manager.AddMessageAsync(chatId, MessageWithFile(chatId), AuthorId));
    }

    [Fact]
    public async Task FileMessage_WithAnUnfinishedUpload_IsNotReady()
    {
        var chatId = await CreateChat();
        StoreFile(chatId, state: MediaUploadState.Uploading);

        Assert.Equal(ErrorCode.AttachmentNotReady,
            await _manager.AddMessageAsync(chatId, MessageWithFile(chatId), AuthorId));
    }

    [Fact]
    public async Task FileMessage_InAChatWithoutAttachments_IsRefused()
    {
        var chatId = await CreateChat(attachmentsAllowed: false);
        StoreFile(chatId);

        Assert.Equal(ErrorCode.AttachmentsNotAllowed,
            await _manager.AddMessageAsync(chatId, MessageWithFile(chatId), AuthorId));
    }

    [Fact]
    public async Task VideoMessage_IsRefused()
    {
        var chatId = await CreateChat();
        var message = new NewChatMessageModel
        {
            ChatId = chatId,
            Video = [new ChatVideoContentDTO { Id = 1, FileName = "a.mp4", Size = 1, MimeType = "video/mp4", Width = 1, Height = 1, Duration = TimeSpan.FromSeconds(1) }]
        };

        Assert.Equal(ErrorCode.AttachmentInvalid, await _manager.AddMessageAsync(chatId, message, AuthorId));
    }

    [Fact]
    public async Task DeletingAMessage_DeletesItsFiles()
    {
        var chatId = await CreateChat();
        var message = ChatMessageModel.Create(chatId, AuthorId, DateTime.UtcNow, null,
            [new Core.Models.TextChat.Messages.Content.ChatImageContentModel
            {
                Id = 1, FileName = "cat.png", Size = 1, MimeType = "image/png", Width = 1, Height = 1,
                UploadId = OriginalId, PreviewUploadId = PreviewId
            }]);
        message.Id = 7;
        _messages.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(message);
        _messages.Setup(r => r.DeleteAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(2L);
        _uploads.Setup(r => r.RemoveByMessageAsync(7, It.IsAny<CancellationToken>()))
            .ReturnsAsync([OriginalId, PreviewId]);

        Assert.Equal(null, await _manager.DeleteMessageAsync(chatId, 7, AuthorId));

        _storage.Verify(s => s.DeleteAsync(OriginalId, It.IsAny<CancellationToken>()), Times.Once);
        _storage.Verify(s => s.DeleteAsync(PreviewId, It.IsAny<CancellationToken>()), Times.Once);
    }

    private void StorePng(int width, int height)
    {
        var header = new byte[24];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 0, 0, 0, 13 }.CopyTo(header, 0);
        "IHDR"u8.CopyTo(header.AsSpan(12));
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(16), (uint)width);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(header.AsSpan(20), (uint)height);
        _storage.Setup(s => s.OpenReadAsync(OriginalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(header));
    }

    [Fact]
    public async Task LargeImage_IsTaken_WhileOriginalsAreAllowed()
    {
        var chatId = await CreateChat();
        StoreUploads(chatId);
        StorePng(8000, 6000);

        Assert.Null(await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
        Assert.Single(_added);
    }

    [Fact]
    public async Task LargeImage_IsRefused_WhenOnlyCompressedOnesAreTaken()
    {
        _media = new CoordinatorMediaSettings(1000, 0, imageMaxSide: 2560, allowOriginalImages: false);
        var chatId = await CreateChat();
        StoreUploads(chatId);
        StorePng(2561, 100);

        Assert.Equal(ErrorCode.ImageNotCompressed,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task ImageWithinTheSide_IsTaken_WhenOnlyCompressedOnesAreTaken()
    {
        _media = new CoordinatorMediaSettings(1000, 0, imageMaxSide: 2560, allowOriginalImages: false);
        var chatId = await CreateChat();
        StoreUploads(chatId);
        StorePng(1440, 2560);

        Assert.Null(await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
        Assert.Single(_added);
    }

    [Fact]
    public async Task ImageWithAnUnreadableHeader_IsRefused_WhenOnlyCompressedOnesAreTaken()
    {
        _media = new CoordinatorMediaSettings(1000, 0, allowOriginalImages: false);
        var chatId = await CreateChat();
        StoreUploads(chatId);
        _storage.Setup(s => s.OpenReadAsync(OriginalId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream([1, 2, 3]));

        Assert.Equal(ErrorCode.AttachmentInvalid,
            await _manager.AddMessageAsync(chatId, MessageWithImage(chatId), AuthorId));
    }

    private void StoreVideo(int chatId, byte[] content, string mimeType = "video/mp4")
    {
        _storedUploads[FileId] = new MediaUploadModel(FileId, Coordinator, AuthorId, chatId, MediaUploadKind.File,
            mimeType, content.Length, MediaUploadState.Completed, DateTime.UtcNow);
        _storage.Setup(s => s.OpenReadAsync(FileId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => new MemoryStream(content));
    }

    private static NewChatMessageModel MessageWithVideo(int chatId, string fileName = "clip.mp4") => new()
    {
        ChatId = chatId,
        Files = [new NewChatFileModel { UploadId = FileId, FileName = fileName }]
    };

    private static readonly CoordinatorMediaSettings OnlyCompressedVideos =
        new(1000, 0, videoMaxSide: 720, allowOriginalVideos: false);

    [Fact]
    public async Task LargeVideo_IsTaken_WhileOriginalsAreAllowed()
    {
        var chatId = await CreateChat();
        StoreVideo(chatId, Media.TestMp4.Build(3840, 2160, 10, mediaLength: 100_000));

        Assert.Null(await _manager.AddMessageAsync(chatId, MessageWithVideo(chatId), AuthorId));
    }

    [Theory]
    [InlineData(1280, 720)]
    [InlineData(720, 1280)]
    public async Task CompressedVideo_IsTaken_WhenOnlyCompressedOnesAreTaken(int width, int height)
    {
        _media = OnlyCompressedVideos;
        var chatId = await CreateChat();
        // 10 s at about 2.5 Mbit/s
        StoreVideo(chatId, Media.TestMp4.Build(width, height, 10, mediaLength: 3_100_000));

        Assert.Null(await _manager.AddMessageAsync(chatId, MessageWithVideo(chatId), AuthorId));
    }

    [Fact]
    public async Task LargerVideo_IsRefused_WhenOnlyCompressedOnesAreTaken()
    {
        _media = OnlyCompressedVideos;
        var chatId = await CreateChat();
        StoreVideo(chatId, Media.TestMp4.Build(1920, 1080, 10));

        Assert.Equal(ErrorCode.VideoNotCompressed,
            await _manager.AddMessageAsync(chatId, MessageWithVideo(chatId), AuthorId));
        Assert.Empty(_added);
    }

    [Fact]
    public async Task VideoOfAHigherBitrate_IsRefused_WhenOnlyCompressedOnesAreTaken()
    {
        _media = OnlyCompressedVideos;
        var chatId = await CreateChat();
        // 10 s at about 20 Mbit/s
        StoreVideo(chatId, Media.TestMp4.Build(1280, 720, 10, mediaLength: 25_000_000));

        Assert.Equal(ErrorCode.VideoNotCompressed,
            await _manager.AddMessageAsync(chatId, MessageWithVideo(chatId), AuthorId));
    }

    [Fact]
    public async Task VideoThatIsNotAnMp4_IsRefused_WhenOnlyCompressedOnesAreTaken()
    {
        _media = OnlyCompressedVideos;
        var chatId = await CreateChat();
        StoreVideo(chatId, [0x1A, 0x45, 0xDF, 0xA3, 0, 0, 0, 0], "application/octet-stream");

        Assert.Equal(ErrorCode.VideoNotCompressed,
            await _manager.AddMessageAsync(chatId, MessageWithVideo(chatId, "clip.webm"), AuthorId));
    }

    [Fact]
    public async Task OtherFile_IsNotChecked_WhenOnlyCompressedVideosAreTaken()
    {
        _media = OnlyCompressedVideos;
        var chatId = await CreateChat();
        StoreFile(chatId);

        Assert.Null(await _manager.AddMessageAsync(chatId, MessageWithFile(chatId), AuthorId));
    }
}
