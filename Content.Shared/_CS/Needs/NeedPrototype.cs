using Content.Shared.Alert;
using Robust.Shared.Prototypes;

namespace Content.Shared._CS.Needs;

/// <summary>
/// Defines a need that an entity can have, such as hunger, thirst, or whatever else we come up with.
/// Designed to be SO EASY that even FENNY can use it!!!
/// </summary>
[Prototype("need")]
public sealed partial class NeedPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    #region General Info
    /// <summary>
    /// The name of the need, for example "Hunger" or "Thirst".
    /// </summary>
    [DataField("needName")]
    public string NeedName = "Busty Vixens";

    /// <summary>
    /// The NeedType associated with this need.
    /// </summary>
    [DataField("needKind")]
    public string NeedKind = "Hunger";

    /// <summary>
    /// The color associated with this need, for use in HUDs and such.
    /// Colors MUST be something definied by Color.FromName, or it will default to white.
    /// Examples: "Red", "Blue", "Green", "Cyan", "Magenta", etc.
    /// Hex codes are NOT supported.
    /// </summary>
    [DataField("color")]
    public string NeedColor = "White";

    /// <summary>
    /// I just realized that making a minor tweak to the prototype would require
    /// backing up a truckload of data and dumping them out onto the yml's porch. not ideal
    /// So this is a quick scalar to adjust how quickly this need decays compared to others
    /// Default is 1.0, which means it decays at the normal rate.
    /// 0.5 means it decays half as fast, 2.0 means it decays twice as fast, etc.
    /// </summary>
    [DataField("decayScalar")]
    public float TimeScalar = 1.0f;

    /// <summary>
    /// The Maximum value this need can have.
    /// </summary>
    [DataField("maxValue")]
    public float MaxValue = 600f;

    /// <summary>
    /// The Minimum value this need can have.
    /// </summary>
    [DataField("minValue")]
    public float MinValue = 0f;

    /// <summary>
    /// The time, in minutes, it takes for this need to decay from full to empty
    /// Assuming no other changes.
    /// </summary>
    [DataField("minutesFromMaxToMin")]
    public float MinutesFromMaxToMin = 60f;

    /// <summary>
    /// When you spawn in, this is how many minutes worth of decay you start with.
    /// </summary>
    [DataField("startingMinutesWorthOfDecay")]
    public float StartingMinutesWorthOfDecay = 0f;

    /// <summary>
    /// The Alert Category associated with this need, if any.
    /// </summary>
    [DataField]
    public ProtoId<AlertCategoryPrototype> AlertCategory = default!;

    /// <summary>
    /// Rate it updates in seconds.
    /// </summary>
    [DataField("secondsPerUpdate")]
    public float UpdateRateSeconds = 1f;
    #endregion

    #region Threshold - ExtraSatisfied
    /// <summary>
    /// Time in minutes it takes to decay from full to ExtraSatisfied.
    /// </summary>
    [DataField("extraSatisfiedMinutesFromFull")]
    public float ExtraSatisfiedMinutesFromFull = 10f;

    /// <summary>
    /// The Slowdown multiplier applied when the need is ExtraSatisfied.
    /// </summary>
    [DataField("extraSatisfiedSpeedMult")]
    public float ExtraSatisfiedSlowdown = 1.0f;

    /// <summary>
    /// The Bonus Roleplay Incentive multiplier applied when the need is ExtraSatisfied.
    /// </summary>
    [DataField("extraSatisfiedMultRPI")]
    public float ExtraSatisfiedRoleplayIncentive = 1.0f;

    /// <summary>
    /// The Alert associated with this need when it is ExtraSatisfied, if any.
    /// </summary>
    [DataField("extraSatisfiedAlert")]
    public ProtoId<AlertPrototype>? ExtraSatisfiedAlert = null;

    /// <summary>
    /// The Hud icon associated with this need when it is ExtraSatisfied, if any.
    /// </summary>
    [DataField("extraSatisfiedIcon")]
    public string? ExtraSatisfiedIcon = null;

    /// <summary>
    /// The Random slowdown prototype to use when this need reaches the ExtraSatisfied threshold, if any.
    /// </summary>
    [DataField("extraSatisfiedDebuffSlowdown")]
    public ProtoId<NeedSlowdownPrototype>? ExtraSatisfiedDebuffSlowdown = null;
    #endregion

    #region Threshold - Satisfied
    /// <summary>
    /// Time in minutes it takes to decay from full to Satisfied.
    /// </summary>
    [DataField("satisfiedMinutesFromFull")]
    public float SatisfiedMinutesFromFull = 20f;

    /// <summary>
    /// The Slowdown multiplier applied when the need is Satisfied.
    /// </summary>
    [DataField("satisfiedSpeedMult")]
    public float SatisfiedSlowdown = 1.0f;

    /// <summary>
    /// The Bonus Roleplay Incentive multiplier applied when the need is Satisfied.
    /// </summary>
    [DataField("satisfiedMultRPI")]
    public float SatisfiedRoleplayIncentive = 1.0f;

    /// <summary>
    /// The Alert associated with this need when it is Satisfied, if any.
    /// </summary>
    [DataField("satisfiedAlert")]
    public ProtoId<AlertPrototype>? SatisfiedAlert = null;

    /// <summary>
    /// The Hud icon associated with this need when it is Satisfied, if any.
    /// </summary>
    [DataField("satisfiedIcon")]
    public string? SatisfiedIcon = null;

    /// <summary>
    /// The Random slowdown prototype to use when this need reaches the Satisfied threshold, if any.
    /// </summary>
    [DataField("satisfiedDebuffSlowdown")]
    public ProtoId<NeedSlowdownPrototype>? SatisfiedDebuffSlowdown = null;
    #endregion

    #region Threshold - Neutral
    /// <summary>
    /// Time in minutes it takes to decay from full to Neutral.
    /// </summary>
    [DataField("neutralMinutesFromFull")]
    public float NeutralMinutesFromFull = 40f;

    /// <summary>
    /// The Slowdown multiplier applied when the need is Neutral.
    /// </summary>
    [DataField("neutralSpeedMult")]
    public float NeutralSlowdown = 1.0f;

    /// <summary>
    /// The Bonus Roleplay Incentive multiplier applied when the need is Neutral.
    /// </summary>
    [DataField("neutralMultRPI")]
    public float NeutralRoleplayIncentive = 1.0f;

    /// <summary>
    /// The Alert associated with this need when it is Neutral, if any.
    /// </summary>
    [DataField("neutralAlert")]
    public ProtoId<AlertPrototype>? NeutralAlert = null;

    /// <summary>
    /// The Hud icon associated with this need when it is Neutral, if any.
    /// </summary>
    [DataField("neutralIcon")]
    public string? NeutralIcon = null;
    #endregion

    #region Threshold - Low
    /// <summary>
    /// Time in minutes it takes to decay from full to Low.
    /// </summary>
    [DataField("lowMinutesFromFull")]
    public float LowMinutesFromFull = 50f;

    /// <summary>
    /// The Slowdown multiplier applied when the need is Low.
    /// </summary>
    [DataField("lowSpeedMult")]
    public float LowSlowdown = 0.9f;

    /// <summary>
    /// The Bonus Roleplay Incentive multiplier applied when the need is Low.
    /// </summary>
    [DataField("lowMultRPI")]
    public float LowRoleplayIncentive = 0.9f;

    /// <summary>
    /// The Alert associated with this need when it is Low, if any.
    /// </summary>
    [DataField("lowAlert")]
    public ProtoId<AlertPrototype>? LowAlert = null;

    /// <summary>
    /// The Hud icon associated with this need when it is Low, if any.
    /// </summary>
    [DataField("lowIcon")]
    public string? LowIcon = null;

    /// <summary>
    /// The Random slowdown prototype to use when this need reaches the Low threshold, if any.
    /// </summary>
    [DataField("lowDebuffSlowdown")]
    public ProtoId<NeedSlowdownPrototype>? LowDebuffSlowdown = null;
    #endregion

    #region Threshold - Critical
    /// <summary>
    /// Time in minutes it takes to decay from full to Critical.
    /// Will actually be ignored, and set to the minimum value of the need
    /// </summary>
    [DataField("criticalMinutesFromFull")]
    public float CriticalMinutesFromFull = 55f;

    /// <summary>
    /// The Slowdown multiplier applied when the need is Critical.
    /// </summary>
    [DataField("criticalSpeedMult")]
    public float CriticalSlowdown = 0.75f;

    /// <summary>
    /// The Bonus Roleplay Incentive multiplier applied when the need is Critical.
    /// </summary>
    [DataField("criticalMultRPI")]
    public float CriticalRoleplayIncentive = 0.75f;

    /// <summary>
    /// The Alert associated with this need when it is Critical, if any.
    /// </summary>
    [DataField("criticalAlert")]
    public ProtoId<AlertPrototype>? CriticalAlert = null;

    /// <summary>
    /// The Hud icon associated with this need when it is Critical, if any.
    /// </summary>
    [DataField("criticalIcon")]
    public string? CriticalIcon = null;

    /// <summary>
    /// The Random slowdown prototype to use when this need reaches the Critical threshold, if any.
    /// </summary>
    [DataField("criticalDebuffSlowdown")]
    public ProtoId<NeedSlowdownPrototype>? CriticalDebuffSlowdown = null;
    #endregion

}
