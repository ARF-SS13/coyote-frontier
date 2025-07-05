using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Humanoid.Markings
{
    [Prototype]
    public sealed partial class MarkingPrototype : IPrototype
    {
        [IdDataField]
        public string ID { get; private set; } = "uwu";

        public string Name { get; private set; } = default!;

        [DataField("bodyPart", required: true)]
        public HumanoidVisualLayers BodyPart { get; private set; } = default!;

        [DataField("markingCategory", required: true)]
        public MarkingCategories MarkingCategory { get; private set; } = default!;

        [DataField("speciesRestriction")]
        public List<string>? SpeciesRestrictions { get; private set; }

        [DataField("kindAllowance")]
        public List<string>? KindAllowance { get; private set; }

        [DataField("sexRestriction")]
        public Sex? SexRestriction { get; private set; }

        [DataField("followSkinColor")]
        public bool FollowSkinColor { get; private set; } = false;

        [DataField("forcedColoring")]
        public bool ForcedColoring { get; private set; } = false;

        [DataField("coloring")]
        public MarkingColors Coloring { get; private set; } = new();

        /// <summary>
        /// Do we need to apply any displacement maps to this marking? Set to false if your marking is incompatible
        /// with a standard human doll, and is used for some special races with unusual shapes
        /// </summary>
        [DataField]
        public bool CanBeDisplaced { get; private set; } = true;

        [DataField("sprites", required: true)]
        public List<SpriteSpecifier> Sprites { get; private set; } = default!;

        // impstation edit - allow markings to support shaders
		[DataField("shader")]
		public string? Shader { get; private set; } = null;
        // end impstation edit

        /// <summary>
        /// Allows specific images to be put into any arbitrary layer on the mob.
        /// Whole point of this is to have things like tails be able to be
        /// behind the mob when facing south-east-west, but in front of the mob
        /// when facing north. This requires two+ sprites, each in a different
        /// layer.
        /// Is a dictionary: sprite name -> layer name,
        /// e.g. "tail-cute-vulp" -> "tail-back", "tail-cute-vulp-oversuit" -> "tail-oversuit"
        /// also, FLOOF ADD =3
        /// </summary>
        [DataField("layering")]
        public Dictionary<string, string>? Layering { get; private set; }

        /// <summary>
        /// Allows you to link a specific sprite's coloring to another sprite's coloring.
        /// This is useful for things like tails, which while they have two sets of sprites,
        /// the two sets of sprites should be treated as one sprite for the purposes of
        /// coloring. Just more intuitive that way~
        /// Format: spritename getting colored -> spritename which colors it
        /// so if we have a Tail Behind with 'cooltail' as the sprite name, and a Tail Oversuit
        /// with 'cooltail-oversuit' as the sprite name, and we want to have the Tail Behind
        /// inherit the color of the Tail Oversuit, we would do:
        /// cooltail -> cooltail-oversuit
        /// cooltail will be hidden from the color picker, and just use whatevers set for
        /// cooltail-oversuit. Easy huh?
        /// also, FLOOF ADD =3
        /// </summary>
        [DataField("colorLinks")]
        public Dictionary<string, string>? ColorLinks { get; private set; }

        public Marking AsMarking()
        {
            return new Marking(ID, Sprites.Count);
        }
    }
}
