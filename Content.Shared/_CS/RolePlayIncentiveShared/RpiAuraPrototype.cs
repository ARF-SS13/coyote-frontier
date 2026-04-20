using Robust.Shared.Prototypes;

namespace Content.Shared._CS.RolePlayIncentiveShared;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype()]
public sealed partial class RpiAuraPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The multiplier applied by this aura at full effect.
    /// </summary>
    [DataField("multiplier", required: true), AlwaysPushInheritance]
    public float Multiplier = 1.0f;

    /// <summary>
    /// The maximum distance this aura has effect, in meters.
    /// </summary>
    [DataField("maxDistance", required: true), AlwaysPushInheritance]
    public float MaxDistance = 10.0f;

    /// <summary>
    /// Time it takes, in minutes, to reach full effect when entering the aura.
    /// </summary>
    [DataField("minutesTillFullEffect", required: true), AlwaysPushInheritance]
    public int MinutesTillFullEffect = 30;

    /// <summary>
    /// Delay, in minutes, before decay starts after leaving the aura.
    /// </summary>
    [DataField("minutesUntilDecayDelay", required: true), AlwaysPushInheritance]
    public int MinutesUntilDecayDelay = 20;

    /// <summary>
    /// Time it takes, in minutes, to decay to zero effect after the decay delay.
    /// </summary>
    [DataField("minutesUntilDecayToZero", required: true), AlwaysPushInheritance]
    public int MinutesUntilDecayToZero = 60;
}
