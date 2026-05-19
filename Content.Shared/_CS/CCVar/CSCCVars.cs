using Robust.Shared.Configuration;

namespace Content.Shared._CS.CCVar;

/// <summary>
/// Contains CVars used by Coyote.
/// </summary>
[CVarDefs]
public sealed class CSCVars
{
    /// <summary>
    /// Max number of items on a belt before we destroy it/warn admins
    /// </summary>
    public static readonly CVarDef<int> ConveyorMaxItemCount =
    CVarDef.Create("conveyor.max_item_count", 200, CVar.SERVERONLY);
    /// <summary>
    /// Max number of items on a belt before we destroy it/warn admins
    /// </summary>
    public static readonly CVarDef<float> ConveyorCleanupIntervalSeconds =
    CVarDef.Create("conveyor.cleanup_interval_seconds", 51f, CVar.SERVERONLY);

    /// <summary>
    /// Websocket address of the local Intiface Central instance.
    /// </summary>
    public static readonly CVarDef<string> IntifaceAddress =
        CVarDef.Create("toy_control.intiface_address", "ws://127.0.0.1:12345", CVar.CLIENT | CVar.ARCHIVE);
}
