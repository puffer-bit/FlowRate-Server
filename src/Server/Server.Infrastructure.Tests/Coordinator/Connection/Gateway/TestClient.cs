using System.Net.WebSockets;
using System.Text;
using Shared.Protocol;

namespace Server.Infrastructure.Tests.Coordinator.Connection.Gateway;

public sealed class TestClient : IAsyncDisposable
{
    public ClientWebSocket Socket { get; } = new();

    public static Task<TestClient> ConnectAsync(int port) =>
        ConnectAsync(port, ProtocolVersion.Format(ProtocolVersion.Current));

    public static async Task<TestClient> ConnectAsync(int port, string? protocolVersion)
    {
        var client = new TestClient();
        client.Socket.Options.RemoteCertificateValidationCallback = (_, _, _, _) => true;
        client.Socket.Options.KeepAliveInterval = TimeSpan.Zero;
        if (protocolVersion != null)
            client.Socket.Options.SetRequestHeader(ProtocolVersion.Header, protocolVersion);
        await client.Socket.ConnectAsync(new Uri($"wss://127.0.0.1:{port}/"), CancellationToken.None);
        return client;
    }

    public Task SendTextAsync(string text) =>
        Socket.SendAsync(Encoding.UTF8.GetBytes(text), WebSocketMessageType.Text, true, CancellationToken.None);

    public async Task<string?> ReceiveTextAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[64 * 1024];
        using var message = new MemoryStream();
        while (true)
        {
            var result = await Socket.ReceiveAsync(buffer, cts.Token);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                await AnswerCloseAsync();
                return null;
            }
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return Encoding.UTF8.GetString(message.ToArray());
        }
    }

    public async Task<(WebSocketCloseStatus? Status, string? Description)> ReceiveCloseAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        var buffer = new byte[64 * 1024];
        try
        {
            while (true)
            {
                var result = await Socket.ReceiveAsync(buffer, cts.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    await AnswerCloseAsync();
                    return (result.CloseStatus, result.CloseStatusDescription);
                }
            }
        }
        catch (WebSocketException)
        {
            return (null, null);
        }
    }

    private async Task AnswerCloseAsync()
    {
        if (Socket.State == WebSocketState.CloseReceived)
            await Socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            if (Socket.State == WebSocketState.Open)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await Socket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, cts.Token);
            }
        }
        catch (Exception ex) when (ex is WebSocketException or OperationCanceledException)
        {
        }
        Socket.Dispose();
    }
}
