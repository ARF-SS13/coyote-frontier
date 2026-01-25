using System.Linq;
using Content.Shared.Store;
using Robust.Shared.Prototypes;

namespace Content.Shared._Coyote.RedeemableStuff;

/// <summary>
/// This handles...
/// </summary>
public sealed class RedeemableSystem : EntitySystem
{
    [Dependency]
    private readonly IPrototypeManager _prototypeManager = default!;

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<RedeemableComponent, ComponentInit>(OnRedeemableInit);
        SubscribeLocalEvent<RedeemableComponent, GetRedeemValueEvent>(OnGetRedeemValue);
    }

    private void OnRedeemableInit(EntityUid uid, RedeemableComponent c, ComponentInit args)
    {
        if (c.Preset == null)
            return;
        if (!_prototypeManager.TryIndex(c.Preset, out RedeemablePresetPrototype? preset))
            return;
        c.TurnInValues = new Dictionary<ProtoId<CurrencyPrototype>, int>
        {
            { "FrontierUplinkCoin", preset.FucValue },
            { "Doubloon", preset.DenBullionValue }
        };
    }

    private void OnGetRedeemValue(EntityUid uid, RedeemableComponent c, GetRedeemValueEvent args)
    {
        if (c.TurnInValues.Count == 0)
            return;
        if (TryComp(uid, out UnRedeemableComponent? unRedeemable))
            return;
        foreach (var (currency, amount) in c.TurnInValues)
        {
            if (!args.Values.TryAdd(currency, amount))
                args.Values[currency] += amount;
        }
    }

}

[ByRefEvent]
public sealed class GetRedeemValueEvent : EntityEventArgs
{
    public Dictionary<ProtoId<CurrencyPrototype>, int> Values = new();
}
