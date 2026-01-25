using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote.RedeemableStuff;

/// <summary>
/// This is used to explicitly mark an entity as unredeemable.
/// Can
/// </summary>
[RegisterComponent]
public sealed partial class UnRedeemableComponent : Component
{
    /// <summary>
    /// Does this unredeemable component block all redeeming, or only certain types of currency?
    /// If null or empty, blocks all redeeming.
    /// </summary>
    [DataField]
    public HashSet<ProtoId<CurrencyPrototype>> BlockedCurrencies = new();

    /// <summary>
    /// If true, it will attempt to spread from itself to anything derived from it
    /// Like if you craft something from an unredeemable item, the result will also be unredeemable.
    /// </summary>
    [DataField("sticky")]
    public bool Sticky = false;
}
