using Content.Shared._NF.Radar;

namespace Content.Shared._NF.Radar;

/// <summary>
/// Handles objects which should be represented by radar blips.
/// </summary>
[RegisterComponent]
public sealed partial class RadarBlipComponent : Component
{
    /// <summary>
    /// Color that gets shown on the radar screen.
    /// </summary>
    [DataField]
    public Color RadarColor { get; set; } = Color.Red;

    /// <summary>
    /// Color that gets shown on the radar screen when the blip is highlighted.
    /// </summary>
    [DataField]
    public Color HighlightedRadarColor { get; set; } = Color.OrangeRed;

    /// <summary>
    /// Scale of the blip.
    /// </summary>
    [DataField]
    public float Scale { get; set; } = 1f;

    /// <summary>
    /// The shape of the blip on the radar.
    /// </summary>
    [DataField]
    public RadarBlipShape Shape { get; set; } = RadarBlipShape.Circle;

    /// <summary>
    /// Whether this blip should be shown even when parented to a grid.
    /// </summary>
    [DataField]
    public bool RequireNoGrid { get; set; } = false;

    /// <summary>
    /// Whether this blip should be visible on radar across different grids.
    /// </summary>
    [DataField]
    public bool VisibleFromOtherGrids { get; set; } = false;

    /// <summary>
    /// Whether this blip is enabled and should be shown on radar.
    /// </summary>
    [DataField]
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Send an event to whatever has the component to do some radar blip logic.
    /// </summary>
    public bool SendRadarBlipEvent = true;
}

/// <summary>
/// The event that is sent to the entity with the RadarBlipComponent.
/// It will be modified by whatever handles the event, to tell us what to do
/// </summary>
[Serializable, ByRefEvent]
public sealed class RadarBlipEvent : EntityEventArgs
{
    public Color? ChangeColor;
    public RadarBlipShape? ChangeShape;
    public float? ChangeScale;
    public bool? ChangeEnabled;

    public RadarBlipEvent(
        Color? color = null,
        RadarBlipShape? shape = null,
        float? scale = null,
        bool? enabled = null)
    {
        ChangeColor = color;
        ChangeShape = shape;
        ChangeScale = scale;
        ChangeEnabled = enabled;
    }
}
