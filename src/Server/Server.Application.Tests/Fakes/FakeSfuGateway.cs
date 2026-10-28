using System.Collections.Concurrent;
using Shared.Enums;
using Shared.Models.Media;
using Server.Core.Models.HybridSession;
using Shared.Models.Media.Transport;
using Server.Application.Abstractions.Sfu;
using Server.Application.Coordinator.Sessions.Chat;
using Server.Application.Coordinator.Sessions.Hybrid;
using Server.Application.Coordinator.Sessions.Hybrid.Peers;
using Server.Application.Coordinator.Users;

namespace Server.Application.Tests.Fakes;

/// <summary>In-memory SFU node that records calls and can fail, time out or hold inbound creation.</summary>
internal sealed class FakeSfuGateway : ISFUGateway
{
    public int NodeId { get; init; } = 1;
    public bool IsConnected { get; set; } = true;

    public event Func<Task>? ConnectionLost;
    public event Func<Task>? Connected;
    public event Action? StatusChanged;

    public SfuNodeStatus Status => SfuNodeStatus.Initial() with
    {
        State = IsConnected ? SFUNodeConnectionState.Connected : SFUNodeConnectionState.Disconnected
    };

    public SFUTransportCreateResult TransportResult { get; set; } = SFUTransportCreateResult.NoError;

    /// <summary>Every call throws <see cref="TimeoutException"/>, as the real gateway does when a deadline passes.</summary>
    public bool TimesOut { get; set; }

    /// <summary>Listeners whose inbound creation fails.</summary>
    public HashSet<int> FailInboundFor { get; } = [];

    /// <summary>When set, inbound creation waits for it, so that a test can see how many run at once.</summary>
    public TaskCompletionSource? InboundGate { get; set; }

    public int InboundsInFlight => _inboundsInFlight;
    public int MaxInboundsInFlight => _maxInboundsInFlight;

    private readonly ConcurrentQueue<string> _calls = new();
    private int _inboundsInFlight;
    private int _maxInboundsInFlight;
    private int _outboundCounter;

    public IReadOnlyList<string> Calls => _calls.ToList();

    public int CallCount(string name) => Calls.Count(call => call == name || call.StartsWith(name + ":"));

    public Task RaiseConnectionLost()
    {
        IsConnected = false;
        return ConnectionLost?.Invoke() ?? Task.CompletedTask;
    }

    public Task RaiseConnected()
    {
        IsConnected = true;
        return Connected?.Invoke() ?? Task.CompletedTask;
    }

    public Task Connect() => Task.CompletedTask;
    public Task Disconnect() => Task.CompletedTask;

    public Task<(SFUTransportCreateResult result, TransportData? transportData)> CreatePeerTransport(int session,
        int userId, string clientEpoch)
    {
        Record($"CreatePeerTransport:{userId}");
        var data = TransportResult == SFUTransportCreateResult.NoError ? Transport() : null;
        return Task.FromResult((TransportResult, data));
    }

    public Task<SFUTransportConnectResult> ConnectPeerTransport(int sessionId, int userId, DTLSParameters dtlsParameters)
    {
        Record($"ConnectPeerTransport:{userId}");
        return Task.FromResult(SFUTransportConnectResult.NoError);
    }

    public Task ClosePeerTransport(int sessionId, int userId)
    {
        Record($"ClosePeerTransport:{userId}");
        return Task.CompletedTask;
    }

    public Task<(SFUOutboundCreateResult result, Outbound? outbound)> AddOutbound(int sessionId, int userId,
        MediaSourceType sourceType, MediaType mediaType, OutboundMediaSettings settings)
    {
        Record($"AddOutbound:{userId}");
        var id = Interlocked.Increment(ref _outboundCounter);
        return Task.FromResult<(SFUOutboundCreateResult, Outbound?)>((SFUOutboundCreateResult.NoError,
            new Outbound
            {
                Id = $"out-{id}", UserId = userId, SSRC = (uint)id, SourceType = sourceType, MediaType = mediaType,
                MimeType = "audio/opus", PayloadType = 100, ClockRate = 48000, Channels = 2
            }));
    }

