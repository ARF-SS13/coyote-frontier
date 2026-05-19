using Content.Shared._CS.ToyControl;
using Robust.Shared.Network;

namespace Content.Client._CS.ToyControl;

public interface IClientToyControlManager
{
    void Initialize();
    void RequestSession(NetUserId targetUserId);
    void SendCommand(ToyControlCommand command);
    void CloseSession(int sessionId);

    event Action<ToyControlSessionStarted>? SessionStarted;
    event Action<ToyControlSessionDenied>? SessionDenied;
    event Action<ToyControlCommand>? CommandReceived;
    event Action<ToyControlSessionClosed>? SessionClosed;
}
