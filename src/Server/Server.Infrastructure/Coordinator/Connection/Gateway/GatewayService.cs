using System.Collections.Concurrent;
using System.Net;
using System.Security.Authentication;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Server.Infrastructure.Coordinator.Connection.Gateway.Kestrel;
using Server.Infrastructure.Coordinator.Media;
using Server.Infrastructure.Coordinator.Push;
using Server.Infrastructure.Security;
using Server.Application.Abstractions.Gateway;
using Server.Application.Coordinator.Messaging;
using Server.Application.Coordinator.Connections.RateLimiting;
using Shared.Protocol;

namespace Server.Infrastructure.Coordinator.Connection.Gateway;

public class GatewayService : IGatewayService
{
    private static readonly TimeSpan StopTimeout = TimeSpan.FromSeconds(10);

    public bool IsGatewayReady { get; private set; }

    private WebApplication? _app;
    private CancellationTokenSource? _stopping;
    private readonly ConcurrentDictionary<string, int> _connectionsPerIp = new();

    private readonly string _ip;
    private readonly int _port;

    private readonly ILogger _logger;
    private readonly IMessageParser _parser;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly ServerCertificate _certificate;
    private readonly IMediaHttpEndpoints? _mediaEndpoints;
    private readonly IPushHttpEndpoints? _pushEndpoints;
    private readonly RequestRateLimits _rateLimits;

    public GatewayService(ILogger logger,
        JsonSerializerOptions jsonOptions,
        IMessageParser parser,
        ServerCertificate certificate,
        string coordinatorInstanceId, string ip, int port,
        IMediaHttpEndpoints? mediaEndpoints = null,
        RequestRateLimits? rateLimits = null,
        IPushHttpEndpoints? pushEndpoints = null)
    {
        _rateLimits = rateLimits ?? RequestRateLimits.Default;
        _mediaEndpoints = mediaEndpoints;
        _pushEndpoints = pushEndpoints;
        _ip = ip;
        _port = port;
        _logger = logger;
        _parser = parser;
        _jsonOptions = jsonOptions;
        _certificate = certificate;
    }

    public async Task StartAsync(IMessageRouter messageRouter)
    {
        if (!IPAddress.TryParse(_ip, out var address))
            throw new InvalidOperationException($"Coordinator listen IP '{_ip}' is not a valid IP address.");
        if (_port is <= 0 or > 65535)
            throw new InvalidOperationException($"Coordinator port {_port} is out of range.");
        if (_app != null)
            throw new InvalidOperationException("The gateway is already running.");

        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions());
        builder.Services.AddLogging();
        builder.Services.AddRouting();
        builder.WebHost.UseKestrelCore();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.AddServerHeader = false;
            kestrel.Listen(address, _port, listen => listen.UseHttps(https =>
            {
                https.ServerCertificate = _certificate.Certificate;
                https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
            }));
        });

        var app = builder.Build();
        var stopping = new CancellationTokenSource();

        app.UseWebSockets(new WebSocketOptions { KeepAliveInterval = TimeSpan.Zero });
        _mediaEndpoints?.Map(app);
        _pushEndpoints?.Map(app);
        app.MapFallback(context => HandleConnectionAsync(context, messageRouter, stopping.Token));

        try
        {
            await app.StartAsync();
        }
        catch (Exception ex)
        {
            stopping.Dispose();
            await app.DisposeAsync();
            throw new InvalidOperationException(
                $"Coordinator gateway could not listen on {_ip}:{_port}: {ex.GetBaseException().Message}", ex);
        }

        _app = app;
        _stopping = stopping;
        IsGatewayReady = true;
        _logger.LogInformation("WebSocket server listening on IP:{Ip} and Port:{Port}.", _ip, _port);
    }

    private async Task HandleConnectionAsync(HttpContext context, IMessageRouter messageRouter, CancellationToken stopping)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        var remoteAddress = context.Connection.RemoteIpAddress;
        if (remoteAddress is { IsIPv4MappedToIPv6: true })
            remoteAddress = remoteAddress.MapToIPv4();
        var ip = remoteAddress?.ToString() ?? string.Empty;

        var clientVersion = context.Request.Headers[ProtocolVersion.Header].ToString();
        if (!ProtocolVersion.IsSupported(clientVersion))
        {
            _logger.LogInformation("Rejected a connection from {Ip}: protocol version '{ClientVersion}' is outside {MinVersion}..{Version}.",
                ip, clientVersion, ProtocolVersion.MinSupported, ProtocolVersion.Current);
            context.Response.Headers[ProtocolVersion.Header] = ProtocolVersion.Format(ProtocolVersion.Current);
            context.Response.Headers[ProtocolVersion.MinHeader] = ProtocolVersion.Format(ProtocolVersion.MinSupported);
            context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
            return;
        }

        var maxConnectionsPerIp = _rateLimits.MaxConnectionsPerIp;
        if (_connectionsPerIp.AddOrUpdate(ip, 1, (_, count) => count + 1) > maxConnectionsPerIp)
        {
            ReleaseConnectionSlot(ip);
            _logger.LogWarning("Rejected a connection from {Ip}: more than {Limit} open connections.", ip, maxConnectionsPerIp);
            GatewayMetrics.RejectedConnections.Add(1);
            context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            return;
        }

        try
        {
            using var socket = await context.WebSockets.AcceptWebSocketAsync();
            GatewayMetrics.AcceptedConnections.Add(1);
            GatewayMetrics.OpenConnections.Add(1);

            var gateway = new KestrelCoordinatorInstanceGateway(
                socket,
                ip,
                _logger,
                messageRouter,
                _parser,
                _jsonOptions);

            try
            {
                await gateway.RunAsync(stopping);
            }
            finally
            {
                GatewayMetrics.OpenConnections.Add(-1);
            }
            context.Abort();
        }
        finally
        {
            ReleaseConnectionSlot(ip);
        }
    }

    private void ReleaseConnectionSlot(string ip)
    {
        while (_connectionsPerIp.TryGetValue(ip, out var count))
        {
            if (count <= 1)
            {
                if (_connectionsPerIp.TryRemove(new KeyValuePair<string, int>(ip, count)))
                    return;
            }
            else if (_connectionsPerIp.TryUpdate(ip, count - 1, count))
            {
                return;
            }
        }
    }

    public async Task StopAsync()
    {
        var app = _app;
        if (app == null)
            return;

        _app = null;
        IsGatewayReady = false;

        _stopping?.Cancel();
        try
        {
            using var timeout = new CancellationTokenSource(StopTimeout);
            await app.StopAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("WebSocket server on IP:{Ip} and Port:{Port} did not stop in time.", _ip, _port);
        }
        finally
        {
            await app.DisposeAsync();
            _stopping?.Dispose();
            _stopping = null;
        }

        _logger.LogInformation("WebSocket server on IP:{Ip} and Port:{Port} stopped.", _ip, _port);
    }

    public void Dispose()
    {
        StopAsync().GetAwaiter().GetResult();
    }
}
