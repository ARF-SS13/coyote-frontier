using Robust.Shared.Player;

namespace Content.Server._CS.ToyControl;

public interface IServerToyControlManager
{
    void Initialize();
    void OnClientDisconnected(ICommonSession session);
}
