using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote;

/// <summary>
/// This is a prototype for...
/// </summary>
[Prototype("flarpiSettings")]
public sealed partial class FlarpiSettingsPrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; } = default!;

    /// <summary>
    /// Name token for the flarpi
    /// </summary>
    [DataField("flarpiName")]
    public string FlarpiNameToken { get; set; } = "flompy";

    /// <summary>
    /// The currency prototype ID for the flarpies we get out of the thing
    /// </summary>
    [DataField("flarpiCurrencyPrototype")]
    public ProtoId<CurrencyPrototype> FlarpiCurrencyPrototype { get; set; } = "Doubloon";

    /// <summary>
    /// How many points to give one Flarpi.
    /// </summary>
    [DataField("pointsPerFlarpi")]
    public decimal PointsPerFlarpi { get; set; } = 1000m;

    // Now the complicated part: we want to try and give X flarpies per 1 hours worth of RPI
    // Every second, FLARPI generates enough points to give one second's worth of RPI, but!
    // this scales with the number of freelancers are around you and online

    /// <summary>
    /// The base FLARPI generation rate, before multipliers.
    /// </summary>
    [DataField("baseFlarpi")]
    public int BaseFlarpi { get; set; } = 1; // 1000 points / 3600 seconds

    /// <summary>
    /// The radius to check for nearby freelancers.
    /// </summary>
    [DataField("nearbyRadius")]
    public float NearbyRadius { get; set; } = 25f;

    /// <summary>
    /// Mode of counting freelancers
    /// </summary>
    [DataField("freelancerCountMode")]
    public FlarpiCountMode FreelancerCountMode { get; set; } = FlarpiCountMode.Nearby;

    /// <summary>
    /// Mode of calculating freelancer flarpi maths
    /// </summary>
    [DataField("freelancerCalcMode")]
    public FlarpiCalculationMode FreelancerCalculationMode { get; set; } = FlarpiCalculationMode.Doubling;

    /// <summary>
    /// Max freelancers to consider for FLARPI generation
    /// </summary>
    [DataField("maxFreelancersConsidered")]
    public int MaxFreelancersConsidered { get; set; } = 0; // 0 means no limit

    /// <summary>
    /// Mode of counting NFSDs
    /// </summary>
    [DataField("nfsdCountMode")]
    public FlarpiCountMode NfsdCountMode { get; set; } = FlarpiCountMode.Nope;

    /// <summary>
    /// Mode of calculating NFSD flarpi maths
    /// </summary>
    [DataField("nfsdCalcMode")]
    public FlarpiCalculationMode NfsdCalculationMode { get; set; } = FlarpiCalculationMode.Nope;

    /// <summary>
    /// Max NFSDs to consider for FLARPI generation
    /// </summary>
    [DataField("maxNfsdsConsidered")]
    public int MaxNfsdsConsidered { get; set; } = 0; // 0 also means no limit
}

/// <summary>
/// Mode of counting players and such for FLARPI generation
/// </summary>
public enum FlarpiCountMode
{
    /// <summary>
    /// Do not count this role
    /// </summary>
    Nope,

    /// <summary>
    /// Count only nearby players
    /// </summary>
    Nearby,

    /// <summary>
    /// Count all online players
    /// </summary>
    Online,
}

/// <summary>
/// Mode of calculating FLARPI generation based on counted players
/// </summary>
public enum FlarpiCalculationMode
{
    /// <summary>
    /// No scaling
    /// </summary>
    Nope,

    /// <summary>
    /// Linear scaling
    /// X * N
    /// </summary>
    Linear,

    /// <summary>
    /// Linear, but double the effect
    /// (X * 2) * N
    /// </summary>
    LinearDoubled,

    /// <summary>
    /// Doubling scaling
    /// X * (2^N)
    /// </summary>
    Doubling,
}


