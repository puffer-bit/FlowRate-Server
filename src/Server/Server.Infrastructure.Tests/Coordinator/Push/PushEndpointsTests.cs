using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Server.Application.Abstractions.Persistence;
using Server.Application.Coordinator.Connections.RateLimiting;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Push;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Users;
using Server.Core.Models.Push;
using Server.Core.Models.TextChat;
using Server.Core.Models.User;
using Server.Infrastructure.Coordinator.Connection.Gateway;
using Server.Infrastructure.Coordinator.Push;
using Server.Infrastructure.Coordinator.Services;
using Server.Infrastructure.Options;
using Server.Infrastructure.Security;
using Server.Infrastructure.Tests.Coordinator.Connection.Gateway;
using Shared.Enums;
using Shared.Enums.User;
using Shared.Models.Push;
using Shared.Models.Requests.SessionActions.ChatSession.Models;
using Shared.Serialization;
using Xunit;

namespace Server.Infrastructure.Tests.Coordinator.Push;

public sealed class PushEndpointsTests : IDisposable
{
    private const string Coordinator = "coordinator";
    private const int UserId = 5;
    private const int ChatId = 7;

    private readonly string _certificatePath = Path.GetTempFileName();
    private readonly string _keyPath = Path.GetTempFileName();
    private readonly string _token = PushReplyToken.Create();
    private readonly Mock<IChatSessionManager> _chats = new();
    private readonly Mock<IChatReadStateRepository> _reads = new();
    private readonly GatewayService _gateway;
    private readonly HttpClient _http;

    public PushEndpointsTests()
    {
        using (var rsa = RSA.Create(2048))
        {
            var request = new CertificateRequest("CN=localhost", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
            File.WriteAllText(_certificatePath, certificate.ExportCertificatePem());
            File.WriteAllText(_keyPath, rsa.ExportPkcs8PrivateKeyPem());
        }

        var hash = PushReplyToken.Hash(_token);
        var devices = new Mock<IPushDeviceRepository>();
        devices.Setup(d => d.FindByReplyTokenAsync(Coordinator, It.Is<byte[]>(h => h.SequenceEqual(hash)),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PushDeviceModel(Coordinator, UserId, "fcm", PushPlatform.Android, "00", new byte[32], hash));
        var users = new Mock<IUserManager>();
        users.Setup(u => u.GetUserAsync(UserId)).ReturnsAsync(new UserModel(UserId, Coordinator, "alice",
            UserState.Normal, UserStatus.Online, DateTime.UtcNow, DateTime.UtcNow));
        _chats.Setup(c => c.AddMessageAsync(It.IsAny<int>(), It.IsAny<NewChatMessageModel>(), UserId))
            .ReturnsAsync((int chatId, NewChatMessageModel _, int _) =>
                chatId == ChatId ? AddMessageResult.NoError : AddMessageResult.ChatNotExists);

        var chat = ChatSessionModel.Create(Coordinator, "general", UserId);
        _chats.Setup(c => c.TryGetSession(ChatId, out chat)).Returns(true);
        _reads.Setup(r => r.GetLastMessageIdAsync(ChatId, It.IsAny<CancellationToken>())).ReturnsAsync(50);
        _reads.Setup(r => r.MoveAsync(UserId, ChatId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((int _, int _, int messageId, CancellationToken _) => messageId);
        _reads.Setup(r => r.GetUnreadAsync(ChatId, UserId, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync([]);
        var reads = new ChatReadService(NullLogger.Instance, _reads.Object, new Mock<IMessageSender>().Object);
        reads.Watch(_chats.Object);

        var replies = new PushReplyService(NullLogger.Instance, devices.Object, users.Object, _chats.Object, reads,
            RequestRateLimits.Default, Coordinator);

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
            Coordinator, "127.0.0.1", port, pushEndpoints: new PushHttpEndpoints(replies));
        _gateway.StartAsync(new FakeRouter()).GetAwaiter().GetResult();

        _http = new HttpClient(new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
        })
        {
            BaseAddress = new Uri($"https://127.0.0.1:{port}")
        };
    }

    public void Dispose()
    {
        _http.Dispose();
        _gateway.Dispose();
        File.Delete(_certificatePath);
        File.Delete(_keyPath);
    }

    private async Task<HttpStatusCode> PostAsync(string? token, string body, string path = PushEndpoints.Reply)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path);
        if (token != null)
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = new StringContent(body, Encoding.UTF8, "application/json");

        using var response = await _http.SendAsync(request);
        return response.StatusCode;
    }

    [Fact]
    public async Task Reply_WithTheTokenOfTheDevice_AddsTheMessage()
    {
        Assert.Equal(HttpStatusCode.NoContent, await PostAsync(_token, """{"chatId":7,"text":"hi"}"""));

        _chats.Verify(c => c.AddMessageAsync(ChatId, It.Is<NewChatMessageModel>(m => m.TextContent!.Text == "hi"),
            UserId), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public async Task Reply_WithoutAKnownToken_IsUnauthorized(string? token)
    {
        Assert.Equal(HttpStatusCode.Unauthorized, await PostAsync(token, """{"chatId":7,"text":"hi"}"""));
        _chats.VerifyNoOtherCalls();
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("""{"chatId":7,"text":""}""")]
    [InlineData("""{"chatId":0,"text":"hi"}""")]
    public async Task Reply_OfABodyThatIsNotValid_IsABadRequest(string body)
    {
        Assert.Equal(HttpStatusCode.BadRequest, await PostAsync(_token, body));
        _chats.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Reply_ToAChatThatDoesNotExist_IsNotFound()
    {
        Assert.Equal(HttpStatusCode.NotFound, await PostAsync(_token, """{"chatId":8,"text":"hi"}"""));
    }

    [Fact]
    public async Task MediaEndpoints_DoNotTakeTheReplyToken()
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "/media/images/1?variant=preview");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _token);

        using var response = await _http.SendAsync(request);

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task MarkRead_WithTheTokenOfTheDevice_MovesThePosition()
    {
        Assert.Equal(HttpStatusCode.NoContent,
            await PostAsync(_token, """{"chatId":7,"messageId":42}""", PushEndpoints.MarkRead));

        _reads.Verify(r => r.MoveAsync(UserId, ChatId, 42, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("unknown")]
    public async Task MarkRead_WithoutAKnownToken_IsUnauthorized(string? token)
    {
        Assert.Equal(HttpStatusCode.Unauthorized,
            await PostAsync(token, """{"chatId":7,"messageId":42}""", PushEndpoints.MarkRead));
        _reads.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task MarkRead_OfABodyThatIsNotValid_IsABadRequest()
    {
        Assert.Equal(HttpStatusCode.BadRequest,
            await PostAsync(_token, """{"chatId":7,"messageId":0}""", PushEndpoints.MarkRead));
        _reads.VerifyNoOtherCalls();
    }
}
