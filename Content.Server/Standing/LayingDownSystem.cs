using Content.Shared.Standing;
using Content.Shared.CCVar;
using Content.Shared.Floofstation;
using Robust.Shared.Configuration;

namespace Content.Server.Standing;

// Largerly rewritten by EE. Take care when merging changes.
public sealed class LayingDownSystem : SharedLayingDownSystem
{
    [Dependency] private readonly INetConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<CheckAutoGetUpEvent>(OnCheckAutoGetUp);
    }

    private void OnCheckAutoGetUp(CheckAutoGetUpEvent ev, EntitySessionEventArgs args)
    {
        var uid = GetEntity(ev.User);

        if (!TryComp(uid, out LayingDownComponent? layingDown))
            return;

        layingDown.AutoGetUp = _cfg.GetClientCVar(args.SenderSession.Channel, FloofCCVars.AutoGetUp);
        Dirty(uid, layingDown);
    }
}
