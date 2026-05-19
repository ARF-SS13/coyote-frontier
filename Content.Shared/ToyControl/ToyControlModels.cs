using Robust.Shared.Network;
using Robust.Shared.Serialization;

namespace Content.Shared._CS.ToyControl;

[Serializable, NetSerializable]
public enum ToyControlSessionCloseReason : byte
{
    ControllerClosed,
    TargetClosed,
    ControllerDisconnected,
    TargetDisconnected,
    TargetOptedOut,
    SessionNotFound,
    Unauthorized,
}

[Serializable, NetSerializable]
public enum ToyControlSessionDeniedReason : byte
{
    TargetNotFound,
    TargetOptedOut,
    TargetAlreadyControlled,
    SelfControlNotAllowed,
}

[Serializable, NetSerializable]
public sealed class ToyControlSessionRequest
{
    public NetUserId TargetUserId;

    public ToyControlSessionRequest()
    {
    }

    public ToyControlSessionRequest(NetUserId targetUserId)
    {
        TargetUserId = targetUserId;
    }
}

[Serializable, NetSerializable]
public sealed class ToyControlSessionStarted
{
    public int SessionId;
    public NetUserId ControllerUserId;
    public NetUserId TargetUserId;
    public bool IsController;

    public ToyControlSessionStarted()
    {
    }

    public ToyControlSessionStarted(int sessionId, NetUserId controllerUserId, NetUserId targetUserId, bool isController)
    {
        SessionId = sessionId;
        ControllerUserId = controllerUserId;
        TargetUserId = targetUserId;
        IsController = isController;
    }
}

[Serializable, NetSerializable]
public sealed class ToyControlSessionDenied
{
    public NetUserId TargetUserId;
    public ToyControlSessionDeniedReason Reason;

    public ToyControlSessionDenied()
    {
    }

    public ToyControlSessionDenied(NetUserId targetUserId, ToyControlSessionDeniedReason reason)
    {
        TargetUserId = targetUserId;
        Reason = reason;
    }
}

[Serializable, NetSerializable]
public sealed class ToyControlCommand
{
    public int SessionId;

    /// <summary>
    /// Seconds after which scalar actuators (Vibrate, Oscillate, Inflate, Constrict) are stopped.
    /// 0 means no timed stop.
    /// </summary>
    public float DurationSeconds;

    // --- ScalarCmd actuators (Buttplug v3) ---
    // float.NaN means "do not send this actuator type". Valid range 0–1.

    /// <summary>Vibration intensity. NaN = don't send.</summary>
    public float Vibrate = float.NaN;

    /// <summary>Oscillation speed (non-position-based machines). NaN = don't send.</summary>
    public float Oscillate = float.NaN;

    /// <summary>Inflation level (pumps, pressure cuffs). NaN = don't send.</summary>
    public float Inflate = float.NaN;

    /// <summary>Constriction level (sleeve-type toys). NaN = don't send.</summary>
    public float Constrict = float.NaN;

    // --- LinearCmd (Buttplug v3 stroker movement) ---

    /// <summary>Target stroke position 0–1. NaN = don't send LinearCmd.</summary>
    public float LinearPosition = float.NaN;

    /// <summary>Milliseconds to reach LinearPosition. Only used when LinearPosition is set.</summary>
    public int LinearDurationMs = 500;

    // --- RotateCmd (Buttplug v3) ---

    /// <summary>Rotation speed 0–1. NaN = don't send RotateCmd.</summary>
    public float RotateSpeed = float.NaN;

    /// <summary>Rotation direction. Only used when RotateSpeed is set.</summary>
    public bool RotateClockwise = true;

    public ToyControlCommand()
    {
    }

    public ToyControlCommand(int sessionId)
    {
        SessionId = sessionId;
    }
}

[Serializable, NetSerializable]
public sealed class ToyControlSessionClosed
{
    public int SessionId;
    public ToyControlSessionCloseReason Reason;

    public ToyControlSessionClosed()
    {
    }

    public ToyControlSessionClosed(int sessionId, ToyControlSessionCloseReason reason)
    {
        SessionId = sessionId;
        Reason = reason;
    }
}
