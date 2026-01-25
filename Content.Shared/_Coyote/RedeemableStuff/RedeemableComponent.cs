using Content.Shared.Store;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote.RedeemableStuff;

/// <summary>
/// This is for making it so an entity can be redeemed for something.
/// Like nfsd stuff being redeemed on the Den for like, den bullion
/// Or guns! Trade them in for whatever!
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class RedeemableComponent : Component
{
    /// <summary>
    /// How much of which kinds of currency this item can be redeemed for.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public Dictionary<ProtoId<CurrencyPrototype>, int> TurnInValues = new();

    /// <summary>
    /// Easy presets for common turn-in values.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public ProtoId<RedeemablePresetPrototype>? Preset;
}
