using Robust.Shared.Prototypes;

namespace Content.Shared._CS.Needs;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype("NeedSlowdown")]
public sealed partial class NeedSlowdownPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The amount of slowdown to apply
    /// </summary>
    [DataField("speedModifier")]
    public float SpeedModifier = 1.0f;

    /// <summary>
    /// The minimum minutes between when one slowdown ends and another can start
    /// </summary>
    [DataField("minMinutesBetweenSlowdowns")]
    public float MinMinutesBetweenSlowdowns = 5.0f;

    /// <summary>
    /// The amount of time in seconds that the slowdown will last
    /// </summary>
    [DataField("durationSeconds")]
    public float DurationSeconds = 60.0f;

    /// <summary>
    /// The chance in percent that this slowdown will be applied when the need reaches the threshold
    /// This will be checked every second while the need is at or below the threshold
    /// if its set to 2%, then here is the math:
    /// at 1 second: 2% chance
    /// at 2 seconds: 3.96% chance
    /// at 3 seconds: 5.88% chance
    /// at 30 seconds: 45.76% chance
    /// at 60 seconds: 70.12% chance
    /// </summary>
    [DataField("chancePercent")]
    public float ChancePercent = 100.0f;

    /// <summary>
    /// The localization string to use for the slowdown start message
    /// </summary>
    [DataField("startMessage")]
    public string StartMessage = "need-slowdown-default-start";

    /// <summary>
    /// The localization string to use for the slowdown end message
    /// </summary>
    [DataField("endMessage")]
    public string EndMessage = "need-slowdown-default-end";
}
