using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote.RedeemableStuff;

/// <summary>
/// This is a prototype for easy presets for redeemable values.
/// </summary>
[Prototype("redeemablePreset")]
public sealed partial class RedeemablePresetPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; } = default!;

    /// <summary>
    /// Fucs you get for turning this in.
    /// </summary>
    [DataField]
    public int FucValue = 0;

    /// <summary>
    /// Den Bullion you get for turning this in.
    /// </summary>
    [DataField]
    public int DenBullionValue = 0;
}
