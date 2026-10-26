using System.Buffers;
using System.Net.WebSockets;
using System.Text.Json;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using Shared.Models;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Connections;
using Server.Application.Coordinator.Authentication;
using Server.Application.Coordinator.Messaging;

namespace Server.Infrastructure.Coordinator.Connection.Gateway.Kestrel;

public sealed class KestrelCoordinatorInstanceGateway : ICoordinatorInstanceGateway
{
    public const int MaxReceiveMessageSize = 128 * 1024;
    public const int MaxSendQueueLength = 1024;
    public const long MaxSendQueueBytes = 8 * 1024 * 1024;
    public static readonly TimeSpan CloseTimeout = TimeSpan.FromSeconds(5);

    private const int ReceiveChunkSize = 16 * 1024;

    public event Action? Disconnected;

    public string ConnectionId { get; } = Guid.NewGuid().ToString();
    public bool IsAvailable => Volatile.Read(ref _closeRequested) == 0 && _socket.State == WebSocketState.Open;
    public int? UserId { get; set; }
    public string IpAddress { get; }

    private readonly WebSocket _socket;
    private readonly ILogger _logger;
    private readonly IMessageRouter _router;
    private readonly IMessageParser _parser;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly Channel<byte[]> _sendQueue;
    private readonly CancellationTokenSource _abort = new();
    private readonly CancellationTokenSource _closeDeadline = new();

    private int _closeRequested;
    private long _queuedBytes;
    private WebSocketCloseStatus _closeStatus = WebSocketCloseStatus.NormalClosure;
    private string? _closeDescription;

    public KestrelCoordinatorInstanceGateway(
        WebSocket socket,
        string ipAddress,
        ILogger logger,
        IMessageRouter router,
        IMessageParser parser,
        JsonSerializerOptions jsonOptions)
    {
        _socket = socket;
        IpAddress = ipAddress;
        _logger = logger;
        _router = router;
        _parser = parser;
        _jsonOptions = jsonOptions;
        _sendQueue = Channel.CreateBounded<byte[]>(new BoundedChannelOptions(MaxSendQueueLength)
        {
            SingleReader = true,
            FullMode = BoundedChannelFullMode.Wait
        });
    }

    public async Task RunAsync(CancellationToken stopping)
    {
        _logger.LogTrace("Web socket client connection established from {UserEndPoint}.", IpAddress);
        _router.AddConnection(this);

        var sendLoop = Task.Run(SendLoopAsync);
        using var receiveToken = CancellationTokenSource.CreateLinkedTokenSource(_abort.Token, _closeDeadline.Token);
        using var stoppingRegistration = stopping.Register(() =>
            RequestClose(WebSocketCloseStatus.EndpointUnavailable, "Server is stopping"));

        try
        {
            await ReceiveLoopAsync(receiveToken.Token);
        }
        finally
        {
            RequestClose(WebSocketCloseStatus.NormalClosure, null);

            await Task.WhenAny(sendLoop, Task.Delay(CloseTimeout));
            await DrainUntilCloseAsync();
            _abort.Cancel();
            _socket.Abort();
            await sendLoop;

            _logger.LogTrace("Web socket connection {ConnectionId} closed.", ConnectionId);
            try
            {
                Disconnected?.Invoke();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Disconnect handling of connection {ConnectionId} failed.", ConnectionId);
            }
        }
    }

    public void Send(BaseMessage message)
    {
        if (!IsAvailable)
            return;

        byte[] utf8Json;
        try
        {
            utf8Json = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize {MessageType} for connection {ConnectionId}.", message.GetType().Name, ConnectionId);
            return;
        }

        Enqueue(utf8Json);
    }

    public void SendSerialized(byte[] utf8Json)
    {
        if (IsAvailable)
            Enqueue(utf8Json);
    }

    private void Enqueue(byte[] utf8Json)
    {
        // A slow reader is bounded by bytes too, since a few large messages weigh more than many small ones
        if (Interlocked.Add(ref _queuedBytes, utf8Json.Length) <= MaxSendQueueBytes
            && _sendQueue.Writer.TryWrite(utf8Json))
            return;

        Interlocked.Add(ref _queuedBytes, -utf8Json.Length);
        if (Volatile.Read(ref _closeRequested) == 0)
        {
            _logger.LogWarning("Send queue of connection {ConnectionId} overflowed. Closing the connection.", ConnectionId);
            GatewayMetrics.SendQueueOverflows.Add(1);
            RequestClose(WebSocketCloseStatus.PolicyViolation, "Send queue overflow");
        }
    }

