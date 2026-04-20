using Robust.Shared.Prototypes;

namespace Content.Shared._CS.RolePlayIncentiveShared;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype("rpiContinuousProxyAction")]
public sealed partial class RpiContinuousProxyActionPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// How long does it take to reach max RPI bonus?
    /// </summary>
    [DataField("minutesToMaxBonus", required: true)]
    public float MinutesToMaxBonus = 10f;

    /// <summary>
    /// The max multiplier you can get from this action.
    /// </summary>
    [DataField("maxMultiplier", required: true)]
    public float MaxMultiplier = 2f;

    /// <summary>
    /// Distance you need to be to the target entity for this to work.
    /// </summary>
    [DataField("maxDistance", required: true)]
    public float MaxDistance = 10f;

    /// <summary>
    /// Distance where the bonus gets a bonus (so you get more bonus for being closer)
    /// </summary>
    [DataField("optimalDistance")]
    public float OptimalDistance = 5f;

    /// <summary>
    /// The bonus bonus multiplier you get for being within optimal distance.
    /// </summary>
    [DataField("optimalDistanceBonusMultiplier")]
    public float OptimalDistanceBonusMultiplier = 2f;

    /// <summary>
    /// Stub localization key for the readouts in the examine thingy
    /// </summary>
    [DataField("examineTextKey")]
    public string ExamineTextKey = string.Empty;

    [DataField("targetMustHaveTheseComponents", required: true)]
    public ComponentRegistry TargetMustHaveTheseComponents = new();

    [DataField("userMustHaveTheseComponents")]
    public ComponentRegistry UserMustHaveTheseComponents = new();

    [DataField("targetMustNotHaveTheseComponents")]
    public ComponentRegistry TargetMustNotHaveTheseComponents = new();

    [DataField("userMustNotHaveTheseComponents")]
    public ComponentRegistry UserMustNotHaveTheseComponents = new();

    [DataField("isNonPlayerComponentQuery")]
    public bool IsNonPlayerComponentQuery = false;

    [DataField("maxTargets")]
    public int MaxTargets = 1;
}
