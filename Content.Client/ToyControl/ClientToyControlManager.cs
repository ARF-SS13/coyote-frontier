using Content.Shared._CS.ToyControl;
using Robust.Shared.Network;

namespace Content.Client._CS.ToyControl;

public sealed class ClientToyControlManager : IClientToyControlManager
{
    [Dependency] private readonly IClientNetManager _net = default!;

    private bool _initialized;

    public event Action<ToyControlSessionStarted>? SessionStarted;
    public event Action<ToyControlSessionDenied>? SessionDenied;
    public event Action<ToyControlCommand>? CommandReceived;
    public event Action<ToyControlSessionClosed>? SessionClosed;

    public void Initialize()
    {
        if (_initialized)
            return;

        _initialized = true;

        _net.RegisterNetMessage<MsgToyControlSessionRequest>();
        _net.RegisterNetMessage<MsgToyControlSessionStarted>(OnSessionStarted);
        _net.RegisterNetMessage<MsgToyControlSessionDenied>(OnSessionDenied);
        _net.RegisterNetMessage<MsgToyControlCommand>(OnCommandReceived);
        _net.RegisterNetMessage<MsgToyControlSessionClosed>(OnSessionClosed);
    }

    public void RequestSession(NetUserId targetUserId)
    {
        var msg = new MsgToyControlSessionRequest
        {
            Request = new ToyControlSessionRequest(targetUserId)
        };

        _net.ClientSendMessage(msg);
    }

    public void SendCommand(ToyControlCommand command)
    {
        var msg = new MsgToyControlCommand { Command = command };
        _net.ClientSendMessage(msg);
    }

    public void CloseSession(int sessionId)
    {
        var msg = new MsgToyControlSessionClosed
        {
            Closed = new ToyControlSessionClosed(sessionId, ToyControlSessionCloseReason.ControllerClosed)
        };

        _net.ClientSendMessage(msg);
    }

    private void OnSessionStarted(MsgToyControlSessionStarted msg)
    {
        SessionStarted?.Invoke(msg.Session);
    }

    private void OnSessionDenied(MsgToyControlSessionDenied msg)
    {
        SessionDenied?.Invoke(msg.Denied);
    }

    private void OnCommandReceived(MsgToyControlCommand msg)
    {
        CommandReceived?.Invoke(msg.Command);
    }

    private void OnSessionClosed(MsgToyControlSessionClosed msg)
    {
        SessionClosed?.Invoke(msg.Closed);
    }
}