    public void Close(ConnectionCloseReason reason = ConnectionCloseReason.Normal)
    {
        switch (reason)
        {
            case ConnectionCloseReason.AuthenticationTimeout:
                RequestClose(WebSocketCloseStatus.PolicyViolation, "Authentication timeout");
                break;
            case ConnectionCloseReason.TooManyRejectedRequests:
                RequestClose(WebSocketCloseStatus.PolicyViolation, "Too many rejected requests");
                break;
            default:
                RequestClose(WebSocketCloseStatus.NormalClosure, null);
                break;
        }
    }

    private void RequestClose(WebSocketCloseStatus status, string? description)
    {
        if (Interlocked.Exchange(ref _closeRequested, 1) != 0)
            return;

        _closeStatus = status;
        _closeDescription = description;
        _sendQueue.Writer.TryComplete();

        try
        {
            _closeDeadline.CancelAfter(CloseTimeout);
        }
        catch (ObjectDisposedException)
        {
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken token)
    {
        var chunk = ArrayPool<byte>.Shared.Rent(ReceiveChunkSize);
        var message = new ArrayBufferWriter<byte>(ReceiveChunkSize);

        try
        {
            while (true)
            {
                var result = await _socket.ReceiveAsync(chunk.AsMemory(), token);

                if (result.MessageType == WebSocketMessageType.Close)
                    return;

                if (message.WrittenCount + result.Count > MaxReceiveMessageSize)
                {
                    _logger.LogWarning("Connection {ConnectionId} from {Ip} sent a message over {Limit} bytes. Closing the connection.",
                        ConnectionId, IpAddress, MaxReceiveMessageSize);
                    GatewayMetrics.OversizedMessages.Add(1);
                    RequestClose(WebSocketCloseStatus.MessageTooBig, $"Message exceeds {MaxReceiveMessageSize} bytes");
                    return;
                }

                message.Write(chunk.AsSpan(0, result.Count));
                if (!result.EndOfMessage)
                    continue;

                if (!Dispatch(result.MessageType, message.WrittenSpan))
                    return;

                if (message.Capacity > ReceiveChunkSize * 2)
                    message = new ArrayBufferWriter<byte>(ReceiveChunkSize);
                else
                    message.ResetWrittenCount();
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            _logger.LogDebug("Connection {ConnectionId} broken: {Message}", ConnectionId, ex.Message);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(chunk);
        }
    }

    private async Task DrainUntilCloseAsync()
    {
        if (_socket.State != WebSocketState.CloseSent)
            return;

        var buffer = ArrayPool<byte>.Shared.Rent(ReceiveChunkSize);
        try
        {
            using var token = CancellationTokenSource.CreateLinkedTokenSource(_abort.Token, _closeDeadline.Token);
            while (_socket.State == WebSocketState.CloseSent)
            {
                var result = await _socket.ReceiveAsync(buffer.AsMemory(), token.Token);
                if (result.MessageType == WebSocketMessageType.Close)
                    return;
            }
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or IOException)
        {
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }

    private bool Dispatch(WebSocketMessageType type, ReadOnlySpan<byte> payload)
    {
        if (type != WebSocketMessageType.Text)
        {
            _logger.LogWarning("Connection {ConnectionId} sent a {MessageType} message. Closing the connection.", ConnectionId, type);
            RequestClose(WebSocketCloseStatus.InvalidMessageType, "Only text messages are supported");
            return false;
        }

        var message = _parser.ParseBaseMessage(payload);
        if (message == null)
        {
            GatewayMetrics.MalformedMessages.Add(1);
            _router.RejectMalformedMessage(ConnectionId);
            return true;
        }

        message.SenderId = UserId ?? 0;

        try
        {
            _router.RouteIncomingMessage(message, ConnectionId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Routing {MessageType} from connection {ConnectionId} failed.", message.GetType().Name, ConnectionId);
        }

        return true;
    }

    private async Task SendLoopAsync()
    {
        var token = _abort.Token;

        try
        {
            await foreach (var outgoing in _sendQueue.Reader.ReadAllAsync(token))
            {
                await _socket.SendAsync(outgoing.AsMemory(), WebSocketMessageType.Text, endOfMessage: true, token);
                Interlocked.Add(ref _queuedBytes, -outgoing.Length);
            }

            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
                await _socket.CloseOutputAsync(_closeStatus, _closeDescription, token);
        }
        catch (Exception ex) when (ex is OperationCanceledException or WebSocketException or ObjectDisposedException or IOException)
        {
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Sending on connection {ConnectionId} failed: {Message}", ConnectionId, ex.Message);
        }
    }
}
