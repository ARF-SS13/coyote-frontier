using System.Collections.Frozen;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote.GenitalsShared;

/// <summary>
/// Hi! This is the genital manager! It handles all the stuff related to genitals.
/// TAKE ME TO YOUR GENITAL MANAGER maam I AM the genital manager
/// </summary>
public sealed class GenitalManager
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    private readonly List<GenitalShapePrototype> _index = new();

    public FrozenDictionary<GenitalSlot, FrozenDictionary<string, GenitalShapePrototype>>
        CategorizedGenitals = default!;

    public FrozenDictionary<string, GenitalShapePrototype> Genitals = default!;
    public List<GenitalSlot> GenitalSlotsOrdered = new()
    {
        GenitalSlot.NoZZZne,
        GenitalSlot.BuZZZtt,
        GenitalSlot.VaZZZgina,
        GenitalSlot.TeZZZsticles,
        GenitalSlot.PeZZZnis,
        GenitalSlot.BeZZZlly,
        GenitalSlot.BrZZZeasts,
    };

    public List<string> GenitalSlotsOrderedStrings = new();
    public List<string> GenitalGroupsStrings = new();

    public void Initialize()
    {
        _prototypeManager.PrototypesReloaded += OnPrototypeReload;
        CachePrototypes();
        CacheGenitalGroups();
        CacheGenitalSlotsOrderedStrings();
    }

    private void CachePrototypes()
    {
        _index.Clear();
        var markingDict = new Dictionary<GenitalSlot, Dictionary<string, GenitalShapePrototype>>();

        foreach (var category in Enum.GetValues<GenitalSlot>())
        {
            markingDict.Add(category, new Dictionary<string, GenitalShapePrototype>());
        }

        foreach (var prototype in _prototypeManager.EnumeratePrototypes<GenitalShapePrototype>())
        {
            _index.Add(prototype);
            markingDict[prototype.Kind].Add(prototype.ID, prototype);
        }

        Genitals = _prototypeManager.EnumeratePrototypes<GenitalShapePrototype>().ToFrozenDictionary(x => x.ID);
        CategorizedGenitals = markingDict.ToFrozenDictionary(
            x => x.Key,
            x => x.Value.ToFrozenDictionary());
    }

    /// <summary>
    /// Goes through GenitalLayerGroup enum and makes a list of all the genital groups, as strings.
    /// </summary>
    private void CacheGenitalGroups()
    {
        GenitalGroupsStrings.Clear();
        foreach (var genitalGroup in Enum.GetValues<GenitalLayerGroup>())
        {
            GenitalGroupsStrings.Add(genitalGroup.ToString());
        }
    }

    /// <summary>
    /// Goes through GenitalSlotsOrdered and makes a list of all the genital slots, as strings.
    /// </summary>
    private void CacheGenitalSlotsOrderedStrings()
    {
        GenitalSlotsOrderedStrings.Clear();
        foreach (var slot in GenitalSlotsOrdered)
        {
            GenitalSlotsOrderedStrings.Add(slot.ToString());
        }
    }

    private void OnPrototypeReload(PrototypesReloadedEventArgs args)
    {
        if (args.WasModified<MarkingPrototype>())
            CachePrototypes();
    }

    public void GetGenital(ProtoId<GenitalShapePrototype> id, out GenitalShapePrototype? prototype)
    {
        if (Genitals.TryGetValue(id, out prototype))
            return;
        prototype = null;
    }

    /// <summary>
    /// Gets the GenitalSizeCollection for a given genital shape prototype ID and size index.
    /// Also sanitizes the size index to ensure it is within bounds.
    /// </summary>
    /// <param name="id">The ID of the genital shape prototype.</param>
    /// <param name="saniSize">The sanitized size index, clamped to the valid range.</param>
    /// <param name="genSizeProt">The GenitalSizeCollection for the specified ID and size index.</param>
    public void GetGenitalSize(ProtoId<GenitalShapePrototype> id,
        ref int saniSize,
        out GenitalSizeCollection? genSizeProt)
    {
        if (!Genitals.TryGetValue(id, out var prototype))
        {
            saniSize = 0;
            genSizeProt = null!;
            return;
        }

        saniSize = Math.Clamp(
            saniSize,
            0,
            prototype.Sizes.Count - 1);
        genSizeProt = prototype.Sizes[saniSize];
    }

    public void GetGenitalSlot(string id, out GenitalSlot slot)
    {
        slot = GenitalSlot.NoZZZne;
        GetGenital(id, out var prototype);
        if (prototype != null)
            slot = prototype.Kind;
    }

    public List<string> GetGenitalSlotStrings()
    {
        if (GenitalSlotsOrderedStrings.Count == 0)
        {
            CacheGenitalSlotsOrderedStrings();
        }
        return GenitalSlotsOrderedStrings;
    }

    public List<String> GetGenitalGroupStrings()
    {
        if (GenitalGroupsStrings.Count == 0)
            CacheGenitalGroups();
        return GenitalGroupsStrings;
    }

    public string GenitalLayerId(
        string genitalGroup,
        string genitalSlot)
    {
        return $"{genitalGroup}_{genitalSlot}";
    }

    public string GenitalSpriteLayerId(
        string genitalGroup,
        string genitalSlot,
        string genitalSpriteRsi,
        string genitalSpriteState)
    {
        return $"{genitalGroup}_{genitalSlot}_{genitalSpriteRsi}_{genitalSpriteState}";
    }
}