    public Task<SFUOutboundCreateResult> PauseOutbound(int sessionId, string outboundId) =>
        Result($"PauseOutbound:{outboundId}", SFUOutboundCreateResult.NoError);

    public Task<SFUOutboundCreateResult> ResumeOutbound(int sessionId, string outboundId) =>
        Result($"ResumeOutbound:{outboundId}", SFUOutboundCreateResult.NoError);

    public Task<SFUOutboundCreateResult> UpdateOutbound(int sessionId, int userId, Outbound outbound) =>
        throw new NotSupportedException();

    public Task<SFUOutboundCreateResult> RemoveOutbound(int sessionId, int userId, string outboundId) =>
        Result($"RemoveOutbound:{outboundId}", SFUOutboundCreateResult.NoError);

    public Task<SFUOutboundDowngradeResult> DowngradeOutbound(int sessionId, int userId, MediaSourceType sourceType,
        MediaType? type = null) => throw new NotSupportedException();

    public Task<SFUOutboundUpgradeResult> UpgradeOutbound(int sessionId, int userId, MediaSourceType sourceType,
        MediaType? type = null) => throw new NotSupportedException();

    public async Task<(SFUInboundCreateResult result, Inbound? inbound)> AddInbound(int sessionId, int userId,
        Outbound outbound, InboundMediaSettings settings)
    {
        Record($"AddInbound:{userId}");

        var inFlight = Interlocked.Increment(ref _inboundsInFlight);
        InterlockedMax(ref _maxInboundsInFlight, inFlight);
        try
        {
            if (InboundGate != null)
                await InboundGate.Task;

            if (FailInboundFor.Contains(userId))
                return (SFUInboundCreateResult.SFUNodeError, null);

            return (SFUInboundCreateResult.NoError, new Inbound
            {
                Id = $"in-{userId}-{outbound.Id}", UserId = outbound.UserId, SSRC = outbound.SSRC,
                SourceType = outbound.SourceType, MediaType = outbound.MediaType, MimeType = outbound.MimeType,
                PayloadType = outbound.PayloadType, ClockRate = outbound.ClockRate, Channels = outbound.Channels
            });
        }
        finally
        {
            Interlocked.Decrement(ref _inboundsInFlight);
        }
    }

    public Task<SFUInboundCreateResult> RemoveInbound(int sessionId, int userId, string inboundId) =>
        Result($"RemoveInbound:{inboundId}", SFUInboundCreateResult.NoError);

    public Task<SFUInboundCreateResult> PauseInbound(int sessionId, string inboundId) =>
        Result($"PauseInbound:{inboundId}", SFUInboundCreateResult.NoError);

    public Task<SFUInboundCreateResult> ResumeInbound(int sessionId, string inboundId) =>
        Result($"ResumeInbound:{inboundId}", SFUInboundCreateResult.NoError);

    public Task<List<Inbound>> GetAllInbounds(int sessionId, int? userId) => Task.FromResult(new List<Inbound>());

    public Task LoadSession(int sessionId)
    {
        Record("LoadSession");
        return Task.CompletedTask;
    }

    public Task RestartSession(int sessionId) => throw new NotSupportedException();

    public Task UnloadSession(int sessionId)
    {
        Record("UnloadSession");
        return Task.CompletedTask;
    }

    private Task<T> Result<T>(string call, T result)
    {
        Record(call);
        return Task.FromResult(result);
    }

    private void Record(string call)
    {
        if (TimesOut)
            throw new TimeoutException($"SFU call {call} timed out.");

        _calls.Enqueue(call);
    }

    private static void InterlockedMax(ref int target, int value)
    {
        int current;
        while (value > (current = Volatile.Read(ref target))
               && Interlocked.CompareExchange(ref target, value, current) != current)
        {
        }
    }

    private static TransportData Transport() => new(
        Host: "127.0.0.1",
        Port: 40000,
        DTLSParameters: Dtls(),
        ICEParameters: new ICEParameters { UsernameFragment = "ufrag", Password = "pwd", Candidates = [] });

    public static DTLSParameters Dtls() => new() { Role = "auto", Fingerprints = [] };
}
