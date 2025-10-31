using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._Coyote.GenitalsShared;

/// <summary>
/// Defines a specific shape of genital!
/// Like, a pair of breasts, or a quartet of breasts, or an udder or something.
/// Includes a bunch of obnoxious data to make it good
/// </summary>
[Prototype("genitalShape")]
public sealed partial class GenitalShapePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; } = default!;

    /// <summary>
    /// Name of the genital shape.
    /// 'Flared Bingus' or 'Penta-dingie' or something
    /// </summary>
    [DataField("name", required: true)]
    public string Name = default!;

    /// <summary>
    /// Description of the genital shape.
    /// </summary>
    [DataField("description", required: true)]
    public string Description = default!;

    /// <summary>
    /// The All Important Size List of the genital!
    /// Has a list of sizes that the genital can be set to.
    /// But not to fast, each size can define multiple markings,
    /// in multiple layers!
    /// list(
    ///     GenitalSizeCollection(
    ///         name: 'H-cup',
    ///         sprites: list(
    ///             GenitalSizeSpriteData(
    ///               colorIndex: 0,
    ///               sprite:
    ///                 - sprite: 'path/to/sprite.rsi',
    ///                 - state: 'h-cup-front',
    ///               layeringMode: GenitalSpritePositioning.FrontMob
    ///             ),
    ///             GenitalSizeSpriteData(
    ///             colorIndex: 0,
    ///             sprite:
    ///                 - sprite: 'path/to/sprite.rsi',
    ///                 - state: 'h-cup-back',
    ///             layeringMode: GenitalSpritePositioning.BehindMob
    ///           )
    ///       )
    ///  )
    /// </summary>
    [DataField("sizes", required: true)]
    public List<GenitalSizeCollection> Sizes = new();

    /// <summary>
    /// What kind of genital is this?
    /// </summary>
    [DataField("kind", required: true)]
    public GenitalSlot Kind = GenitalSlot.NoZZZne;

    /// <summary>
    /// How many colors does this genital have?
    /// note that if its 0, it will not be colorable and you're a bad person for doing that.
    /// </summary>
    [DataField("colorCount", required: true)]
    public int ColorCount = 0;
}

/// <summary>
/// A collection of sizes for a specific genital shape.
/// </summary>
public sealed class GenitalSizeCollection(
    string name,
    List<GenitalSizeSpriteData> sprites)
{
    /// <summary>
    /// Descriptive name of the size.
    /// shows up in the list of sizes for the genital.
    /// Examples: '13 inches', 'H-cup', 'XXXXL', '3 Decigrundles', etc.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public string Name = name;

    /// <summary>
    /// The list of sprites for this size.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public List<GenitalSizeSpriteData> Sprites = sprites;

    /// <summary>
    /// The list of aroused sprites for this size.
    /// Can be null if there are no aroused sprites.
    /// </summary>
    [DataField("arousedSprites")]
    [ViewVariables(VVAccess.ReadWrite)]
    public List<GenitalSizeSpriteData>? ArousedSprites = null;
}


/// <summary>
/// Defines a singular sprite for this size of a genital!
/// </summary>
public sealed class GenitalSizeSpriteData(
    int colorIndex,
    SpriteSpecifier sprite,
    GenitalSpritePositioning layerGroup = GenitalSpritePositioning.BehindMob)
{
    /// <summary>
    /// The color index of the sprite here
    /// </summary>
    [DataField(required: true)]
    public int ColorIndex = colorIndex;

    /// <summary>
    /// The sprite RSI for this part of the size.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public SpriteSpecifier Sprite = sprite;

    /// <summary>
    /// part of the layer group this size is in.
    /// </summary>
    [DataField(required: true)]
    [ViewVariables(VVAccess.ReadWrite)]
    public GenitalSpritePositioning LayerGroup = layerGroup;
}

////////////// ENUMS //////////////

/// <summary>
/// Enum for the genital slots.
/// </summary>
/// <remarks>
///  remove the ZZZ before release, its just so copilot doesn't see a naughty word and cease to function.
///  I LOVE WHEN SOMETHING I PAY FOR JUST SAYS 'NO'
/// </remarks>
public enum GenitalSlot : byte
{
    NoZZZne,
    BrZZZeasts,
    BeZZZlly,
    BuZZZtt,
    PeZZZnis,
    TeZZZsticles,
    VaZZZgina,
}

/// <summary>
/// Defines a layer sub group for genitals.
/// So there are multiple layers of genitals, whether in front, or behind, or something else.
/// Notes:
/// Boobs have two layers, one for the front and one for the back.
///   front needs to be drawn over the hands, back needs to be drawn under the hands.
///   The normal system
/// Bellies have one layer, sadly, which is in front and behind the hands.
/// Butts are... odd, their layers are kinda reversed?
///   the front layer is visible while facing north, and is drawn over the hands.
///   the second front layer is visible else, and is drawn under the hands.
///   both these layers are drawn over the mob. fun
///   Peni and Vagi are simple, they have one layer in front of the mob.
///   Testicles are simple, they have one layer in front of the mob.
/// Therefore the layer sub groups are as follows:
/// BehindMob: for the back of the mob, behind everything
/// BetweenHands: for the front of the mob, between the hands
/// OverHands: for the front of the mob, over the hands
/// ...
/// cept none of this is possible, so we just have two sub groups:
/// BehindMob: for the back of the mob, behind everything
/// VariableLayer: In front of the mob, layered depending on whats set by the player
/// </summary>
public enum GenitalSpritePositioning : byte
{
    BehindMob,
    VariableLayer, // Between the hands, in front of the mob
}

/// <summary>
/// Genital layer groups, used to determine what the genital should be drawn on.
/// </summary>
public enum GenitalLayeringMode : byte
{
    UnderUnderwear, // set to UnderClothing by default, idk
    UnderClothing, // Between markings layers and clothing layers
    OverClothing, // Over clothing layers, like you popped your boobs out of your shirt
    OverSuit, // Over the suit, show it over everything (cept for like, fire i guess)
}

/// <summary>
/// Discrete genital layers, used to determine what the genital should be drawn on.
/// </summary>
public enum GenitalLayerGroup : byte
{
    GenitalBehind,
    GenitalUnderUndies,
    GenitalOverUndies,
    GenitalOverClothes,
    GenitalOverSuit,
}
