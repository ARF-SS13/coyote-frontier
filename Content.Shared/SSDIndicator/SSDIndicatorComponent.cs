using Content.Shared.CCVar;
using Content.Shared.StatusIcon;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared.SSDIndicator;

/// <summary>
/// Shows status icon when an entity is SSD, based on if a player is attached or not.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState, AutoGenerateComponentPause]
public sealed partial class SSDIndicatorComponent : Component
{
    /// <summary>
    /// Whether or not the entity is SSD.
    /// </summary>
    [AutoNetworkedField]
    [DataField, ViewVariables(VVAccess.ReadOnly)]
    public bool IsSSD = true;

    /// <summary>
    /// The icon displayed next to the associated entity when it is SSD.
    /// </summary>
    [DataField]
    [AutoNetworkedField] // Frontier: update client when icon changes
    public ProtoId<SsdIconPrototype> Icon = "SSDIcon";

    /// <summary>
    /// The time at which the entity will fall asleep, if <see cref="CCVars.ICSSDSleep"/> is true.
    /// </summary>
    [AutoNetworkedField, AutoPausedField]
    [Access(typeof(SSDIndicatorSystem))]
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan FallAsleepTime = TimeSpan.Zero;

    /// <summary>
    /// The next time this component will be updated.
    /// </summary>
    [AutoNetworkedField, AutoPausedField]
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextUpdate = TimeSpan.Zero;

    /// <summary>
    /// The time between updates checking if the entity should be force slept.
    /// </summary>
    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(1);

    // Frontier: skip sleeping
    /// <summary>
    ///     Required to don't remove forced sleep from other sources
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public bool PreventSleep = false;
    // End Frontier

    /// <summary>
    /// They went SSD at this time.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public TimeSpan WentBraindeadAt = TimeSpan.Zero;

    /// <summary>
    /// The job that was opened when they went SSD.
    /// Prevents reopening the job if they go SSD again within a certain time frame.
    /// </summary>
    public bool JobOpened = false;

    /// <summary>
    /// When they started being braindead on nash.
    /// People dont like seeing a bunch of soulless husks sitting around the bar
    /// so when it gets to idk like 3 hours, we find a cryopod and dump their dumb pu55y in it.
    /// </summary>
    [DataField]
    [AutoNetworkedField]
    public TimeSpan BraindeadNashTime = TimeSpan.Zero;

    /// <summary>
    /// if its been this long since they went SSD, we cryopod them.
    /// </summary>
    [DataField]
    public TimeSpan CryoBraindeadTimeLimit = TimeSpan.FromHours(3); // HEY DAN REMEMBER TO CHANGE THIS BACK TO 3 HOURS AFTER TESTING

}
