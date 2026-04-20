using Content.Shared.DisplacementMap;
using Robust.Shared.Prototypes;

namespace Content.Shared._CS;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype("legDisplacement")]
public sealed partial class LegDisplacementPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField]
    public Dictionary<string, DisplacementData> Displacements = new();

    [DataField]
    public Dictionary<string, DisplacementData> FemaleDisplacements = new();

    [DataField]
    public Dictionary<string, DisplacementData> MaleDisplacements = new();
}
