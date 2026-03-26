using System.Linq;
using Content.Server.Actions;
using Content.Server.Humanoid;
using Content.Shared._CS.Humanoid;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Mobs;
using Content.Shared.Toggleable;
using Content.Shared.Wagging;
using Robust.Shared.Prototypes;

namespace Content.Server.Wagging;

/// <summary>
/// Adds an action to toggle wagging animation for tails markings that supporting this
/// </summary>
public sealed class WaggingSystem : EntitySystem
{
    [Dependency] private readonly ActionsSystem _actions = default!;
    [Dependency] private readonly HumanoidAppearanceSystem _humanoidAppearance = default!;
    [Dependency] private readonly IPrototypeManager _prototype = default!;
    [Dependency] private readonly CoyoteMarkingSystem _coyoteMarking = default!; // Coyote, obviously

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WaggingComponent, ComponentInit>(OnWaggingMapInit); // Coyote: Move to Component Init
        SubscribeLocalEvent<WaggingComponent, ComponentShutdown>(OnWaggingShutdown);
        SubscribeLocalEvent<WaggingComponent, ToggleActionEvent>(OnWaggingToggle);
        SubscribeLocalEvent<WaggingComponent, MobStateChangedEvent>(OnMobStateChanged);
    }

    private void OnWaggingMapInit(EntityUid uid, WaggingComponent component, ComponentInit args) // Coyote: Move to Component Init
    {

        if (!TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
            return;

        if (!humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Tail, out var markings))
            return;

        foreach (var _ in markings.Where(marking => _coyoteMarking.TryGetWaggingId(marking.MarkingId, out _)).Where(_ => !_actions.GetAction(component.ActionEntity).HasValue))
        {
            _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
        }
    }

    private void OnWaggingShutdown(EntityUid uid, WaggingComponent component, ComponentShutdown args)
    {
        _actions.RemoveAction(uid, component.ActionEntity);
    }

    private void OnWaggingToggle(EntityUid uid, WaggingComponent component, ref ToggleActionEvent args)
    {
        if (args.Handled)
            return;

        TryToggleWagging(uid, wagging: component);
    }

    private void OnMobStateChanged(EntityUid uid, WaggingComponent component, MobStateChangedEvent args)
    {
        if (component.Wagging)
            TryToggleWagging(uid, wagging: component);
    }

    public bool TryToggleWagging(EntityUid uid, WaggingComponent? wagging = null, HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref wagging, ref humanoid))
            return false;

        if (!humanoid.MarkingSet.Markings.TryGetValue(MarkingCategories.Tail, out var markings))
            return false;

        if (markings.Count == 0)
            return false;

        wagging.Wagging = !wagging.Wagging;

        for (var idx = 0; idx < markings.Count; idx++) // Coyote: Improved wagging system
        {
            string? target;
            if (wagging.Wagging)
            {
                _coyoteMarking.TryGetStaticId(markings[idx].MarkingId, out target);
            }
            else
            {
                _coyoteMarking.TryGetWaggingId(markings[idx].MarkingId, out target);
            }

            if (target == null)
            {
                Log.Error($"Unable to find corresponding wagging or static ID for {markings[0].MarkingId}?");
                return false;
            }

            _humanoidAppearance.SetMarkingId(uid, MarkingCategories.Tail, idx, target, humanoid: humanoid);
        } // Coyote end

        return true;
    }
}
