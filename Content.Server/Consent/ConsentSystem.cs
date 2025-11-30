using Content.Shared.Consent;
using Content.Server.Mind;
using Content.Shared.Mind;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.Server.Player;

namespace Content.Server.Consent;

public sealed class ConsentSystem : SharedConsentSystem
{
    [Dependency] private readonly IServerConsentManager _consent = default!;
    [Dependency] private readonly MindSystem _serverMindSystem = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly StationSpawningSystem _stationSpawning = default!;

    protected override FormattedMessage GetConsentText(NetUserId userId)
    {
        var text = _consent.GetPlayerConsentSettings(userId).Freetext;
        if (text == string.Empty)
        {
            text = Loc.GetString("consent-examine-not-set");
        }

        text += GetCharacterConsent(userId); // DEN: per-character consent.

        var message = new FormattedMessage();
        message.AddText(text);
        return message;
    }

    private string GetCharacterConsent(NetUserId userId)
    {
        var result = string.Empty;
        var hasSession = _playerManager.TryGetSessionById(userId, out var session);

        if (hasSession && session != null
            && TryComp<HumanoidAppearanceComponent>(session?.AttachedEntity, out var appearanceComponent)
            && appearanceComponent != null)
        {
            var profile = appearanceComponent.LastProfileLoaded;

            if (profile != null)
            {
                result += $"\n\n- [{profile?.Name}] -";
                result += $"\n{profile?.CharacterConsent}";
            }
        }

        return result;
    }

    public override bool HasConsent(Entity<MindContainerComponent?> ent, ProtoId<ConsentTogglePrototype> consentId)
    {
        if (!Resolve(ent, ref ent.Comp)
            || _serverMindSystem.GetMind(ent, ent) is not { } mind)
        {
            return true; // NPCs as well as player characters without a mind consent to everything
        }

        if (!TryComp<MindComponent>(mind, out var mindComponent)
            || mindComponent.UserId is not { } userId)
        {
            // Not sure if this is ever reached? MindComponent seems to always have UserId.
            Log.Warning("HasConsent No UserId or missing MindComponent");
            return false; // For entities that have a mind but with no user attached, consent to nothing.
        }

        return _consent.GetPlayerConsentSettings(userId).Toggles.TryGetValue(consentId, out var val) && val == "on";
    }
}
