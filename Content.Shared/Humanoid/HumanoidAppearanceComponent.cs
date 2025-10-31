using Content.Shared._Coyote.GenitalsShared;
using Content.Shared.DisplacementMap;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Robust.Shared.Enums;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using Content.Shared.Preferences; //DeltaV, used for Metempsychosis, Fugitive, and Paradox Anomaly

namespace Content.Shared.Humanoid;

[NetworkedComponent, RegisterComponent, AutoGenerateComponentState(true)]
public sealed partial class HumanoidAppearanceComponent : Component
{
    public MarkingSet ClientOldMarkings = new();

    [DataField, AutoNetworkedField]
    public MarkingSet MarkingSet = new();

    [DataField]
    public Dictionary<HumanoidVisualLayers, HumanoidSpeciesSpriteLayer> BaseLayers = new();

    [DataField, AutoNetworkedField]
    public HashSet<HumanoidVisualLayers> PermanentlyHidden = new();

    // Couldn't these be somewhere else?

    [DataField, AutoNetworkedField]
    public Gender Gender;

    [DataField, AutoNetworkedField]
    public int Age = 18;

    [DataField, AutoNetworkedField]
    public string CustomSpecieName = "";

    /// <summary>
    ///     Any custom base layers this humanoid might have. See:
    ///     limb transplants (potentially), robotic arms, etc.
    ///     Stored on the server, this is merged in the client into
    ///     all layer settings.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo> CustomBaseLayers = new();

    /// <summary>
    ///     Current species. Dictates things like base body sprites,
    ///     base humanoid to spawn, etc.
    /// </summary>
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<SpeciesPrototype> Species { get; set; }

    /// <summary>
    ///     The initial profile and base layers to apply to this humanoid.
    /// </summary>
    [DataField]
    public ProtoId<HumanoidProfilePrototype>? Initial { get; private set; }

    /// <summary>
    ///     Skin color of this humanoid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Color SkinColor { get; set; } = Color.FromHex("#C0967F");

    /// <summary>
    ///     A map of the visual layers currently hidden to the equipment
    ///     slots that are currently hiding them. This will affect the base
    ///     sprite on this humanoid layer, and any markings that sit above it.
    /// </summary>
    [DataField, AutoNetworkedField]
    public Dictionary<HumanoidVisualLayers, SlotFlags> HiddenLayers = new();

    /// <summary>
    /// So. When the mob has a custom layer base thing in this slot, it will be hidden.
    /// </summary>
    [DataField, AutoNetworkedField]
    public List<HumanoidVisualLayers> HiddenBaseLayers = new();

    /// <summary>
    /// The specific markings that are hidden, whether or not the layer is hidden.
    /// This is so we can just turn off a single marking, or part of a single marking.
    /// (cus underwear, its for underwear, so you can take off your bra and still have your shirt on)
    /// FLOOF ADD
    /// </summary>
    [DataField, AutoNetworkedField]
    public HashSet<string> HiddenMarkings = new();

    [DataField, AutoNetworkedField]
    public Sex Sex = Sex.Male;

    [DataField, AutoNetworkedField]
    public Color EyeColor = Color.Brown;

    /// <summary>
    ///     Hair color of this humanoid. Used to avoid looping through all markings
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Color? CachedHairColor;

    /// <summary>
    ///     Facial Hair color of this humanoid. Used to avoid looping through all markings
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public Color? CachedFacialHairColor;

    /// <summary>
    ///     Which layers of this humanoid that should be hidden on equipping a corresponding item..
    /// </summary>
    [DataField]
    public HashSet<HumanoidVisualLayers> HideLayersOnEquip = [HumanoidVisualLayers.Hair];

    /// <summary>
    ///     Which markings the humanoid defaults to when nudity is toggled off.
    /// </summary>
    [DataField]
    public ProtoId<MarkingPrototype>? UndergarmentTop = new ProtoId<MarkingPrototype>("UndergarmentTopTanktop");

    [DataField]
    public ProtoId<MarkingPrototype>? UndergarmentBottom = new ProtoId<MarkingPrototype>("UndergarmentBottomBoxers");

    /// <summary>
    ///     The displacement maps that will be applied to specific layers of the humanoid.
    /// </summary>
    [DataField]
    public Dictionary<HumanoidVisualLayers, DisplacementData> MarkingsDisplacement = new();

    /// <summary>
    /// DeltaV - let paradox anomaly be cloned
    /// TODO: paradox clones
    /// </summary>
    [ViewVariables]
    public HumanoidCharacterProfile? LastProfileLoaded;

    /// <summary>
    ///     The height of this humanoid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Height = 1f;

    /// <summary>
    ///     The width of this humanoid.
    /// </summary>
    [DataField, AutoNetworkedField]
    public float Width = 1f;

    /// <summary>
    /// The dict of genitals that are currently being used.
    /// THIS STORES a lot of the data!
    /// </summary>
    public List<GenitalData> Genitals = new();

    public List<GenitalSpriteMetaData> GenitalSpriteMetaDatas = new();

}

[DataDefinition]
[Serializable, NetSerializable]
public readonly partial struct CustomBaseLayerInfo
{
    public CustomBaseLayerInfo(string? id, Color? color = null)
    {
        DebugTools.Assert(id == null || IoCManager.Resolve<IPrototypeManager>().HasIndex<HumanoidSpeciesSpriteLayer>(id));
        Id = id;
        Color = color;
    }

    /// <summary>
    ///     ID of this custom base layer. Must be a <see cref="HumanoidSpeciesSpriteLayer"/>.
    /// </summary>
    [DataField]
    public ProtoId<HumanoidSpeciesSpriteLayer>? Id { get; init; }

    /// <summary>
    ///     Color of this custom base layer. Null implies skin colour if the corresponding <see cref="HumanoidSpeciesSpriteLayer"/> is set to match skin.
    /// </summary>
    [DataField]
    public Color? Color { get; init; }
}

