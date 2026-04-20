using Robust.Shared.Prototypes;

namespace Content.Shared._CS.RolePlayIncentiveShared;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype("rpiChatAction")]
public sealed partial class RpiChatActionPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// The category this action falls under.
    /// MUST be something defined in RoleplayActs enum.
    /// </summary>
    [DataField("category", required: true)]
    public string Category = string.Empty;

    /// <summary>
    /// Award one point for every multiple of this length the message is.
    /// </summary>
    [DataField("lengthPerPoint", required: true)]
    public int LengthPerPoint = 0;

    /// <summary>
    /// Whether or not it is multiplied by the number of people present.
    /// </summary>
    [DataField("multiplyByPeoplePresent", required: true)]
    public bool MultiplyByPeoplePresent = false;

    /// <summary>
    /// Max number of people that can be considered "present" for this action.
    /// </summary>
    [DataField("maxPeoplePresent", required: true)]
    public int MaxPeoplePresent = 0;
}
