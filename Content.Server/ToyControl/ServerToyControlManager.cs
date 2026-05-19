using Content.Server.Consent;
using Content.Shared.Consent;
using Content.Shared._CS.ToyControl;
using Robust.Server.Player;
using Robust.Shared.Network;
using Robust.Shared.Player;
using System.Linq;

namespace Content.Server._CS.ToyControl;

public sealed class ServerToyControlManager : IServerToyControlManager
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly IServerNetManager _net = default!;
    [Dependency] private readonly IEntityManager _entityManager = default!;

    private readonly Dictionary<int, ToyControlSessionData> _sessions = new();
    private readonly Dictionary<NetUserId, HashSet<int>> _sessionsByUser = new();

    private int _nextSessionId = 1;

    public void Initialize()
    {
        _net.RegisterNetMessage<MsgToyControlSessionRequest>(HandleSessionRequest);
        _net.RegisterNetMessage<MsgToyControlCommand>(HandleCommand);
        _net.RegisterNetMessage<MsgToyControlSessionClosed>(HandleClose);

        _net.RegisterNetMessage<MsgToyControlSessionStarted>();
        _net.RegisterNetMessage<MsgToyControlSessionDenied>();
    }

    public void OnClientDisconnected(ICommonSession session)
    {
        if (!_sessionsByUser.TryGetValue(session.UserId, out var ids))
            return;

        var toClose = ids.ToArray();
        foreach (var sessionId in toClose)
        {
            if (!_sessions.TryGetValue(sessionId, out var data))
                continue;

            var reason = data.ControllerUserId == session.UserId
                ? ToyControlSessionCloseReason.ControllerDisconnected
                : ToyControlSessionCloseReason.TargetDisconnected;
            CloseSession(sessionId, reason);
        }
    }

    private void HandleSessionRequest(MsgToyControlSessionRequest msg)
    {
        var sourceSession = _player.GetSessionByChannel(msg.MsgChannel);
        var targetUserId = msg.Request.TargetUserId;

        if (sourceSession.UserId == targetUserId)
        {
            SendDenied(sourceSession.Channel, targetUserId, ToyControlSessionDeniedReason.SelfControlNotAllowed);
            return;
        }

        if (!_player.TryGetSessionById(targetUserId, out var targetSession))
        {
            SendDenied(sourceSession.Channel, targetUserId, ToyControlSessionDeniedReason.TargetNotFound);
            return;
        }

        if (!HasToyControlConsent(targetSession))
        {
            SendDenied(sourceSession.Channel, targetUserId, ToyControlSessionDeniedReason.TargetOptedOut);
            return;
        }

        if (HasActiveSession(targetUserId))
        {
            SendDenied(sourceSession.Channel, targetUserId, ToyControlSessionDeniedReason.TargetAlreadyControlled);
            return;
        }

        var sessionId = _nextSessionId++;
        _sessions[sessionId] = new ToyControlSessionData(sourceSession.UserId, targetUserId);
        IndexSession(sourceSession.UserId, sessionId);
        IndexSession(targetUserId, sessionId);

        var toController = new MsgToyControlSessionStarted
        {
            Session = new ToyControlSessionStarted(sessionId, sourceSession.UserId, targetUserId, true)
        };

        var toTarget = new MsgToyControlSessionStarted
        {
            Session = new ToyControlSessionStarted(sessionId, sourceSession.UserId, targetUserId, false)
        };

        _net.ServerSendMessage(toController, sourceSession.Channel);
        _net.ServerSendMessage(toTarget, targetSession.Channel);
    }

    private void HandleCommand(MsgToyControlCommand msg)
    {
        var sourceSession = _player.GetSessionByChannel(msg.MsgChannel);
        var cmd = msg.Command;

        if (!_sessions.TryGetValue(cmd.SessionId, out var data))
            return;

        if (data.ControllerUserId != sourceSession.UserId)
        {
            CloseSession(cmd.SessionId, ToyControlSessionCloseReason.Unauthorized);
            return;
        }

        if (!_player.TryGetSessionById(data.TargetUserId, out var targetSession))
        {
            CloseSession(cmd.SessionId, ToyControlSessionCloseReason.TargetDisconnected);
            return;
        }

        if (!HasToyControlConsent(targetSession))
        {
            CloseSession(cmd.SessionId, ToyControlSessionCloseReason.TargetOptedOut);
            return;
        }

        // Clamp before relay as a server-side safety guard.
        ClampActuator(ref cmd.Vibrate);
        ClampActuator(ref cmd.Oscillate);
        ClampActuator(ref cmd.Inflate);
        ClampActuator(ref cmd.Constrict);
        ClampActuator(ref cmd.LinearPosition);
        ClampActuator(ref cmd.RotateSpeed);
        cmd.LinearDurationMs = Math.Clamp(cmd.LinearDurationMs, 50, 10000);
        cmd.DurationSeconds = Math.Clamp(cmd.DurationSeconds, 0f, 30f);

        _net.ServerSendMessage(msg, targetSession.Channel);
    }

    private void HandleClose(MsgToyControlSessionClosed msg)
    {
        var sourceSession = _player.GetSessionByChannel(msg.MsgChannel);

        if (!_sessions.TryGetValue(msg.Closed.SessionId, out var data))
            return;

        if (data.ControllerUserId != sourceSession.UserId && data.TargetUserId != sourceSession.UserId)
            return;

        var reason = data.ControllerUserId == sourceSession.UserId
            ? ToyControlSessionCloseReason.ControllerClosed
            : ToyControlSessionCloseReason.TargetClosed;

        CloseSession(msg.Closed.SessionId, reason);
    }

    private void CloseSession(int sessionId, ToyControlSessionCloseReason reason)
    {
        if (!_sessions.Remove(sessionId, out var data))
            return;

        RemoveSessionIndex(data.ControllerUserId, sessionId);
        RemoveSessionIndex(data.TargetUserId, sessionId);

        var closed = new MsgToyControlSessionClosed
        {
            Closed = new ToyControlSessionClosed(sessionId, reason)
        };

        if (_player.TryGetSessionById(data.ControllerUserId, out var controller))
            _net.ServerSendMessage(closed, controller.Channel);

        if (_player.TryGetSessionById(data.TargetUserId, out var target))
            _net.ServerSendMessage(closed, target.Channel);
    }

    private bool HasActiveSession(NetUserId userId)
    {
        return _sessionsByUser.TryGetValue(userId, out var ids) && ids.Count > 0;
    }

    private bool HasToyControlConsent(ICommonSession session)
    {
        var entity = session.AttachedEntity;
        if (entity == null)
            return false;

        var consentSys = _entityManager.System<ConsentSystem>();
        return consentSys.HasConsent(entity.Value, "RemoteToyControl");
    }

    private void SendDenied(INetChannel channel, NetUserId targetUserId, ToyControlSessionDeniedReason reason)
    {
        var msg = new MsgToyControlSessionDenied
        {
            Denied = new ToyControlSessionDenied(targetUserId, reason)
        };
        _net.ServerSendMessage(msg, channel);
    }

    private void IndexSession(NetUserId userId, int sessionId)
    {
        if (!_sessionsByUser.TryGetValue(userId, out var ids))
        {
            ids = new HashSet<int>();
            _sessionsByUser[userId] = ids;
        }

        ids.Add(sessionId);
    }

    private void RemoveSessionIndex(NetUserId userId, int sessionId)
    {
        if (!_sessionsByUser.TryGetValue(userId, out var ids))
            return;

        ids.Remove(sessionId);

        if (ids.Count == 0)
            _sessionsByUser.Remove(userId);
    }

    /// <summary>
    /// Clamps a nullable float actuator value to [0, 1]. NaN values (meaning "don't send") are left untouched.
    /// </summary>
    private static void ClampActuator(ref float value)
    {
        if (!float.IsNaN(value))
            value = Math.Clamp(value, 0f, 1f);
    }

    private readonly record struct ToyControlSessionData(NetUserId ControllerUserId, NetUserId TargetUserId);
}
