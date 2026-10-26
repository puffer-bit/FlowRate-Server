using Microsoft.Extensions.Logging;
using Shared.Enums;
using Shared.Models.Media;
using Server.Core.Models.HybridSession;
using Server.Application.Coordinator.Events;

namespace Server.Application.Coordinator.Sessions.Hybrid.Peers;

/// <summary>
/// Media routing: which peer publishes voice and screen, and which peers consume them. Called under the lock only.
/// </summary>
public partial class HybridSessionPeerManager
{
    private readonly Dictionary<int, Outbound> _voiceOutbounds = new();
    private readonly Dictionary<int, Outbound> _screenOutbounds = new();
    private readonly Dictionary<int, Outbound> _screenAudioOutbounds = new();
    private readonly Dictionary<(int ListenerUserId, string OutboundId), Inbound> _inbounds = new();
    private readonly HashSet<(int ListenerUserId, string OutboundId)> _screenWatchers = new();
    private readonly HashSet<string> _readyInbounds = new();

    public Task<(StartVoiceShareResult result, Outbound? outbound)> StartVoiceShareAsync(int userId) =>
        LockedAsync(async () =>
    {
        if (!EnsureReady("start voice share", userId))
            return (StartVoiceShareResult.SFUNotActive, null);

        if (!_peers.TryGetValue(userId, out var speaker))
        {
            _logger.LogError("Peer with ID {userId} failed to start voice. Peer not exists.", userId);
            return (StartVoiceShareResult.PeerNotExists, null);
        }

        if (!speaker.IsAdmitted)
            return (StartVoiceShareResult.Rejected, null);

        if (_voiceOutbounds.TryGetValue(userId, out var existing))
            return (StartVoiceShareResult.NoError, existing);

        var (result, outbound) = await Gateway.AddOutbound(Model.Id, userId, MediaSourceType.Microphone, MediaType.Audio,
            Model.Media.ForOutbound(MediaSourceType.Microphone, MediaType.Audio));
        if (result != SFUOutboundCreateResult.NoError || outbound == null)
        {
            _logger.LogError("Peer with ID {userId} failed to start voice. SFU service return error: {error}", userId, result);
            return (StartVoiceShareResult.SFUError, null);
        }

        _voiceOutbounds[userId] = outbound;
        speaker.SetMicrophoneMuted(false);

        await ConsumeAllAsync(_peers.Values
            .Where(listener => listener.UserId != userId && listener.CanReceiveMedia)
            .Select(listener => (listener.UserId, outbound)));

        _logger.LogTrace("Peer with ID {userId} successful started voice.", userId);
        return (StartVoiceShareResult.NoError, outbound);
    }, timedOut: (StartVoiceShareResult.TimedOut, null));

