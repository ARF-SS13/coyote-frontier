using Content.Shared._Coyote.RolePlayIncentiveShared;
using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote;

/// <summary>
/// This is to make you classed as pirate!!!
/// </summary>
[RegisterComponent]
public sealed partial class IsPirateComponent : Component;

/// <summary>
/// This is make to have u be classed as NFSd!!!
/// </summary>
[RegisterComponent]
public sealed partial class IsNfsdComponent : Component
{
    /// <summary>
    /// How worth are they
    /// </summary>
    [DataField("worth")]
    public int Worth = 1;
}
