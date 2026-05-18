using System.Linq;
using System.Numerics;
using System.Collections.Generic;
using Content.Server.Salvage.Expeditions;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Server.Station.Components;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Shuttles.Components;
using Content.Shared.Localizations;
// _CS Start: shared expedition arrival and reservation handling
using Content.Shared.Parallax.Biomes;
// _CS End: shared expedition arrival and reservation handling
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Map; // _CS
using Content.Server.GameTicking; // _CS
using Content.Server._NF.Salvage.Expeditions.Structure; // _CS
using Content.Server._NF.Salvage.Expeditions;
using Content.Server.Body.Components;
using Content.Server.Buckle.Systems;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Server.Power.Components;
using Content.Shared._CS;
using Content.Shared.Atmos;
using Content.Shared.Buckle.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared.Mind.Components;
using Content.Shared.Salvage;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Warps;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Enums; // _CS

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles actively running a salvage expedition.
     */

    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!; // _CS
    [Dependency] private readonly BuckleSystem _buckle = default!;
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly TemperatureSystem _temperature = default!;

    private static readonly TimeSpan StandardShipExpeditionDuration = TimeSpan.FromMinutes(15);
    // _CS Start: extended shared expedition duration
    private static readonly TimeSpan SharedShipExpeditionDuration = TimeSpan.FromMinutes(30);
    // _CS End: extended shared expedition duration
    private readonly Dictionary<EntityUid, TimeSpan> _pausedShuttleExpeditionRemaining = new();
    private readonly Dictionary<EntityUid, TimeSpan> _previousNaturalAnnouncementRemaining = new();

    private void InitializeRunner()
    {
        SubscribeLocalEvent<FTLRequestEvent>(OnFTLRequest);
        SubscribeLocalEvent<FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt);
    }

    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent ev)
    {
        if (!TryComp(ev.Uid, out TransformComponent? xform) ||
            !TryComp<SalvageExpeditionComponent>(xform.MapUid, out var salvage))
        {
            return;
        }

        // TODO: This is terrible but need bluespace harnesses or something.
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var mobState, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Don't count unidentified humans (loot) or anyone you murdered so you can still maroon them once dead.
            if (_mobState.IsDead(uid, mobState))
                continue;

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != ev.Uid)
            {
                ev.Cancelled = true;
                ev.Reason = Loc.GetString("salvage-expedition-not-all-present");
                return;
            }
        }
    }

    /// <summary>
    /// Announces status updates to salvage crewmembers on the state of the expedition.
    /// </summary>
    private void Announce(EntityUid mapUid, string text)
    {
        var mapId = Comp<MapComponent>(mapUid).MapId;

        // I love TComms and chat!!!
        _chat.ChatMessageToManyFiltered(
            Filter.BroadcastMap(mapId),
            ChatChannel.Radio,
            text,
            text,
            _mapSystem.GetMapOrInvalid(mapId),
            false,
            true,
            null);
    }

    /// <summary>
    /// Announces a message only to players currently aboard a specific shuttle grid.
    /// Used for per-ship briefings when joining an already-running shared expedition.
    /// </summary>
    // _CS Start: per-grid radio announcement for shared expeditions
    private void AnnounceToGrid(EntityUid gridUid, string text) // Frontier
    {
        var filter = Filter.Empty();
        foreach (var session in _players.Sessions)
        {
            if (!session.AttachedEntity.HasValue)
                continue;

            if (Transform(session.AttachedEntity.Value).GridUid == gridUid)
                filter.AddPlayer(session);
        }

        _chat.ChatMessageToManyFiltered(
            filter,
            ChatChannel.Radio,
            text,
            text,
            gridUid,
            false,
            true,
            null);
    }
    // _CS End: per-grid radio announcement for shared expeditions
    // End Frontier

    private void OnFTLRequest(ref FTLRequestEvent ev)
    {
        if (!HasComp<SalvageExpeditionComponent>(ev.MapUid) ||
            !TryComp<FTLDestinationComponent>(ev.MapUid, out var dest))
        {
            return;
        }

        // Only one shuttle can occupy an expedition.
        dest.Enabled = false;
        _shuttleConsoles.RefreshShuttleConsoles();
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<SalvageExpeditionComponent>(args.MapUid, out var component))
            return;

        // _CS Start: shared expedition arrival and reservation handling
        ReleaseLandingZoneReservation(args.MapUid, args.Entity, component);

        if (!component.ShuttleEndTimes.ContainsKey(args.Entity))
            component.ShuttleEndTimes[args.Entity] = _timing.CurTime + GetDefaultShipExpeditionDuration(component);

        TrackSharedArrivalShuttles(args.Entity, args.MapUid, component);

        var station = _station.GetOwningStation(args.Entity);
        if (station is { Valid: true } stationUid && TryComp<SalvageExpeditionDataComponent>(stationUid, out var stationData))
        {
            stationData.CanFinish = true;
            UpdateStationConsoles(stationUid);
        }

        var isFirstArrival = component.Stage == ExpeditionStage.Added;

        // Map-wide countdown and SSD scan only on first arrival.
        if (isFirstArrival)
        {
            var arrivalRemaining = component.MissionParams.OpenContract
                ? GetDefaultShipExpeditionDuration(component)
                : component.EndTime - _timing.CurTime;

            var arrivalMinutes = GetDisplayedRemainingMinutes(arrivalRemaining);
            Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", arrivalMinutes)));
            UpdateSsdGoobers(args.MapUid, component);
        }

        // Compute direction from each arriving shuttle so every ship gets an accurate heading.
        if (component.DungeonLocation != Vector2.Zero)
        {
            // Shuttle has landed by FTLCompleted; use its actual position
            var shuttlePosition = _transform.GetMapCoordinates(args.Entity).Position;
            string? dirMsg = null;

            if (component.SharedDungeonCenters.Count > 0)
            {
                // Shared expedition: announce direction to each compound cluster.
                var directionStrings = new List<string>();
                foreach (var center in component.SharedDungeonCenters)
                {
                    var d = center - shuttlePosition;
                    if (d.LengthSquared() > 0.01f)
                        directionStrings.Add(ContentLocalizationManager.FormatDirection(d.GetDir()).ToLower());
                }
                if (directionStrings.Count > 0)
                    dirMsg = Loc.GetString("salvage-expedition-announcement-dungeon-multi", ("directions", string.Join(", ", directionStrings)));
            }
            else
            {
                var dungeonDirection = component.DungeonLocation - shuttlePosition;
                if (dungeonDirection.LengthSquared() > 0.01f)
                {
                    var directionLocalization = ContentLocalizationManager.FormatDirection(dungeonDirection.GetDir()).ToLower();
                    dirMsg = Loc.GetString("salvage-expedition-announcement-dungeon", ("direction", directionLocalization));
                }
            }

            if (dirMsg != null)
            {
                var shipRemaining = component.ShuttleEndTimes.TryGetValue(args.Entity, out var shipEndTime)
                    ? shipEndTime - _timing.CurTime
                    : component.EndTime - _timing.CurTime;

                if (shipRemaining < TimeSpan.Zero)
                    shipRemaining = TimeSpan.Zero;

                var shipMinutes = GetDisplayedRemainingMinutes(shipRemaining);
                var remainingMsg = Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", shipMinutes));

                if (isFirstArrival)
                    Announce(args.MapUid, dirMsg);
                else
                {
                    AnnounceToGrid(args.Entity, dirMsg);
                    AnnounceToGrid(args.Entity, remainingMsg);
                }
            }
        }
        // _CS End: shared expedition arrival and reservation handling

        // _CS: type-specific announcement — also per-ship for shared expeditions so late arrivals are briefed.
        string? missionMsg = null;
        switch (component.MissionParams.MissionType)
        {
            case SalvageMissionType.Destruction:
                if (TryComp<SalvageDestructionExpeditionComponent>(args.MapUid, out var destruction)
                    && destruction.Structures.Count > 0
                    && TryComp(destruction.Structures[0], out MetaDataComponent? structureMeta)
                    && structureMeta.EntityPrototype != null)
                {
                    var name = structureMeta.EntityPrototype.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        name = Loc.GetString("salvage-expedition-announcement-destruction-entity-fallback");
                    // Assuming all structures are of the same type.
                    missionMsg = Loc.GetString("salvage-expedition-announcement-destruction", ("structure", name), ("count", destruction.Structures.Count));
                }
                break;
            case SalvageMissionType.Elimination:
                if (TryComp<SalvageEliminationExpeditionComponent>(args.MapUid, out var elimination)
                    && elimination.Megafauna.Count > 0
                    && TryComp(elimination.Megafauna[0], out MetaDataComponent? targetMeta)
                    && targetMeta.EntityPrototype != null)
                {
                    var name = targetMeta.EntityPrototype.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        name = Loc.GetString("salvage-expedition-announcement-elimination-entity-fallback");
                    // Assuming all megafauna are of the same type.
                    missionMsg = Loc.GetString("salvage-expedition-announcement-elimination", ("target", name), ("count", elimination.Megafauna.Count));
                }
                break;
        }
        if (missionMsg != null)
        {
            if (isFirstArrival)
                Announce(args.MapUid, missionMsg);
            else
                AnnounceToGrid(args.Entity, missionMsg);
        }
        // _CS End

        if (isFirstArrival)
        {
            component.Stage = ExpeditionStage.Running;
            Dirty(args.MapUid, component);
        }
    }

    // _CS Start: landing zone reservation release after shuttle arrival
    private void ReleaseLandingZoneReservation(EntityUid expeditionMap, EntityUid shuttleGridUid, SalvageExpeditionComponent expedition)
    {
        if (!TryComp<MapGridComponent>(shuttleGridUid, out var shuttleGrid))
            return;

        var shuttlePosition = _transform.GetMapCoordinates(shuttleGridUid).Position;
        var shuttleBox = shuttleGrid.LocalAABB.Translated(shuttlePosition).Enlarged(1f);
        var releasedZones = new List<Box2>();

        var removed = false;
        for (var i = expedition.ReservedLandingZones.Count - 1; i >= 0; i--)
        {
            if (!expedition.ReservedLandingZones[i].Intersects(shuttleBox))
                continue;

            releasedZones.Add(expedition.ReservedLandingZones[i]);
            expedition.ReservedLandingZones.RemoveAt(i);
            removed = true;
        }

        if (removed)
        {
            // Ensure pre-reserved landing areas are immediately backfilled so they do not remain void.
            if (TryComp<BiomeComponent>(expeditionMap, out var biome) &&
                TryComp<MapGridComponent>(expeditionMap, out var expeditionGrid))
            {
                var tiles = new List<(Vector2i Index, Tile Tile)>();
                foreach (var zone in releasedZones)
                {
                    _biome.ReserveTiles(expeditionMap, zone, tiles, biome, expeditionGrid);
                }
            }

            // _CS Start: keep atmosphere-exclusion zones in sync with released landing reservations
            if (TryComp<ExpeditionAtmosphereExclusionComponent>(expeditionMap, out var atmosExclusion))
            {
                foreach (var zone in releasedZones)
                {
                    atmosExclusion.ExcludedZones.Remove(zone);
                }
            }
            // _CS End: keep atmosphere-exclusion zones in sync with released landing reservations

            Dirty(expeditionMap, expedition);
        }
    }
    // _CS End: landing zone reservation release after shuttle arrival

    private void OnFTLStarted(ref FTLStartedEvent ev)
    {
        if (!TryComp<SalvageExpeditionComponent>(ev.FromMapUid, out var expedition))
        {
            return;
        }

        expedition.ShuttleEndTimes.Remove(ev.Entity);
        expedition.ForcedDepartureShuttles.Remove(ev.Entity);
        _pausedShuttleExpeditionRemaining.Remove(ev.Entity);

        var stationUid = _station.GetOwningStation(ev.Entity);
        if (stationUid is { Valid: true } participantStation && TryComp<SalvageExpeditionDataComponent>(participantStation, out var station))
        {
            station.CanFinish = false;
            UpdateStationConsoles(participantStation);
        }

        // Check if any shuttles remain.
        var query = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();

        while (query.MoveNext(out _, out var xform))
        {
            if (xform.MapUid == ev.FromMapUid)
                return;
        }

        // Last shuttle has left so finish the mission.
        QueueDel(ev.FromMapUid.Value);
    }

    // Runs the expedition
    private void UpdateRunner()
    {
        // Generic missions
        var query = EntityQueryEnumerator<SalvageExpeditionComponent>();

        // Run the basic mission timers (e.g. announcements, auto-FTL, completion, etc)
        while (query.MoveNext(out var uid, out var comp))
        {
            var now = _timing.CurTime;
            var dirty = false;

            // For shared expeditions, keep body-recovery ownership aligned with the last shuttle grid occupied.
            UpdateSharedShuttleAssociations(uid, comp);

            // If this expedition was just created, stale shuttle pause-cache from a recycled map UID must not apply.
            if (comp.Stage == ExpeditionStage.Added)
                ClearPausedShuttleCacheForMap(uid);

            AbortIfWiped(uid, comp); // _CS

            var nearestRemaining = TimeSpan.MaxValue;
            var nearestAnnouncementRemaining = TimeSpan.MaxValue;
            var hasNaturalAnnouncementTimer = false;
            var latestEnd = now;
            var activeShuttles = new HashSet<EntityUid>();

            var shuttleQuery = AllEntityQuery<ShuttleComponent, TransformComponent>();
            while (shuttleQuery.MoveNext(out var shuttleUid, out var shuttle, out var shuttleXform))
            {
                if (shuttleXform.MapUid != uid)
                    continue;

                activeShuttles.Add(shuttleUid);

                if (!comp.ShuttleEndTimes.TryGetValue(shuttleUid, out var shuttleEnd))
                {
                    shuttleEnd = now + GetDefaultShipExpeditionDuration(comp);
                    comp.ShuttleEndTimes[shuttleUid] = shuttleEnd;
                    dirty = true;
                }

                var shuttleRemaining = shuttleEnd - now;
                if (shuttleRemaining < TimeSpan.Zero)
                    shuttleRemaining = TimeSpan.Zero;

                // Pause only this shuttle's timer when an expedition-extending anchor is active on this shuttle.
                if (IsShuttleAnchorExtending(uid, shuttleUid))
                {
                    if (!_pausedShuttleExpeditionRemaining.TryGetValue(shuttleUid, out var pausedRemaining))
                    {
                        pausedRemaining = shuttleRemaining;
                        _pausedShuttleExpeditionRemaining[shuttleUid] = pausedRemaining;
                    }

                    var pausedEnd = now + pausedRemaining;
                    if (pausedEnd != shuttleEnd)
                    {
                        shuttleEnd = pausedEnd;
                        comp.ShuttleEndTimes[shuttleUid] = shuttleEnd;
                        dirty = true;
                    }
                }
                else if (_pausedShuttleExpeditionRemaining.Remove(shuttleUid, out var resumeRemaining))
                {
                    var resumedEnd = now + resumeRemaining;
                    if (resumedEnd != shuttleEnd)
                    {
                        shuttleEnd = resumedEnd;
                        comp.ShuttleEndTimes[shuttleUid] = shuttleEnd;
                        dirty = true;
                    }
                }

                shuttleRemaining = shuttleEnd - now;
                if (shuttleRemaining < TimeSpan.Zero)
                    shuttleRemaining = TimeSpan.Zero;

                if (shuttleRemaining < nearestRemaining)
                    nearestRemaining = shuttleRemaining;

                if (!comp.ForcedDepartureShuttles.Contains(shuttleUid))
                {
                    hasNaturalAnnouncementTimer = true;
                    if (shuttleRemaining < nearestAnnouncementRemaining)
                        nearestAnnouncementRemaining = shuttleRemaining;
                }

                if (shuttleEnd > latestEnd)
                    latestEnd = shuttleEnd;

                if (HasComp<FTLComponent>(shuttleUid))
                    continue;

                if (shuttleRemaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime) + TimeSpan.FromSeconds(0.5))
                    TryAutoReturnShuttle(uid, comp, shuttleUid, shuttle, shuttleXform, shuttleRemaining);
            }

            foreach (var trackedShuttle in comp.ShuttleEndTimes.Keys.ToArray())
            {
                if (activeShuttles.Contains(trackedShuttle))
                    continue;

                comp.ShuttleEndTimes.Remove(trackedShuttle);
                comp.ForcedDepartureShuttles.Remove(trackedShuttle);
                _pausedShuttleExpeditionRemaining.Remove(trackedShuttle);
                dirty = true;
            }

            if (activeShuttles.Count == 0)
            {
                // _CS Start: clear weather/audio before expedition map deletion
                StopExpeditionWeather(uid, comp);
                // _CS End: clear weather/audio before expedition map deletion
                QueueDel(uid);
                continue;
            }

            if (comp.EndTime != latestEnd)
            {
                comp.EndTime = latestEnd;
                dirty = true;
            }

            if (nearestRemaining == TimeSpan.MaxValue)
                nearestRemaining = comp.EndTime - now;

            if (nearestRemaining < TimeSpan.Zero)
                nearestRemaining = TimeSpan.Zero;

            // Countdown announcements should be driven by naturally expiring shuttles only.
            // Forced-departure shuttles (early finish / abort) should not trigger global countdown warnings.
            if (hasNaturalAnnouncementTimer && nearestAnnouncementRemaining < TimeSpan.Zero)
                nearestAnnouncementRemaining = TimeSpan.Zero;

            // Only announce when crossing thresholds naturally, not just because we happen to already be below them.
            var previousNaturalAnnouncementRemaining = nearestAnnouncementRemaining;
            if (hasNaturalAnnouncementTimer)
            {
                if (!_previousNaturalAnnouncementRemaining.TryGetValue(uid, out previousNaturalAnnouncementRemaining))
                    previousNaturalAnnouncementRemaining = nearestAnnouncementRemaining;

                _previousNaturalAnnouncementRemaining[uid] = nearestAnnouncementRemaining;
            }
            else
            {
                _previousNaturalAnnouncementRemaining.Remove(uid);
            }

            var audioLength = _audio.GetAudioLength(comp.SelectedSong);

            if (hasNaturalAnnouncementTimer)
            {
                if (comp.Stage < ExpeditionStage.FinalCountdown &&
                    previousNaturalAnnouncementRemaining > TimeSpan.FromSeconds(45) &&
                    nearestAnnouncementRemaining <= TimeSpan.FromSeconds(45))
                {
                    comp.Stage = ExpeditionStage.FinalCountdown;
                    dirty = true;
                    Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-seconds", ("duration", TimeSpan.FromSeconds(45).Seconds)));
                }
                else if (comp.Stage < ExpeditionStage.MusicCountdown && nearestAnnouncementRemaining < audioLength) // _CS
                {
                    comp.Stage = ExpeditionStage.MusicCountdown;
                    dirty = true;
                    var musicMinutes = GetDisplayedRemainingMinutes(nearestAnnouncementRemaining);
                    Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", musicMinutes)));
                }
                else if (comp.Stage < ExpeditionStage.Countdown && nearestAnnouncementRemaining < TimeSpan.FromMinutes(5)) // _CS: 4<5
                {
                    comp.Stage = ExpeditionStage.Countdown;
                    dirty = true;
                    var countdownMinutes = GetDisplayedRemainingMinutes(nearestAnnouncementRemaining);
                    Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", countdownMinutes)));
                }
            }

            if (dirty)
                Dirty(uid, comp);
        }

        // _CS: mission-specific logic
        // Destruction
        var structureQuery = EntityQueryEnumerator<SalvageDestructionExpeditionComponent, SalvageExpeditionComponent>();

        while (structureQuery.MoveNext(out var uid, out var structure, out var comp))
        {
            if (comp.Completed)
                continue;

            var structureAnnounce = false;

            for (var i = structure.Structures.Count - 1; i >= 0; i--)
            {
                var objective = structure.Structures[i];

                if (Deleted(objective))
                {
                    structure.Structures.RemoveAt(i);
                    structureAnnounce = true;
                }
            }

            if (structureAnnounce)
                Announce(uid, Loc.GetString("salvage-expedition-structure-remaining", ("count", structure.Structures.Count)));

            if (structure.Structures.Count == 0)
            {
                comp.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-completed"));
            }
        }

        // Elimination
        var eliminationQuery = EntityQueryEnumerator<SalvageEliminationExpeditionComponent, SalvageExpeditionComponent>();
        while (eliminationQuery.MoveNext(out var uid, out var elimination, out var comp))
        {
            if (comp.Completed)
                continue;

            var announce = false;

            for (var i = elimination.Megafauna.Count - 1; i >= 0; i--)
            {
                var mob = elimination.Megafauna[i];

                if (Deleted(mob) || _mobState.IsDead(mob))
                {
                    elimination.Megafauna.RemoveAt(i);
                    announce = true;
                }
            }

            if (announce)
                Announce(uid, Loc.GetString("salvage-expedition-megafauna-remaining", ("count", elimination.Megafauna.Count)));

            if (elimination.Megafauna.Count == 0)
            {
                comp.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-completed"));
            }
        }
        // _CS End: mission-specific logic
    }

    private TimeSpan GetDefaultShipExpeditionDuration(SalvageExpeditionComponent expedition)
    {
        return expedition.MissionParams.OpenContract
            ? SharedShipExpeditionDuration
            : StandardShipExpeditionDuration;
    }

    private bool IsShuttleAnchorExtending(EntityUid expeditionMap, EntityUid shuttleUid)
    {
        var anchorQuery = EntityQueryEnumerator<StationAnchorComponent, TransformComponent, PowerChargeComponent>();
        while (anchorQuery.MoveNext(out _, out var anchor, out var anchorXform, out var anchorPower))
        {
            if (!anchor.ExtendDuration)
                continue;

            if (!anchor.SwitchedOn || !anchorPower.Active)
                continue;

            if (anchorXform.MapUid != expeditionMap || anchorXform.GridUid != shuttleUid)
                continue;

            return true;
        }

        return false;
    }

    private void ClearPausedShuttleCacheForMap(EntityUid expeditionMap)
    {
        var shuttleQuery = AllEntityQuery<ShuttleComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out var shuttleUid, out _, out var shuttleXform))
        {
            if (shuttleXform.MapUid != expeditionMap)
                continue;

            _pausedShuttleExpeditionRemaining.Remove(shuttleUid);
        }
    }

    private void TryAutoReturnShuttle(
        EntityUid expeditionMap,
        SalvageExpeditionComponent expedition,
        EntityUid shuttleUid,
        ShuttleComponent shuttle,
        TransformComponent shuttleXform,
        TimeSpan shuttleRemaining)
    {
        // Try to find a destination that does not collide with other grids.
        var mapId = _gameTicker.DefaultMap;
        if (!_mapSystem.TryGetMap(mapId, out var mapUid))
        {
            Log.Error($"Could not get DefaultMap EntityUID, shuttle {shuttleUid} may be stuck on expedition.");
            return;
        }

        var ftlTime = (float)shuttleRemaining.TotalSeconds;
        if (shuttleRemaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime))
            ftlTime = MathF.Max(0, (float)shuttleRemaining.TotalSeconds - 0.5f);

        ftlTime = MathF.Min(ftlTime, _shuttle.DefaultStartupTime);

        // Rescue stranded players to this shuttle only when allowed for this departure path.
        var shuttleGrid = shuttleXform.GridUid;
        DestinationPriority? deadLoserDestinations = null;
        if (shuttleGrid != null)
        {
            var forceShortened = expedition.ForcedDepartureShuttles.Contains(shuttleGrid.Value);
            var shouldRescueBodies = !expedition.MissionParams.OpenContract || !forceShortened;

            var mobQuery = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
            while (mobQuery.MoveNext(out var mobUid, out var mindC, out var mobXform))
            {
                if (!shouldRescueBodies)
                    continue;

                if (mobXform.MapUid != expeditionMap)
                    continue;

                if (mobXform.GridUid == shuttleGrid)
                    continue;

                if (!mindC.HasHadMind)
                    continue;

                if (expedition.MissionParams.OpenContract)
                {
                    if (!expedition.SharedArrivalShuttles.TryGetValue(mobUid, out var assignedShuttle) || assignedShuttle != shuttleGrid.Value)
                        continue;
                }

                deadLoserDestinations ??= GetDeadLoserDestinations(shuttleGrid.Value);
                // _CS Start: shared expedition random body return routing
                RescueDork(mobUid, deadLoserDestinations, shuttleGrid.Value, expedition);
                // _CS End: shared expedition random body return routing
                Spawn("EffectSparks", Transform(mobUid).Coordinates);
                Spawn("EffectGravityPulse", Transform(mobUid).Coordinates);
                SoundSpecifier sound = new SoundPathSpecifier("/Audio/_CS/ExpedReturnToBed.ogg");
                _audio.PlayPvs(sound, mobUid);
            }
        }

        int numRetries = 20;
        float minDistance = 200f;
        float minRange = 750f;
        float maxRange = 3500f;

        var gridCoords = new List<Vector2>();
        var gridQuery = EntityManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
        while (gridQuery.MoveNext(out var _, out _, out var xform))
        {
            if (xform.MapID == mapId)
                gridCoords.Add(_transform.GetWorldPosition(xform));
        }

        var dropLocation = _random.NextVector2(minRange, maxRange);
        for (var i = 0; i < numRetries; i++)
        {
            var positionIsValid = true;
            foreach (var station in gridCoords)
            {
                if (Vector2.Distance(station, dropLocation) < minDistance)
                {
                    positionIsValid = false;
                    break;
                }
            }

            if (positionIsValid)
                break;

            dropLocation = _random.NextVector2(minRange, maxRange);
        }

        _shuttle.FTLToCoordinates(
            shuttleUid,
            shuttle,
            new EntityCoordinates(mapUid.Value, dropLocation),
            0f,
            ftlTime,
            TravelTime);
    }

    /// <summary>
    /// Takes a mob, and puts them onto this shuttle.
    /// </summary>
    private void RescueDork(
        EntityUid mobUid,
        DestinationPriority possibleDestinations,
        EntityUid shuttleGrid,
        SalvageExpeditionComponent expedition)
    {
        TendToDork(mobUid);
        Spawn("EffectGravityPulse", Transform(mobUid).Coordinates);
        Spawn("EffectSparks", Transform(mobUid).Coordinates);
        // unbuckle them if they are buckled
        _buckle.TryUnbuckle(mobUid, null);
        // _CS Start: shared expedition random return shortcut
        if (TryTeleportToRandomSharedShuttlePosition(mobUid, possibleDestinations, shuttleGrid, expedition))
            return;
        // _CS End: shared expedition random return shortcut
        // try beds first
        foreach (var bedUid in possibleDestinations.Beds)
        {
            if (TryTeleportToStrap(mobUid, bedUid))
                return;
        }
        // then chairs
        foreach (var chairUid in possibleDestinations.Chairs)
        {
            if (TryTeleportToStrap(mobUid, chairUid))
                return;
        }
        // then consoles
        foreach (var consoleUid in possibleDestinations.Consoles)
        {
            var consoleXform = Transform(consoleUid);
            var mobXform = Transform(mobUid);
            _transform.SetCoordinates(mobUid, consoleXform.Coordinates);
            _transform.AttachToGridOrMap(mobUid, mobXform);
            return;
        }
        // then fallback
        foreach (var fallbackUid in possibleDestinations.Fallback)
        {
            var fallbackXform = Transform(fallbackUid);
            var mobXform = Transform(mobUid);
            _transform.SetCoordinates(mobUid, fallbackXform.Coordinates);
            _transform.AttachToGridOrMap(mobUid, mobXform);
            return;
        }
    }

    // _CS Start: randomized shared body return placement
    private bool TryTeleportToRandomSharedShuttlePosition(
        EntityUid mobUid,
        DestinationPriority possibleDestinations,
        EntityUid shuttleGrid,
        SalvageExpeditionComponent expedition)
    {
        if (!expedition.MissionParams.OpenContract)
            return false;

        if (!expedition.SharedArrivalShuttles.TryGetValue(mobUid, out var assignedShuttle) || assignedShuttle != shuttleGrid)
            return false;

        if (TryTeleportToRandomStrap(mobUid, possibleDestinations.Beds))
            return true;

        if (TryTeleportToRandomStrap(mobUid, possibleDestinations.Chairs))
            return true;

        if (TryTeleportToRandomEntityCoordinates(mobUid, shuttleGrid, possibleDestinations.Consoles))
            return true;

        if (TryTeleportToRandomEntityCoordinates(mobUid, shuttleGrid, possibleDestinations.Fallback))
            return true;

        return false;
    }

    private bool TryTeleportToRandomStrap(EntityUid mobUid, List<EntityUid> destinations)
    {
        if (destinations.Count == 0)
            return false;

        var shuffled = new List<EntityUid>(destinations);
        while (shuffled.Count > 0)
        {
            var index = _random.Next(shuffled.Count);
            var strapUid = shuffled[index];
            shuffled.RemoveAt(index);

            if (TryTeleportToStrap(mobUid, strapUid))
                return true;
        }

        return false;
    }

    private bool TryTeleportToRandomEntityCoordinates(EntityUid mobUid, EntityUid shuttleGrid, List<EntityUid> destinations)
    {
        if (destinations.Count == 0)
            return false;

        var shuffled = new List<EntityUid>(destinations);
        while (shuffled.Count > 0)
        {
            var index = _random.Next(shuffled.Count);
            var destinationUid = shuffled[index];
            shuffled.RemoveAt(index);

            var destinationXform = Transform(destinationUid);
            if (destinationXform.GridUid != shuttleGrid)
                continue;

            var mobXform = Transform(mobUid);
            _transform.SetCoordinates(mobUid, destinationXform.Coordinates);
            _transform.AttachToGridOrMap(mobUid, mobXform);
            return true;
        }

        return false;
    }
    // _CS End: randomized shared body return placement

    /// <summary>
    /// Gets a list of possible destinations for dead/dying crew to be rescued to.
    /// Tries to find a location based on a list of priorities.
    /// HERES THE PRIORITIES:
    /// 2: Beds with no mobs in them.
    /// 3: Chairs with no mobs in them.
    /// 4: I dunno the console I guess
    /// </summary>
    private DestinationPriority GetDeadLoserDestinations(EntityUid shuttleGrid)
    {
        DestinationPriority destinations = new();
        // first, find the exped consoles on the grid
        var destQuery = EntityQueryEnumerator<SalvageExpeditionConsoleComponent, TransformComponent>();
        while (destQuery.MoveNext(
                   out var uid,
                   out var _,
                   out var xform))
        {
            if (xform.GridUid != shuttleGrid)
                continue;
            destinations.Add(uid, DestinationType.Console);
        }
        // then, all beds / chairs (theyre both strap components)
        var strapQuery = EntityQueryEnumerator<StrapComponent, TransformComponent>();
        while (strapQuery.MoveNext(
                   out var uid,
                   out var strap,
                   out var xform))
        {
            if (xform.GridUid != shuttleGrid)
                continue;
            destinations.Add(uid, strap.Position == StrapPosition.Stand ? DestinationType.Chair : DestinationType.Bed);
        }
        // then some fallback stuff, find the warp point
        // worst case, we just teleport them to the center of the grid. hope its not in a wall!!
        var warpQuery = EntityQueryEnumerator<WarpPointComponent, TransformComponent>();
        while (warpQuery.MoveNext(
                   out var uid,
                   out var _,
                   out var xform))
        {
            if (xform.GridUid != shuttleGrid)
                continue;
            destinations.Add(uid, DestinationType.Fallback);
        }
        return destinations;
    }

    /// <summary>
    /// Beats the heck out of the dork if they arent dead
    /// Then extinguishes them and caps their Heat to 300ish if above that.
    /// </summary>
    private void TendToDork(EntityUid mobUid)
    {
        if (HasComp<BorgBrainComponent>(mobUid))
            return; // dont hurt borgs, theyre already dead or something
        if (_mobState.IsAlive(mobUid))
        {
            // hey you're alive! stop that!
            var hurtEmThisMuch = new DamageSpecifier()
            {
                DamageDict = { ["Slash"] = 150, ["Heat"] = 150, ["Poison"] = 100 }
            };
            _damageable.TryChangeDamage(
                mobUid,
                hurtEmThisMuch,
                true);
        }
        else if (_mobState.IsCritical(mobUid))
        {
            // I saw that, you're still alive! stop that!
            var hurtEmThisMuch = new DamageSpecifier()
            {
                DamageDict = { ["Slash"] = 50, ["Heat"] = 50, ["Poison"] = 25 }
            };
            _damageable.TryChangeDamage(
                mobUid,
                hurtEmThisMuch,
                true);
        }

        // okay, extinguish them, and clamp their burn damages to a max of 300
        // fire sucks, i hate this game
        var ev = new ExtinguishEvent
        {
            FireStacksAdjustment = -1000,
        };
        RaiseLocalEvent(mobUid, ref ev);
        if (TryComp<DamageableComponent>(mobUid, out var damageable)
            && damageable.Damage.DamageDict.TryGetValue("Heat", out var burnAmount)
            && burnAmount > 300)
        {
            var reduceBy = burnAmount - 300;
            var burnDamageSpecifier = new DamageSpecifier()
            {
                DamageDict = { ["Heat"] = -reduceBy }
            };
            _damageable.TryChangeDamage(
                mobUid,
                burnDamageSpecifier,
                true);
        }
        if (!TryComp<TemperatureComponent>(mobUid, out var comp))
            return;
        if (TryComp<ThermalRegulatorComponent>(
                mobUid,
                out var regulator)) // _CS: Look for normal body temperature and use it
        {
            _temperature.ForceChangeTemperature(
                mobUid,
                regulator.NormalBodyTemperature,
                comp);
        }
        else
        {
            _temperature.ForceChangeTemperature(
                mobUid,
                Atmospherics.T20C,
                comp);
        }
        // FIRE SUCKSSSSSSSSSS
    }

    /// <summary>
    /// Tries to teleport the mob to the strap and buckle them in.
    /// Returns true on success.
    /// </summary>
    private bool TryTeleportToStrap(EntityUid mobUid, EntityUid strapUid)
    {
        if (!TryComp<BuckleComponent>(mobUid, out var buckle))
            return false;
        if (!TryComp<StrapComponent>(strapUid, out var strap))
            return false;
        if (strap.BuckledEntities.Count > 0)
            return false; // already occupied
        var strapXform = Transform(strapUid);
        var mobXform = Transform(mobUid);
        _transform.SetCoordinates(mobUid, strapXform.Coordinates);
        _transform.AttachToGridOrMap(mobUid, mobXform);
        return _buckle.TryBuckle(
            mobUid,
            null,
            strapUid);
    }

    // class that holds a set of destinations with a priority
    private sealed class DestinationPriority
    {
        public List<EntityUid> Beds = new();
        public List<EntityUid> Chairs = new();
        public List<EntityUid> Consoles = new();
        public List<EntityUid> Fallback = new();
        public void Add(EntityUid uid, DestinationType type)
        {
            switch (type)
            {
                case DestinationType.Bed:
                    Beds.Add(uid);
                    break;
                case DestinationType.Chair:
                    Chairs.Add(uid);
                    break;
                case DestinationType.Console:
                    Consoles.Add(uid);
                    break;
                default:
                case DestinationType.Fallback:
                    Fallback.Add(uid);
                    break;
            }
        }
    }

    // enum for destination types
    private enum DestinationType
    {
        Bed,
        Chair,
        Console,
        Fallback,
    }

    // _CS Start: wipe-detection abort
    /// <summary>
    /// Checks if everyone on the map worth caring about is dead, and aborts the expedition if so.
    /// Honestly, as long as one person is not in crit and not SSD, we consider the expedition salvageable.
    /// </summary>
    private void AbortIfWiped(EntityUid mapUid, SalvageExpeditionComponent component)
    {
        if (component.MissionParams.OpenContract)
            return;

        if (component.Aborted)
            return;
        // give it a 30 second grade after first check to avoid instant aborts
        if (component.NextAutoAbortCheck == TimeSpan.Zero)
        {
            component.NextAutoAbortCheck = _timing.CurTime + TimeSpan.FromSeconds(30);
            return;
        }
        // its an entity query and idk how expensive it is, so, cooldown
        if (_timing.CurTime < component.NextAutoAbortCheck)
            return;
        component.NextAutoAbortCheck = _timing.CurTime + TimeSpan.FromSeconds(15);

        // okay first look for aghosts, whatever
        var aghostQuery =
            EntityQueryEnumerator<AdminGhostComponent, TransformComponent>();
        while (aghostQuery.MoveNext(
                   out _,
                   out _,
                   out var xform))
        {
            if (xform.MapUid == mapUid)
                return; // aghost found, dont abort
        }

        var query =
            EntityQueryEnumerator<
                MindContainerComponent,
                TransformComponent>();
        HashSet<EntityUid> pplOnThisExped = new();
        while (query.MoveNext(
                   out var uid,
                   out var mindC,
                   out var xform))
        {
            if (component.InitialSsdGoobers.Contains(uid))
                continue; // they are an initial ssd goober, ignore them
            if (xform.MapUid != mapUid)
                continue;
            // unidentified humans (loot) dont count
            if (!mindC.HasHadMind)
                continue;
            pplOnThisExped.Add(uid);
        }

        /* We habe the people on this exped, now lets check if the exped is unsalvageable
         * The criteria for unsalvageable is that everyone who 'counts' is dead
         * So who counts?
         * - Aghosts dont count (handled above)
         * - People who have never had a mind dont count (handled above)
         * - People who were SSD on arrival dont count (handled above)
         */
        foreach (var uid in pplOnThisExped.ToList())
        {
            if (!TryComp<MobStateComponent>(uid, out var mobState))
                continue;
            // if they are dead, remove them from the list and keep checking
            if (_mobState.IsDead(uid, mobState))
                pplOnThisExped.Remove(uid);
            // if... a darn posibrain...
            if (TryComp<BorgBrainComponent>(uid, out var bbrain))
            {
                // borgs just kinda.. leave behind brains when they die
                pplOnThisExped.Remove(uid); // so theyre dead or something
            }
        }
        // if anyone is left, abort is not necessary
        if (pplOnThisExped.Count > 0)
            return;
        // everyone who matters is dead, abort the expedition
        AbortNow(mapUid, component);
    }

    private void AbortNow(EntityUid mapUid, SalvageExpeditionComponent component)
    {
        if (component.Aborted)
            return;

        component.Aborted = true;
        StopExpeditionWeather(mapUid, component);
        // everyone is dead or ssd, abort the expedition
        const int departTime = 20;
        Announce(mapUid, Loc.GetString("salvage-expedition-abort-wipe"));
        Announce(mapUid, Loc.GetString("salvage-expedition-announcement-shuttle-leave-seconds", ("departTime", departTime)));
        component.NextAutoAbortCheck = TimeSpan.FromDays(1); // prevent further checks

        var forcedDeparture = _timing.CurTime + TimeSpan.FromSeconds(departTime);
        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out var shuttleUid, out _, out var shuttleXform))
        {
            if (shuttleXform.MapUid != mapUid)
                continue;

            component.ShuttleEndTimes[shuttleUid] = forcedDeparture;
            component.ForcedDepartureShuttles.Add(shuttleUid);
            _pausedShuttleExpeditionRemaining[shuttleUid] = TimeSpan.FromSeconds(departTime);
        }

        component.Stage = ExpeditionStage.FinalCountdown;
        component.EndTime = forcedDeparture;
        Dirty(mapUid, component);
    }
    // _CS End: wipe-detection abort

    private void UpdateSsdGoobers(EntityUid mapUid, SalvageExpeditionComponent component)
    {
        var query =
            EntityQueryEnumerator<
                MindContainerComponent,
                TransformComponent>();
        while (query.MoveNext(
                   out var uid,
                   out var mindC,
                   out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;
            // unidentified humans (loot) dont count
            if (!mindC.HasHadMind)
                continue;
            // if they are not ssd, dont count
            _players.TryGetSessionByEntity(uid, out var session);
            if (session != null
                && session.Status != SessionStatus.Disconnected)
                continue;
            // they are ssd, add them to the list
            component.InitialSsdGoobers.Add(uid);
        }
    }

    // _CS Start: shared arrival shuttle tracking
    private void TrackSharedArrivalShuttles(EntityUid shuttleUid, EntityUid mapUid, SalvageExpeditionComponent expedition)
    {
        if (!expedition.MissionParams.OpenContract)
            return;

        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var mobUid, out var mindContainer, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (xform.GridUid != shuttleUid)
                continue;

            // Keep this aligned with rescue eligibility: entities that have had a mind are treated as player bodies.
            if (!mindContainer.HasHadMind)
                continue;

            expedition.SharedArrivalShuttles[mobUid] = shuttleUid;
            expedition.SharedArrivalShuttleLocalPositions[mobUid] = _transform.ToCoordinates(shuttleUid, _transform.GetMapCoordinates(mobUid)).Position;
        }
    }

    private void UpdateSharedShuttleAssociations(EntityUid mapUid, SalvageExpeditionComponent expedition)
    {
        if (!expedition.MissionParams.OpenContract)
            return;

        var activeShuttleGrids = new HashSet<EntityUid>();
        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();
        while (shuttleQuery.MoveNext(out _, out var shuttleXform))
        {
            if (shuttleXform.MapUid != mapUid)
                continue;

            if (shuttleXform.GridUid is not { Valid: true } shuttleGrid)
                continue;

            activeShuttleGrids.Add(shuttleGrid);
        }

        if (activeShuttleGrids.Count == 0)
            return;

        var query = EntityQueryEnumerator<MindContainerComponent, TransformComponent>();
        while (query.MoveNext(out var mobUid, out var mindContainer, out var xform))
        {
            if (xform.MapUid != mapUid)
                continue;

            if (xform.GridUid is not { Valid: true } mobGrid)
                continue;

            if (!activeShuttleGrids.Contains(mobGrid))
                continue;

            if (!mindContainer.HasHadMind)
                continue;

            expedition.SharedArrivalShuttles[mobUid] = mobGrid;
            expedition.SharedArrivalShuttleLocalPositions[mobUid] = _transform.ToCoordinates(mobGrid, _transform.GetMapCoordinates(mobUid)).Position;
        }

        foreach (var trackedMob in expedition.SharedArrivalShuttles.Keys.ToArray())
        {
            if (EntityManager.EntityExists(trackedMob))
                continue;

            expedition.SharedArrivalShuttles.Remove(trackedMob);
            expedition.SharedArrivalShuttleLocalPositions.Remove(trackedMob);
        }
    }
    // _CS End: shared arrival shuttle tracking

    private static int GetDisplayedRemainingMinutes(TimeSpan remaining)
    {
        if (remaining <= TimeSpan.Zero)
            return 0;

        return Math.Max(1, (int)Math.Ceiling(remaining.TotalMinutes));
    }
}