    public Task<StopVoiceShareResult> StopVoiceShareAsync(int initiatorId, int userId) => LockedAsync(async () =>
    {
        if (!EnsureReady("stop voice share", userId))
            return StopVoiceShareResult.SFUNotActive;

        if (!_peers.TryGetValue(userId, out var speaker))
        {
            _logger.LogError("Peer with ID {userId} failed to stop voice. Peer not exists.", userId);
            return StopVoiceShareResult.PeerNotExists;
        }

        if (!_voiceOutbounds.Remove(userId, out var outbound))
        {
            _logger.LogDebug("Peer with ID {userId} has no voice to stop.", userId);
            return StopVoiceShareResult.NoError;
        }

        // Without a voice outbound there is nothing to be muted
        speaker.SetMicrophoneMuted(false);
        ForgetInbounds(outbound.Id);

        var result = await Gateway.RemoveOutbound(Model.Id, userId, outbound.Id);
        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStoppedVoiceShareEvent(Model.Id, userId,
            InitiatorOrNull(initiatorId, userId)));

        if (result != SFUOutboundCreateResult.NoError)
        {
            _logger.LogError("Peer with ID {userId} failed to stop voice. SFU service return error: {error}", userId, result);
            return StopVoiceShareResult.SFUError;
        }

        _logger.LogTrace("Peer with ID {userId} successful stopped voice.", userId);
        return StopVoiceShareResult.NoError;
    }, timedOut: StopVoiceShareResult.TimedOut);

    public Task<SetMicrophoneMuteResult> SetMicrophoneMutedAsync(int initiatorId, int userId, bool isMuted) =>
        LockedAsync(async () =>
    {
        if (!EnsureReady("change microphone mute", userId))
            return SetMicrophoneMuteResult.SFUNotActive;

        if (!_peers.TryGetValue(userId, out var peer))
        {
            _logger.LogError("Peer with ID {userId} failed to change microphone mute. Peer not exists.", userId);
            return SetMicrophoneMuteResult.PeerNotExists;
        }

        if (!_voiceOutbounds.TryGetValue(userId, out var outbound))
        {
            _logger.LogDebug("Peer with ID {userId} has no voice to mute.", userId);
            return SetMicrophoneMuteResult.VoiceNotStarted;
        }

        if (peer.IsMicrophoneMuted == isMuted)
            return SetMicrophoneMuteResult.NoError;

        var result = isMuted
            ? await Gateway.PauseOutbound(Model.Id, outbound.Id)
            : await Gateway.ResumeOutbound(Model.Id, outbound.Id);

        if (result != SFUOutboundCreateResult.NoError)
        {
            _logger.LogError("Peer with ID {userId} failed to change microphone mute. SFU service return error: {error}", userId, result);
            return SetMicrophoneMuteResult.SFUError;
        }

        peer.SetMicrophoneMuted(isMuted);

        var initiator = InitiatorOrNull(initiatorId, userId);
        if (isMuted)
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerMuteMicrophoneEvent(Model.Id, userId, initiator));
        else
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerUnmuteMicrophoneEvent(Model.Id, userId, initiator));

        _logger.LogTrace("Peer with ID {userId} microphone {state}.", userId, isMuted ? "muted" : "unmuted");
        return SetMicrophoneMuteResult.NoError;
    }, timedOut: SetMicrophoneMuteResult.TimedOut);

    // Only a flag with no SFU call, so it does not wait behind the session lock
    public async Task<SetSoundMuteResult> SetSoundMutedAsync(int initiatorId, int userId, bool isMuted)
    {
        if (!_peers.TryGetValue(userId, out var peer) || peer.State == HybridSessionPeerState.Closed)
        {
            _logger.LogError("Peer with ID {userId} failed to change sound mute. Peer not exists.", userId);
            return SetSoundMuteResult.PeerNotExists;
        }

        if (!peer.SetSoundMuted(isMuted))
            return SetSoundMuteResult.NoError;

        var initiator = InitiatorOrNull(initiatorId, userId);
        if (isMuted)
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerMuteSoundEvent(Model.Id, userId, initiator));
        else
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerUnmuteSoundEvent(Model.Id, userId, initiator));

        _logger.LogTrace("Peer with ID {userId} sound {state}.", userId, isMuted ? "muted" : "unmuted");
        return SetSoundMuteResult.NoError;
    }

    // Flags and a notice with no SFU call, so neither waits behind the session lock
    public async Task<SetAfkResult> SetAfkAsync(int userId, bool isAfk, string? reason)
    {
        if (!_peers.TryGetValue(userId, out var peer) || peer.State == HybridSessionPeerState.Closed)
        {
            _logger.LogDebug("Peer with ID {userId} failed to change AFK. Peer not exists.", userId);
            return SetAfkResult.PeerNotExists;
        }

        if (!peer.SetAfk(isAfk, reason))
            return SetAfkResult.NoError;

        if (isAfk)
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerAfkEvent(Model.Id, userId, peer.AfkReason));
        else
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerNotAfkEvent(Model.Id, userId));

        _logger.LogTrace("Peer with ID {userId} is {state}.", userId, isAfk ? "AFK" : "back");
        return SetAfkResult.NoError;
    }

    public async Task<PokeResult> PokeAsync(int initiatorId, int userId, string? message)
    {
        if (initiatorId == userId)
            return PokeResult.CannotPokeSelf;

        if (!_peers.TryGetValue(initiatorId, out var initiator) || initiator.State == HybridSessionPeerState.Closed
            || !_peers.TryGetValue(userId, out var peer) || peer.State == HybridSessionPeerState.Closed)
            return PokeResult.PeerNotExists;

        message = string.IsNullOrWhiteSpace(message) ? null : message.Trim();
        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerPokedEvent(Model.Id, userId, initiatorId, message,
            peer.ConnectionId));

        _logger.LogTrace("Peer with ID {initiatorId} poked peer with ID {userId}.", initiatorId, userId);
        return PokeResult.NoError;
    }

    public Task<(StartVideoShareResult result, Outbound? outbound)> StartVideoShareAsync(int userId) =>
        LockedAsync(async () =>
    {
        if (!EnsureReady("start video share", userId))
            return (StartVideoShareResult.SFUNotActive, null);

        if (!_peers.TryGetValue(userId, out var peer))
        {
            _logger.LogError("Peer with ID {userId} failed to start video. Peer not exists.", userId);
            return (StartVideoShareResult.PeerNotExists, null);
        }

        if (!peer.IsAdmitted)
            return (StartVideoShareResult.Rejected, null);

        var (result, outbound) = await Gateway.AddOutbound(Model.Id, userId, MediaSourceType.Camera, MediaType.Video,
            Model.Media.ForOutbound(MediaSourceType.Camera, MediaType.Video));
        if (result != SFUOutboundCreateResult.NoError || outbound == null)
        {
            _logger.LogError("Peer with ID {userId} failed to start video. SFU service return error: {error}", userId, result);
            return (StartVideoShareResult.SFUError, null);
        }

        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStartedVideoShareEvent(Model.Id, userId, outbound));

        _logger.LogTrace("Peer with ID {userId} successful started video.", userId);
        return (StartVideoShareResult.NoError, outbound);
    }, timedOut: (StartVideoShareResult.TimedOut, null));

    public Task<StopVideoShareResult> StopVideoShareAsync(int initiatorId, int userId)
    {
        throw new NotImplementedException();
    }

    public Task<(StartScreenShareResult result, Outbound? outbound, Outbound? audioOutbound)> StartScreenShareAsync(
        int userId, bool withAudio) => LockedAsync(async () =>
    {
        if (!EnsureReady("start screen share", userId))
            return (StartScreenShareResult.SFUNotActive, null, null);

        if (!_peers.TryGetValue(userId, out var sharer))
        {
            _logger.LogError("Peer with ID {userId} failed to start screen share. Peer not exists.", userId);
            return (StartScreenShareResult.PeerNotExists, null, null);
        }

        // Waiting for approval or rejected: such a peer may not be heard or seen
        if (!sharer.IsAdmitted)
            return (StartScreenShareResult.Rejected, null, null);

        // A running share keeps what it was started with; the sound is changed by starting it again
        if (sharer.IsScreenShareActive && _screenOutbounds.TryGetValue(userId, out var active))
            return (StartScreenShareResult.NoError, active,
                sharer.IsScreenAudioActive ? _screenAudioOutbounds.GetValueOrDefault(userId) : null);

        var outbound = await PublishScreenAsync(userId, MediaType.Video, _screenOutbounds);
        if (outbound == null)
            return (StartScreenShareResult.SFUError, null, null);

        Outbound? audioOutbound = null;
        if (withAudio)
        {
            audioOutbound = await PublishScreenAsync(userId, MediaType.Audio, _screenAudioOutbounds);
            if (audioOutbound == null)
            {
                await Gateway.PauseOutbound(Model.Id, outbound.Id);
                return (StartScreenShareResult.SFUError, null, null);
            }
        }

        sharer.SetScreenShareActive(true, withAudio);
        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStartedScreenShareEvent(Model.Id, userId,
            HasAudio: withAudio));

        _logger.LogTrace("Peer with ID {userId} successful started screen share, with audio: {withAudio}.", userId, withAudio);
        return (StartScreenShareResult.NoError, outbound, audioOutbound);
    }, timedOut: (StartScreenShareResult.TimedOut, null, null));

    /// <summary>
    /// Resumes the screen outbound of the media type a previous share left paused, or creates it.
    /// </summary>
    /// <returns>Null when the SFU failed.</returns>
    private async Task<Outbound?> PublishScreenAsync(int userId, MediaType mediaType, Dictionary<int, Outbound> outbounds)
    {
        if (outbounds.TryGetValue(userId, out var outbound))
        {
            var resumeResult = await Gateway.ResumeOutbound(Model.Id, outbound.Id);
            if (resumeResult == SFUOutboundCreateResult.NoError)
                return outbound;

            _logger.LogError("Peer with ID {userId} failed to resume screen share {mediaType}. SFU service return error: {error}",
                userId, mediaType, resumeResult);
            return null;
        }

        var (result, created) = await Gateway.AddOutbound(Model.Id, userId, MediaSourceType.Screen, mediaType,
            Model.Media.ForOutbound(MediaSourceType.Screen, mediaType));
        if (result != SFUOutboundCreateResult.NoError || created == null)
        {
            _logger.LogError("Peer with ID {userId} failed to start screen share {mediaType}. SFU service return error: {error}",
                userId, mediaType, result);
            return null;
        }

        outbounds[userId] = created;
        return created;
    }

    public Task<StopScreenShareResult> StopScreenShareAsync(int initiatorId, int userId) => LockedAsync(async () =>
    {
        if (!EnsureReady("stop screen share", userId))
            return StopScreenShareResult.SFUNotActive;

        if (!_peers.TryGetValue(userId, out var sharer))
        {
            _logger.LogError("Peer with ID {userId} failed to stop screen share. Peer not exists.", userId);
            return StopScreenShareResult.PeerNotExists;
        }

        if (!sharer.IsScreenShareActive || !_screenOutbounds.TryGetValue(userId, out var outbound))
        {
            _logger.LogDebug("Peer with ID {userId} has no screen share to stop.", userId);
            return StopScreenShareResult.NoError;
        }

        var audioOutbound = sharer.IsScreenAudioActive ? _screenAudioOutbounds.GetValueOrDefault(userId) : null;

        sharer.SetScreenShareActive(false);
        await PauseScreenWatchersAsync(outbound.Id);

        var result = await Gateway.PauseOutbound(Model.Id, outbound.Id);

        if (audioOutbound != null)
        {
            await PauseScreenWatchersAsync(audioOutbound.Id);

            var audioResult = await Gateway.PauseOutbound(Model.Id, audioOutbound.Id);
            if (result == SFUOutboundCreateResult.NoError)
                result = audioResult;
        }

        await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStoppedScreenShareEvent(Model.Id, userId,
            InitiatorOrNull(initiatorId, userId)));

        if (result != SFUOutboundCreateResult.NoError)
        {
            _logger.LogError("Peer with ID {userId} failed to stop screen share. SFU service return error: {error}", userId, result);
            return StopScreenShareResult.SFUError;
        }

        _logger.LogTrace("Peer with ID {userId} successful stopped screen share.", userId);
        return StopScreenShareResult.NoError;
    }, timedOut: StopScreenShareResult.TimedOut);

    public Task<(SetScreenShareWatchResult result, Inbound? inbound, Inbound? audioInbound)> SetScreenShareWatchAsync(
        int listenerUserId, int userId, bool isWatching) => LockedAsync(async () =>
    {
        if (!EnsureReady("change screen share watch", listenerUserId))
            return (SetScreenShareWatchResult.SFUNotActive, null, null);

        if (!_peers.TryGetValue(listenerUserId, out var listener) || !_peers.TryGetValue(userId, out var sharer) ||
            listenerUserId == userId)
        {
            _logger.LogError("Peer with ID {listenerUserId} failed to change watch of screen share of peer {userId}. Peer not exists.",
                listenerUserId, userId);
            return (SetScreenShareWatchResult.PeerNotExists, null, null);
        }

        _screenOutbounds.TryGetValue(userId, out var outbound);
        _screenAudioOutbounds.TryGetValue(userId, out var audioOutbound);

        if (!isWatching)
        {
            var (result, watched) = await UnwatchScreenOutboundAsync(listenerUserId, outbound);
            var (audioResult, heard) = await UnwatchScreenOutboundAsync(listenerUserId, audioOutbound);

            if (result != SFUInboundCreateResult.NoError || audioResult != SFUInboundCreateResult.NoError)
            {
                _logger.LogError("Peer with ID {listenerUserId} failed to stop watching screen share of peer {userId}. SFU service return error: {error}",
                    listenerUserId, userId, result != SFUInboundCreateResult.NoError ? result : audioResult);
                return (SetScreenShareWatchResult.SFUError, null, null);
            }

            if (watched != null)
                _logger.LogTrace("Peer with ID {listenerUserId} stopped watching screen share of peer {userId}.", listenerUserId, userId);
            return (SetScreenShareWatchResult.NoError, watched, heard);
        }

        if (outbound == null || !sharer.IsScreenShareActive)
            return (SetScreenShareWatchResult.ScreenShareNotActive, null, null);

        if (!listener.CanReceiveMedia)
            return (SetScreenShareWatchResult.PeerNotExists, null, null);

        var inbound = await WatchScreenOutboundAsync(listenerUserId, outbound);
        if (inbound == null)
            return (SetScreenShareWatchResult.SFUError, null, null);

        Inbound? audioInbound = null;
        if (sharer.IsScreenAudioActive && audioOutbound != null)
        {
            audioInbound = await WatchScreenOutboundAsync(listenerUserId, audioOutbound);
            if (audioInbound == null)
            {
                await UnwatchScreenOutboundAsync(listenerUserId, outbound);
                return (SetScreenShareWatchResult.SFUError, null, null);
            }
        }

        _logger.LogTrace("Peer with ID {listenerUserId} started watching screen share of peer {userId}.", listenerUserId, userId);
        return (SetScreenShareWatchResult.NoError, inbound, audioInbound);
    }, timedOut: (SetScreenShareWatchResult.TimedOut, null, null));

    /// <summary>
    /// Makes the listener receive one outbound of a screen share: resumes the inbound it already has,
    /// or creates one, which starts once the listener reports it ready.
    /// </summary>
    /// <returns>Null when the SFU failed.</returns>
    private async Task<Inbound?> WatchScreenOutboundAsync(int listenerUserId, Outbound outbound)
    {
        var key = (listenerUserId, outbound.Id);

        if (_inbounds.TryGetValue(key, out var inbound))
        {
            if (_screenWatchers.Contains(key))
                return inbound;

            var resumeResult = _readyInbounds.Contains(inbound.Id)
                ? await Gateway.ResumeInbound(Model.Id, inbound.Id)
                : SFUInboundCreateResult.NoError;
            if (resumeResult != SFUInboundCreateResult.NoError)
            {
                _logger.LogError("Peer with ID {listenerUserId} failed to resume inbound of screen outbound {outboundId}. SFU service return error: {error}",
                    listenerUserId, outbound.Id, resumeResult);
                return null;
            }
        }
        else
        {
            inbound = await CreateInboundAsync(listenerUserId, outbound);
            if (inbound == null)
                return null;

            _inbounds[key] = inbound;
        }

        _screenWatchers.Add(key);
        return inbound;
    }

    /// <returns>The paused inbound, or null when the listener did not receive the outbound.</returns>
    private async Task<(SFUInboundCreateResult result, Inbound? inbound)> UnwatchScreenOutboundAsync(
        int listenerUserId, Outbound? outbound)
    {
        if (outbound == null || !_screenWatchers.Remove((listenerUserId, outbound.Id)))
            return (SFUInboundCreateResult.NoError, null);

        if (!_inbounds.TryGetValue((listenerUserId, outbound.Id), out var watched))
            return (SFUInboundCreateResult.NoError, null);

        var result = await Gateway.PauseInbound(Model.Id, watched.Id);
        return result == SFUInboundCreateResult.NoError ? (result, watched) : (result, null);
    }

    public Task<ResumeInboundsResult> ResumeInboundsAsync(int listenerUserId, IReadOnlyCollection<string> inboundIds) =>
        LockedAsync(async () =>
    {
        if (!EnsureReady("resume inbounds", listenerUserId))
            return ResumeInboundsResult.SFUNotActive;

        if (!_peers.ContainsKey(listenerUserId))
        {
            _logger.LogError("Peer with ID {listenerUserId} failed to resume inbounds. Peer not exists.", listenerUserId);
            return ResumeInboundsResult.PeerNotExists;
        }

        var requested = inboundIds.ToHashSet();
        var owned = _inbounds
            .Where(pair => pair.Key.ListenerUserId == listenerUserId && requested.Contains(pair.Value.Id))
            .ToList();

        var result = ResumeInboundsResult.NoError;
        foreach (var (key, inbound) in owned)
        {
            if (_readyInbounds.Contains(inbound.Id))
                continue;

            // An unwatched screen only waits to be watched, which resumes it then
            var isScreen = _screenOutbounds.Values.Concat(_screenAudioOutbounds.Values)
                .Any(outbound => outbound.Id == key.OutboundId);
            if (isScreen && !_screenWatchers.Contains(key))
            {
                _readyInbounds.Add(inbound.Id);
                continue;
            }

            // Marked only once resumed: a call that threw would otherwise make the retry of the
            // client skip the inbound, which then stays paused for good
            var resumeResult = await Gateway.ResumeInbound(Model.Id, inbound.Id);
            if (resumeResult == SFUInboundCreateResult.NoError)
            {
                _readyInbounds.Add(inbound.Id);
                continue;
            }

            _logger.LogError("Peer with ID {listenerUserId} failed to resume inbound {inboundId}. SFU service return error: {error}",
                listenerUserId, inbound.Id, resumeResult);
            result = ResumeInboundsResult.SFUError;
        }

        return result;
    }, timedOut: ResumeInboundsResult.TimedOut);

    private async Task PauseScreenWatchersAsync(string outboundId)
    {
        var watchers = _screenWatchers.Where(key => key.OutboundId == outboundId).ToList();

        foreach (var key in watchers)
            _screenWatchers.Remove(key);

        try
        {
            await Task.WhenAll(watchers
                .Where(key => _inbounds.ContainsKey(key))
                .Select(key => Gateway.PauseInbound(Model.Id, _inbounds[key].Id)));
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to pause watchers of outbound {outboundId}", outboundId);
        }
    }

    private Task ConsumeExistingOutboundsAsync(int listenerUserId) =>
        ConsumeAllAsync(_voiceOutbounds.Values
            .Where(outbound => outbound.UserId != listenerUserId)
            .Select(outbound => (listenerUserId, outbound)));

    /// <summary>
    /// Creates the inbounds on the SFU in parallel, then records them one by one:
    /// the dictionaries are not thread safe, so only the SFU calls run concurrently.
    /// </summary>
    private async Task ConsumeAllAsync(IEnumerable<(int ListenerUserId, Outbound Outbound)> routes)
    {
        var missing = routes.Where(route => !_inbounds.ContainsKey((route.ListenerUserId, route.Outbound.Id))).ToList();
        if (missing.Count == 0)
            return;

        var inbounds = await Task.WhenAll(missing.Select(route => CreateInboundAsync(route.ListenerUserId, route.Outbound)));

        for (var i = 0; i < missing.Count; i++)
        {
            var (listenerUserId, outbound) = missing[i];
            if (inbounds[i] is not { } inbound)
                continue;

            _inbounds[(listenerUserId, outbound.Id)] = inbound;
            await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStartedVoiceShareEvent(Model.Id,
                outbound.UserId, listenerUserId, inbound,
                _peers.TryGetValue(listenerUserId, out var listener) ? listener.ConnectionId : null));
        }
    }

    /// <returns>Null if the inbound could not be created; one failed listener does not fail the others.</returns>
    private async Task<Inbound?> CreateInboundAsync(int listenerUserId, Outbound outbound)
    {
        try
        {
            var (result, inbound) = await Gateway.AddInbound(Model.Id, listenerUserId, outbound,
                Model.Media.ForInbound(outbound.MediaType));
            if (result == SFUInboundCreateResult.NoError && inbound != null)
                return inbound;

            _logger.LogError("Failed to create inbound of outbound {outboundId} for user with ID {userId}. " +
                             "SFU service return error: {error}", outbound.Id, listenerUserId, result);
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to create inbound of outbound {outboundId} for user with ID {userId}",
                outbound.Id, listenerUserId);
        }

        return null;
    }

    /// <summary>Removes everything the peer consumes and publishes.</summary>
    private async Task ReleasePeerMediaAsync(int userId)
    {
        try
        {
            var consumed = _inbounds.Where(pair => pair.Key.ListenerUserId == userId).ToList();
            foreach (var (key, inbound) in consumed)
            {
                _inbounds.Remove(key);
                _screenWatchers.Remove(key);
                _readyInbounds.Remove(inbound.Id);
            }

            await Task.WhenAll(consumed.Select(pair => Gateway.RemoveInbound(Model.Id, userId, pair.Value.Id)));

            if (_peers.TryGetValue(userId, out var speaker))
                speaker.SetMicrophoneMuted(false);

            if (_voiceOutbounds.Remove(userId, out var outbound))
            {
                ForgetInbounds(outbound.Id);
                await Gateway.RemoveOutbound(Model.Id, userId, outbound.Id);
                await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStoppedVoiceShareEvent(Model.Id, userId));
            }

            if (_screenOutbounds.Remove(userId, out var screenOutbound))
            {
                speaker?.SetScreenShareActive(false);
                ForgetInbounds(screenOutbound.Id);
                await Gateway.RemoveOutbound(Model.Id, userId, screenOutbound.Id);
                await _coordinatorEventBus.PublishAsync(new HybridSessionPeerStoppedScreenShareEvent(Model.Id, userId));
            }

            if (_screenAudioOutbounds.Remove(userId, out var screenAudioOutbound))
            {
                ForgetInbounds(screenAudioOutbound.Id);
                await Gateway.RemoveOutbound(Model.Id, userId, screenAudioOutbound.Id);
            }
        }
        catch (Exception e)
        {
            _logger.LogError(e, "Failed to release media of user with ID {userId}", userId);
        }
    }

    private void ForgetInbounds(string outboundId)
    {
        foreach (var key in _inbounds.Keys.Where(key => key.OutboundId == outboundId).ToList())
        {
            if (_inbounds.Remove(key, out var inbound))
                _readyInbounds.Remove(inbound.Id);
        }

        _screenWatchers.RemoveWhere(key => key.OutboundId == outboundId);
    }

    private void ResetMediaState()
    {
        _voiceOutbounds.Clear();
        _screenOutbounds.Clear();
        _screenAudioOutbounds.Clear();
        _inbounds.Clear();
        _screenWatchers.Clear();
        _readyInbounds.Clear();
    }

    private static int? InitiatorOrNull(int initiatorId, int userId) => initiatorId == userId ? null : initiatorId;
}
