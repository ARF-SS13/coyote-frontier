using Robust.Shared.Prototypes;

namespace Content.Shared.Radio;

[Prototype]
public sealed partial class RadioChannelPrototype : IPrototype
{
    /// <summary>
    /// Human-readable name for the channel.
    /// </summary>
    [DataField("name")]
    public LocId Name { get; private set; } = string.Empty;

    [ViewVariables(VVAccess.ReadOnly)]
    public string LocalizedName => Loc.GetString(Name);

    /// <summary>
    /// Single-character prefix to determine what channel a message should be sent to.
    /// </summary>
    [DataField("keycode")]
    public char KeyCode { get; private set; } = '\0';

    [DataField("frequency")]
    public int Frequency { get; private set; } = 0;

    [DataField("color")]
    public Color Color { get; private set; } = Color.Lime;

    [IdDataField, ViewVariables]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// If channel is long range it doesn't require telecommunication server
    /// and messages can be sent across different stations
    /// </summary>
    [DataField("longRange"), ViewVariables]
    public bool LongRange = false;

    // Frontier: radio channel frequencies
    /// <summary>
    /// If true, the frequency of the message being sent will be appended to the chat message
    /// </summary>
    [DataField, ViewVariables]
    public bool ShowFrequency = false;
    // End Frontier

    /// <summary>
    /// ImpStation port, if a channel is read-only, radio cannot be sent through it
    /// Intercomms still can
    /// Supermatter throws errors without this
    /// </summary>
    [DataField("intercomOnly")]
    public bool IntercomOnly = false;

    /// <summary>
    /// Maximum distance in meters this channel can transmit. If 0 or null, range is unlimited except by map boundaries.
    /// </summary>
    [DataField("maxRange"), ViewVariables]
    public float? MaxRange = null;

    // And now, the range stuff!
    // We have several kinds of range:
    // Optimal range: totally intelligible, crystal clear text
    // Light Degradation Gradient: text starts to degrade, but still mostly intelligible
    // Heavy Degradation Gradient: text is very degraded, barely intelligible, and smaller, sender is obscured
    // Beyond Heavy Degradation Gradient: High chance to just not hear anything at all, otherwise above

    /// <summary>
    /// If true, range degradation is used for this channel.
    /// If false, it goes across the map
    /// </summary>
    [DataField("useRangeDegradation"), ViewVariables]
    public bool UseRangeDegradation = false;

    /// <summary>
    /// Optimal range in meters. Within this range, messages are perfectly clear.
    /// if either target is on a grid, we add the diameter of the grid to the optimal range.
    /// </summary>
    [DataField("optimalRange"), ViewVariables]
    public float OptimalRange = 450f;

    /// <summary>
    /// Minimum degradation percent at the start of the degradation range.
    /// </summary>
    [DataField("degradationMinPercent"), ViewVariables]
    public float DegradationMinPercent = 0.05f;

    /// <summary>
    /// Maximum degradation percent at the end of the heavy degradation range.
    /// </summary>
    [DataField("degradationMaxPercent"), ViewVariables]
    public float DegradationMaxPercent = 0.4f;

    /// <summary>
    /// So letters get degraded as normal, words have their percent degraded multiplied by this.
    /// </summary>
    [DataField("degradationWordMult"), ViewVariables]
    public float DegradationWordMult = 0.25f;

    /// <summary>
    /// Multiplier for degradation percent in the heavy degradation range.
    /// </summary>
    [DataField("heavyDegradationMultiplier"), ViewVariables]
    public float HeavyDegradationMultiplier = 2f;

    /// <summary>
    /// Light degradation range in meters from the optimal range
    /// In this range, messages start to degrade slightly.
    /// </summary>
    [DataField("lightDegradationRange"), ViewVariables]
    public float LightDegradationRange = 250f;

    /// <summary>
    /// Heavy degradation range in meters from the optimal range + light degradation range
    /// In this range, messages degrade heavily.
    /// </summary>
    [DataField("heavyDegradationRange"), ViewVariables]
    public float HeavyDegradationRange = 250f;

    /// <summary>
    /// Total degradation range in meters.
    /// </summary>
    [DataField("totalDegradationRange"), ViewVariables]
    public float TotalDegradationRange = 500f;

    /// <summary>
    /// Beyond the heavy degradation range, chance to not hear the message at all.
    /// Just comes through as static.
    /// </summary>
    [DataField("beyondHeavyDegradationDropChancePercent"), ViewVariables]
    public float BeyondHeavyDegradationDropChance = 0.5f;

}

// no better place to put this!!!
public sealed class RadioMessageDataHolder(
    string locBase,
    Color color,
    string chatColor,
    string fontType,
    int fontSize,
    string verb,
    string channelText,
    string name,
    string message,
    EntityUid? sender,
    RadioChannelPrototype channel
    )
{
    /// <summary>
    /// The localization base for the radio message.
    /// </summary>
    public string LocBase = locBase;

    /// <summary>
    /// The color of the radio message.
    /// </summary>
    public Color Color = color;

    /// <summary>
    /// The chatcolor of the bingle who said this message.
    /// </summary>
    public string ChatColor = chatColor;

    /// <summary>
    /// The font type of the radio message.
    /// </summary>
    public string FontType = fontType;

    /// <summary>
    /// The font size of the radio message.
    /// </summary>
    public int FontSize = fontSize;

    /// <summary>
    /// The skronk verb list of em of the radio message.
    /// </summary>
    public string Verb = verb;

    /// <summary>
    /// The channel text of the radio message.
    /// </summary>
    public string ChannelText = channelText;

    /// <summary>
    /// The name of the bingle who said this message.
    /// </summary>
    public string Name = name;

    /// <summary>
    /// The message content of the radio message.
    /// </summary>
    public string Message = message;

    /// <summary>
    /// The sender of the radio message.
    /// </summary>
    public EntityUid? Sender = sender;

    /// <summary>
    /// The channel the radio message was sent on.
    /// </summary>
    public RadioChannelPrototype Channel = channel;
}