/// <summary>
/// Genital data, holds some cool data about the genitals
/// Needs to be modifiable cus these values will change, a lot!
/// </summary>
public sealed class GenitalData(
    ProtoId<GenitalShapePrototype> prototype,
    int size = 0,
    List<Color> colors = null!,
    bool hidden = false,
    bool aroused = false,
    GenitalLayeringMode layeringMode = GenitalLayeringMode.UnderClothing)
{
    /// <summary>
    /// The genital prototype. Generally doesnt change
    /// </summary>
    public ProtoId<GenitalShapePrototype> Prototype = prototype;

    /// <summary>
    /// The current size of the genital.
    /// Will be sanitized to be within the min and max size of the prototype.
    /// </summary>
    public int Size = size;

    /// <summary>
    /// The colors of the genital.
    /// Can be up to three colors, but can be less.
    /// </summary>
    public List<Color> Colors = colors;

    /// <summary>
    /// Is it hidden?
    /// </summary>
    public bool Hidden = hidden;

    /// <summary>
    /// Is it aroused?
    /// </summary>
    public bool Aroused = aroused;

    /// <summary>
    /// What layer group should this genital be drawn on?
    /// </summary>
    public GenitalLayeringMode LayeringMode = layeringMode;

    /// <summary>
    /// The sprites we're currently using for this genital.
    /// This is a dictionary of sprite keys, with a chunk of metadata.
    /// </summary>
    public Dictionary<string, GenitalSpriteMetaData> Sprites = new();


    /// <summary>
    /// Generates a string that can be used to store the genital data in a database.
    /// WELCOME TO SERIALIZE
    /// </summary>
    public string Genital2String()
    {
        // reserved character
        var sanitizedProtoId = Prototype.ToString().Replace('@', '_');
        List<string> colorStringList = new();
        foreach (Color color in Colors)
        {
            colorStringList.Add(color.ToHex());
        }
        var colorString = string.Join("&&&", colorStringList);
        var sizeString = Size.ToString();
        var hiddenString = Hidden ? "HIDDEN" : "VISIBLE";
        var arousedString = Aroused ? "AROUSED" : "NOTAROUSED";
        var layerGroupString = LayeringMode switch
        {
            GenitalLayeringMode.UnderUnderwear => "DEFAULT",
            GenitalLayeringMode.UnderClothing => "UNDERCLOTHING",
            GenitalLayeringMode.OverClothing => "OVERCLOTHING",
            GenitalLayeringMode.OverSuit => "OVERSUIT",
            _ => "DEFAULT",
        };
        var outString =
            sanitizedProtoId
            + "@"
            + sizeString
            + "@"
            + hiddenString
            + "@"
            + arousedString
            + "@"
            + layerGroupString
            + "@"
            + colorString;

        return outString;
    }

    public static GenitalData? ParseFromDbString(string input)
    {
        if (input.Length == 0)
            return null;
        var split = input.Split('@');
        if (split.Length != 6)
            return null;
        // indexes:
        // 0 - prototype
        // 1 - size
        // 2 - hidden
        // 3 - aroused
        // 4 - layer group
        // 5 - colors
        var prototype = new ProtoId<GenitalShapePrototype>(split[0].Replace('_', '@'));
        var size = int.Parse(split[1]);
        var hidden = split[2] == "HIDDEN";
        var aroused = split[3] == "AROUSED";
        var layerGroup = split[4] switch
        {
            "UNDERUNDERWEAR" => GenitalLayeringMode.UnderUnderwear,
            "UNDERCLOTHING" => GenitalLayeringMode.UnderClothing,
            "OVERCLOTHING" => GenitalLayeringMode.OverClothing,
            "OVERSUIT" => GenitalLayeringMode.OverSuit,
            _ => GenitalLayeringMode.UnderUnderwear,
        };
        var colorString = split[5];
        var splitColor = colorString.Split("&&&");
        var colorList = SplitColor2ColorList(splitColor);
        var genDatNew = new GenitalData(
            prototype,
            size,
            colorList,
            hidden,
            aroused,
            layerGroup);
        return genDatNew;
    }
    private static List<Color> SplitColor2ColorList(string[] splitColor)
    {
        if (splitColor.Length == 0)
            return new List<Color>();
        List<Color> colorList = new();
        foreach (string color in splitColor)
        {
            if (color.Length == 0)
                continue;
            colorList.Add(Color.FromHex(color));
        }
        return colorList;
    }

}

/// <summary>
/// Genital sprite metadata.
/// Hoooooolds.... sprite RSI path, sprite state, color index, layer group, the key for the sprite
/// this is for a single sprite, not a size.
/// </summary>
public sealed class GenitalSpriteMetaData(
    string spriteRsiPath,
    string spriteState,
    int colorIndex,
    Color colorTrue,
    GenitalSpritePositioning layerGroup,
    string key)
{
    /// <summary>
    /// The RSI path of the sprite.
    /// </summary>
    public string SpriteRsiPath = spriteRsiPath;

    /// <summary>
    /// The state of the sprite.
    /// </summary>
    public string SpriteState = spriteState;

    /// <summary>
    /// The color index of the sprite.
    /// </summary>
    public int ColorIndex = colorIndex;

    /// <summary>
    /// The actual color of the sprite.
    /// </summary>
    public Color Color = colorTrue;

    /// <summary>
    /// The layer group of the sprite.
    /// </summary>
    public GenitalSpritePositioning LayerGroup = layerGroup;

    /// <summary>
    /// The key for the sprite.
    /// </summary>
    public string Key = key;
}







