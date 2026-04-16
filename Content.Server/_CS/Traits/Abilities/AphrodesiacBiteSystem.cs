using Content.Server.Body.Systems;
using Content.Server.Consent;
using Content.Shared._CS.Traits.Abilities;
using Content.Shared.Actions;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;

namespace Content.Server._CS.Traits.Abilities;

public sealed class AphrodesiacBiteSystem : EntitySystem
{
    [Dependency] private readonly ConsentSystem _consent = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<AphrodesiacBiteEvent>(OnBite);
        SubscribeLocalEvent<AphrodesiacBiteComponent, ComponentInit>(OnInit);
    }

    public void OnInit(EntityUid uid, AphrodesiacBiteComponent component, ComponentInit args)
    {
        _actions.AddAction(uid, ref component.ActionEntity, component.Action, uid);
    }

    public void OnBite(AphrodesiacBiteEvent ev)
    {
        if (ev.Handled)
            return;

        TryInject(ev.Target, ev.Performer);
    }

    public void TryInject(EntityUid target, EntityUid user)
    {
        if (!TryComp<BloodstreamComponent>(target, out var bloodstream))
            return;

        if (!TryComp<AphrodesiacBiteComponent>(user, out var bite))
            return;

        if (bite.RequiresConsent && !_consent.HasConsent(target, bite.ConsentToggleId))
        {
            _popup.PopupEntity(Loc.GetString("aphrodesiac-no-consent", ("target", target)), user, PopupType.LargeCaution);
            return;
        }

        var solution = new Solution(bite.Reagent, bite.Amount);
        if (_bloodstream.TryAddToChemicals(target, solution))
            _audio.PlayPvs(bite.Sound, user);
    }
}
