using Content.Client.DisplacementMap;
using System.Numerics;
using Content.Client.Consent;
using Content.Shared.Consent;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client.Humanoid;

public sealed class HumanoidAppearanceSystem : SharedHumanoidAppearanceSystem
{
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly MarkingManager _markingManager = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly DisplacementMapSystem _displacement = default!;
    [Dependency] private readonly IClientConsentManager _consentManager = default!;

    private static readonly ProtoId<ConsentTogglePrototype> GenitalMarkingsConsent = "GenitalMarkings";


    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, AfterAutoHandleStateEvent>(OnHandleState);
        _consentManager.OnServerDataLoaded += OnConsentChanged;
    }

    private void OnHandleState(EntityUid uid, HumanoidAppearanceComponent component, ref AfterAutoHandleStateEvent args)
    {
        UpdateSprite(component, Comp<SpriteComponent>(uid));
    }

    private void OnConsentChanged()
    {
        foreach (var (humanoid, sprite) in EntityQuery<HumanoidAppearanceComponent, SpriteComponent>())
        {
            UpdateSprite(humanoid, sprite);
        }
    }

    private void UpdateSprite(HumanoidAppearanceComponent component, SpriteComponent sprite)
    {
        UpdateLayers(component, sprite);
        ApplyMarkingSet(component, sprite);
        // TODO: make this thing a more versatulate proc
        var speciesPrototype = _prototypeManager.Index(component.Species);

        // Don't clamp height/width on client - the server already handles limits
        // Clamping here prevents temporary size effects (size gun, clothing, buffs) from displaying properly
        var height = component.Height;
        var width = component.Width;

        // Directly set sprite scale - this is the original approach that worked
        // Using SpriteSystem.SetScale() was causing issues with outline shader rendering
        sprite.Scale = new Vector2(width, height);

        UpdateLayersAgain(component, sprite); // cool

        sprite[sprite.LayerMapReserveBlank(HumanoidVisualLayers.Eyes)].Color = component.EyeColor;
    }

    private static bool IsHidden(HumanoidAppearanceComponent humanoid, HumanoidVisualLayers layer)
        => humanoid.HiddenLayers.ContainsKey(layer) || humanoid.PermanentlyHidden.Contains(layer);

    private void UpdateLayers(HumanoidAppearanceComponent component, SpriteComponent sprite)
    {
        var oldLayers = new HashSet<HumanoidVisualLayers>(component.BaseLayers.Keys);
        component.BaseLayers.Clear();
        component.HiddenBaseLayers.Clear();

        // add default species layers
        var speciesProto = _prototypeManager.Index(component.Species);
        var baseSprites = _prototypeManager.Index(speciesProto.SpriteSet);
        foreach (var (key, id) in baseSprites.Sprites)
        {
            oldLayers.Remove(key);
            if (!component.CustomBaseLayers.ContainsKey(key))
            {
                SetLayerData(
                    component,
                    sprite,
                    key,
                    id,
                    sexMorph: true);
            }
        }

        // add custom layers
        foreach (var (key, info) in component.CustomBaseLayers)
        {
            oldLayers.Remove(key);
            SetLayerData(
                component,
                sprite,
                key,
                info.Id,
                sexMorph: false,
                color: info.Color);
        }

        // hide old layers
        // TODO maybe just remove them altogether?
        foreach (var key in oldLayers)
        {
            if (sprite.LayerMapTryGet(key, out var index))
                sprite[index].Visible = false;
        }
    }

    /// <summary>
    /// Goes through the layers again and hides them if they are hidden by the comp's HiddenBaseLayers.
    /// Bite me
    /// </summary>
    private void UpdateLayersAgain(
        HumanoidAppearanceComponent component,
        SpriteComponent sprite)
    {
        foreach (var layer in component.HiddenBaseLayers)
        {
            if (sprite.LayerMapTryGet(layer, out var index))
            {
                sprite[index].Visible = false;
            }
        }
    }

    private void SetLayerData(
        HumanoidAppearanceComponent component,
        SpriteComponent sprite,
        HumanoidVisualLayers key,
        string? protoId,
        bool sexMorph = false,
        Color? color = null)
    {
        var layerIndex = sprite.LayerMapReserveBlank(key);
        var layer = sprite[layerIndex];
        layer.Visible = !IsHidden(component, key);

        if (color != null)
            layer.Color = color.Value;

        if (protoId == null)
            return;

        if (sexMorph)
            protoId = HumanoidVisualLayersExtension.GetSexMorph(key, component.Sex, protoId);

        var proto = _prototypeManager.Index<HumanoidSpeciesSpriteLayer>(protoId);
        component.BaseLayers[key] = proto;

        if (proto.MatchSkin)
            layer.Color = component.SkinColor.WithAlpha(proto.LayerAlpha);

        if (proto.BaseSprite != null)
            sprite.LayerSetSprite(layerIndex, proto.BaseSprite);
    }

    /// <summary>
    /// Finds this list of keys in the <see cref="HumanoidAppearanceComponent.BaseLayers"/> and
    /// Hides them if they are present. To be used after markings are applied, to hide the base
    /// layer if something is set on the marking base layer. g
    /// </summary>
    public void HideBaseLayers(
        EntityUid uid,
        HumanoidAppearanceComponent? humanoid,
        IEnumerable<HumanoidVisualLayers> layers2Hide)
    {
        if (!Resolve(uid, ref humanoid))
            return;

        var sprite = Comp<SpriteComponent>(uid);
        var speciesProto = _prototypeManager.Index(humanoid.Species);
        var baseSprites = _prototypeManager.Index(speciesProto.SpriteSet);

        foreach (var layer in layers2Hide)
        {
            if (!baseSprites.Sprites.TryGetValue(layer, out var id))
                continue;
            if (humanoid.BaseLayers.TryGetValue(layer, out var baseLayer)
                && sprite.LayerMapTryGet(layer, out var index))
            {
                sprite[index].Visible = false;
                humanoid.PermanentlyHidden.Add(layer);
            }
        }
    }

    /// <summary>
    ///     Loads a profile directly into a humanoid.
    /// </summary>
    /// <param name="uid">The humanoid entity's UID</param>
    /// <param name="profile">The profile to load.</param>
    /// <param name="humanoid">The humanoid entity's humanoid component.</param>
    /// <remarks>
    ///     This should not be used if the entity is owned by the server. The server will otherwise
    ///     override this with the appearance data it sends over.
    /// </remarks>
    public override void LoadProfile(EntityUid uid, HumanoidCharacterProfile? profile, HumanoidAppearanceComponent? humanoid = null)
    {
        if (profile == null)
            return;

        if (!Resolve(uid, ref humanoid))
        {
            return;
        }

        var customBaseLayers = new Dictionary<HumanoidVisualLayers, CustomBaseLayerInfo>();

        var speciesPrototype = _prototypeManager.Index<SpeciesPrototype>(profile.Species);
        var markings = new MarkingSet(speciesPrototype.MarkingPoints, _markingManager, _prototypeManager);

        // Add markings that doesn't need coloring. We store them until we add all other markings that doesn't need it.
        var markingFColored = new Dictionary<Marking, MarkingPrototype>();
        foreach (var marking in profile.Appearance.Markings)
        {
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                if (!prototype.ForcedColoring)
                {
                    markings.AddBack(prototype.MarkingCategory, marking);
                }
                else
                {
                    markingFColored.Add(marking, prototype);
                }
            }
        }

        // legacy: remove in the future?
        //markings.RemoveCategory(MarkingCategories.Hair);
        //markings.RemoveCategory(MarkingCategories.FacialHair);

        // We need to ensure hair before applying it or coloring can try depend on markings that can be invalid
        var hairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.Hair, out var hairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(hairAlpha)
            : profile.Appearance.HairColor;
        var hair = new Marking(profile.Appearance.HairStyleId,
            new[] { hairColor });

        var facialHairColor = _markingManager.MustMatchSkin(profile.Species, HumanoidVisualLayers.FacialHair, out var facialHairAlpha, _prototypeManager)
            ? profile.Appearance.SkinColor.WithAlpha(facialHairAlpha)
            : profile.Appearance.FacialHairColor;
        var facialHair = new Marking(profile.Appearance.FacialHairStyleId,
            new[] { facialHairColor });

        // Frontier: Match hair and facial hair colors to the forced color if it exists
        if (_markingManager.MustMatchColor(profile.Species, HumanoidVisualLayers.Hair, out var forcedHairAlpha, _prototypeManager) is Color forcedHairColor)
        {
            profile.Appearance.SkinColor.WithAlpha(forcedHairAlpha);
            hairColor = forcedHairColor;
        }
        if (_markingManager.MustMatchColor(profile.Species, HumanoidVisualLayers.FacialHair, out var forcedFacialHairAlpha, _prototypeManager) is Color forcedFacialHairColor)
        {
            profile.Appearance.SkinColor.WithAlpha(forcedFacialHairAlpha);
            facialHairColor = forcedFacialHairColor;
        }
        // End Frontier

        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, hair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.Hair, hair);
        }
        if (_markingManager.CanBeApplied(profile.Species, profile.Sex, facialHair, _prototypeManager))
        {
            markings.AddBack(MarkingCategories.FacialHair, facialHair);
        }

        // Finally adding marking with forced colors
        foreach (var (marking, prototype) in markingFColored)
        {
            var markingColors = MarkingColoring.GetMarkingLayerColors(
                prototype,
                profile.Appearance.SkinColor,
                profile.Appearance.EyeColor,
                markings
            );
            markings.AddBack(prototype.MarkingCategory, new Marking(marking.MarkingId, markingColors));
        }

        markings.EnsureSpecies(profile.Species, profile.Appearance.SkinColor, _markingManager, _prototypeManager);
        markings.EnsureSexes(profile.Sex, _markingManager);
        markings.EnsureDefault(
            profile.Appearance.SkinColor,
            profile.Appearance.EyeColor,
            _markingManager);

        DebugTools.Assert(IsClientSide(uid));

        humanoid.MarkingSet = markings;
        humanoid.PermanentlyHidden = new HashSet<HumanoidVisualLayers>();
        humanoid.HiddenLayers = new Dictionary<HumanoidVisualLayers, SlotFlags>();
        humanoid.CustomBaseLayers = customBaseLayers;
        humanoid.Sex = profile.Sex;
        humanoid.Gender = profile.Gender;
        humanoid.Age = profile.Age;
        humanoid.Species = profile.Species;
        humanoid.SkinColor = profile.Appearance.SkinColor;
        humanoid.EyeColor = profile.Appearance.EyeColor;
        humanoid.Height = profile.Height;
        humanoid.Width = profile.Width;

        UpdateSprite(humanoid, Comp<SpriteComponent>(uid));
    }

    private void ApplyMarkingSet(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        // I am lazy and I CBF resolving the previous mess, so I'm just going to nuke the markings.
        // Really, markings should probably be a separate component altogether.
        ClearAllMarkings(humanoid, sprite);

        // var censorNudity = _configurationManager.GetCVar(CCVars.AccessibilityClientCensorNudity) ||
        //                    _configurationManager.GetCVar(CCVars.AccessibilityServerCensorNudity);
        // The reason we're splitting this up is in case the character already has undergarment equipped in that slot.
        // var applyUndergarmentTop = censorNudity;
        // var applyUndergarmentBottom = censorNudity;

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype))
                {
                    ApplyMarking(markingPrototype, marking.MarkingColors, marking.Visible, humanoid, sprite);
                    // if (markingPrototype.BodyPart == HumanoidVisualLayers.UndergarmentTop)
                    //     applyUndergarmentTop = false;
                    // else if (markingPrototype.BodyPart == HumanoidVisualLayers.UndergarmentBottom)
                    //     applyUndergarmentBottom = false;
                }
            }
        }

        humanoid.ClientOldMarkings = new MarkingSet(humanoid.MarkingSet);

        // AddUndergarments(humanoid, sprite, applyUndergarmentTop, applyUndergarmentBottom);
    }

    private void ClearAllMarkings(HumanoidAppearanceComponent humanoid, SpriteComponent sprite)
    {
        foreach (var markingList in humanoid.ClientOldMarkings.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, sprite);
            }
        }

        humanoid.ClientOldMarkings.Clear();

        foreach (var markingList in humanoid.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                RemoveMarking(marking, sprite);
            }
        }

        humanoid.HiddenMarkings.Clear();
    }

    private void RemoveMarking(Marking marking, SpriteComponent spriteComp)
    {
        if (!_markingManager.TryGetMarking(marking, out var prototype))
        {
            return;
        }

        foreach (var sprite in prototype.Sprites)
        {
            if (sprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerId = $"{marking.MarkingId}-{rsi.RsiState}";
            if (!spriteComp.LayerMapTryGet(layerId, out var index))
            {
                continue;
            }

            spriteComp.LayerMapRemove(layerId);
            spriteComp.RemoveLayer(index);
        }
    }

    private void AddUndergarments(HumanoidAppearanceComponent humanoid, SpriteComponent sprite, bool undergarmentTop, bool undergarmentBottom)
    {
        if (undergarmentTop && humanoid.UndergarmentTop != null)
        {
            var marking = new Marking(humanoid.UndergarmentTop, new List<Color> { new Color() });
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                // Markings are added to ClientOldMarkings because otherwise it causes issues when toggling the feature on/off.
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentTop, new List<Marking>{ marking });
                ApplyMarking(prototype, null, true, humanoid, sprite);
            }
        }

        if (undergarmentBottom && humanoid.UndergarmentBottom != null)
        {
            var marking = new Marking(humanoid.UndergarmentBottom, new List<Color> { new Color() });
            if (_markingManager.TryGetMarking(marking, out var prototype))
            {
                humanoid.ClientOldMarkings.Markings.Add(MarkingCategories.UndergarmentBottom, new List<Marking>{ marking });
                ApplyMarking(prototype, null, true, humanoid, sprite);
            }
        }
    }

    private void ApplyMarking(MarkingPrototype markingPrototype,
        IReadOnlyList<Color>? colors,
        bool visible,
        HumanoidAppearanceComponent humanoid,
        SpriteComponent sprite)
    {
        // FLOOF ADD START
        // make a handy dict of filename -> colors
        // cus we might need to access it by filename to link
        // one sprite's colors to another
        var colorDict = new Dictionary<string, Color>();
        for (var i = 0; i < markingPrototype.Sprites.Count; i++)
        {
            var spriteName = markingPrototype.Sprites[i] switch
            {
                SpriteSpecifier.Rsi rsi => rsi.RsiState,
                SpriteSpecifier.Texture texture => texture.TexturePath.Filename,
                _ => null
            };

            if (spriteName != null)
            {
                if (colors != null && i < colors.Count)
                    colorDict.Add(spriteName, colors[i]);
                else
                    colorDict.Add(spriteName, Color.White);
            }
        }
        // now, rearrange them, copying any parented colors to children set to
        // inherit them
        if (markingPrototype.ColorLinks != null)
        {
            foreach (var (child, parent) in markingPrototype.ColorLinks)
            {
                if (colorDict.TryGetValue(parent, out var color))
                {
                    colorDict[child] = color;
                }
            }
        }
        // and, since we can't rely on the iterator knowing where the heck to put
        // each sprite when we have one marking setting multiple layers,
        // lets just kinda sorta do that ourselves
        var layerDict = new Dictionary<string, int>();

        MarkingAdded(markingPrototype, humanoid);

        visible &= !humanoid.HiddenMarkings.Contains(markingPrototype.ID); // FLOOF ADD
        // FLOOF ADD END

        // Coyote: genital markings consent toggle
        if (!(_consentManager.GetConsent().Toggles.TryGetValue(GenitalMarkingsConsent, out var consent) && consent == "on"))
        {
            visible &= markingPrototype.MarkingCategory != MarkingCategories.Genital;
        }

        for (var j = 0; j < markingPrototype.Sprites.Count; j++)
        {
            // FLOOF CHANGE START
            var markingSprite = markingPrototype.Sprites[j];
            if (markingSprite is not SpriteSpecifier.Rsi rsi)
            {
                continue;
            }

            var layerSlot = markingPrototype.BodyPart;
            // first, try to see if there are any custom layers for this marking
            if (markingPrototype.Layering != null)
            {
                var name = rsi.RsiState;
                if (markingPrototype.Layering.TryGetValue(name, out var layerName))
                {
                    layerSlot = Enum.Parse<HumanoidVisualLayers>(layerName);
                }
            }
            // update the layerDict
            // if it doesnt have this, add it at 0, otherwise increment it
            if (layerDict.TryGetValue(layerSlot.ToString(), out var layerIndex))
            {
                layerDict[layerSlot.ToString()] = layerIndex + 1;
            }
            else
            {
                layerDict.Add(layerSlot.ToString(), 0);
            }

            if (!sprite.LayerMapTryGet(layerSlot, out var targetLayer))
            {
                continue;
            }

            visible &= !IsHidden(humanoid, markingPrototype.BodyPart);
            visible &= humanoid.BaseLayers.TryGetValue(markingPrototype.BodyPart, out var setting)
                       && setting.AllowsMarkings;

            var layerId = $"{markingPrototype.ID}-{rsi.RsiState}";
            // FLOOF CHANGE END


            if (!sprite.LayerMapTryGet(layerId, out _))
            {
                // for layers that are supposed to be behind everything,
                // adding 1 to the layer index makes it not be behind
                // everything. fun! FLOOF ADD =3
                // var targLayerAdj = targetLayer == 0 ? 0 + j : targetLayer + j + 1;
                var targLayerAdj = targetLayer + layerDict[layerSlot.ToString()] + 1;
                var layer = sprite.AddLayer(markingSprite, targLayerAdj);
                sprite.LayerMapSet(layerId, layer);
                sprite.LayerSetSprite(layerId, rsi);
            }
		    // impstation edit begin - check if there's a shader defined in the markingPrototype's shader datafield, and if there is...
			if (markingPrototype.Shader != null)
			{
			// use spriteComponent's layersetshader function to set the layer's shader to that which is specified.
				sprite.LayerSetShader(layerId, markingPrototype.Shader);
			}
			// impstation edit end
            sprite.LayerSetVisible(layerId, visible);

            if (!visible || setting == null) // this is kinda implied
            {
                continue;
            }

            // Okay so if the marking prototype is modified but we load old marking data this may no longer be valid
            // and we need to check the index is correct.
            // So if that happens just default to white?
            // FLOOF ADD =3
            sprite.LayerSetColor(layerId, colorDict.TryGetValue(rsi.RsiState, out var color) ? color : Color.White);

            if (humanoid.MarkingsDisplacement.TryGetValue(markingPrototype.BodyPart, out var displacementData) && markingPrototype.CanBeDisplaced)
            {
                _displacement.TryAddDisplacement(displacementData, sprite, targetLayer + j + 1, layerId, out _);
            }
        }

        if (MarkingCategoriesConversion.Category2Layer(
                markingPrototype.MarkingCategory,
                out var whichCat))
        {
            // WEEOO WEEOO set the base layer to be hidden on the comp
            // but only if it is not already hidden
            if (!humanoid.HiddenBaseLayers.Contains(whichCat))
            {
                humanoid.HiddenBaseLayers.Add(whichCat);
            }
        }
    }

    public override void SetSkinColor(EntityUid uid, Color skinColor, bool sync = true, bool verify = true, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid) || humanoid.SkinColor == skinColor)
            return;

        base.SetSkinColor(uid, skinColor, false, verify, humanoid);

        if (!TryComp(uid, out SpriteComponent? sprite))
            return;

        foreach (var (layer, spriteInfo) in humanoid.BaseLayers)
        {
            if (!spriteInfo.MatchSkin)
                continue;

            var index = sprite.LayerMapReserveBlank(layer);
            sprite[index].Color = skinColor.WithAlpha(spriteInfo.LayerAlpha);
        }
    }

    public override void SetLayerVisibility(
        Entity<HumanoidAppearanceComponent> ent,
        HumanoidVisualLayers layer,
        bool visible,
        SlotFlags? slot,
        ref bool dirty)
    {
        base.SetLayerVisibility(ent, layer, visible, slot, ref dirty);

        var sprite = Comp<SpriteComponent>(ent);
        if (!sprite.LayerMapTryGet(layer, out var index))
        {
            if (!visible)
                return;
            index = sprite.LayerMapReserveBlank(layer);
        }

        var spriteLayer = sprite[index];
        if (spriteLayer.Visible == visible)
            return;

        spriteLayer.Visible = visible;

        // I fucking hate this. I'll get around to refactoring sprite layers eventually I swear
        // Just a week away...

        foreach (var markingList in ent.Comp.MarkingSet.Markings.Values)
        {
            foreach (var marking in markingList)
            {
                if (_markingManager.TryGetMarking(marking, out var markingPrototype) && markingPrototype.BodyPart == layer)
                    ApplyMarking(markingPrototype, marking.MarkingColors, marking.Visible, ent, sprite);
            }
        }
    }
}
