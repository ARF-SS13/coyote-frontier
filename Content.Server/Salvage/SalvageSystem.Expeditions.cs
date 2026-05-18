using System.Linq;
using System.Threading;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Shared.CCVar;
using Content.Shared.Popups;
using Content.Shared.Examine;
using Content.Shared.Random.Helpers;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.CPUJob.JobQueues.Queues;
using Robust.Shared.GameStates;
using Robust.Shared.Map;
using Content.Server._NF.Salvage.Expeditions; // _CS
using Content.Server.Station.Components; // _CS
using Content.Shared.Procedural; // _CS
using Content.Shared.Salvage; // _CS
using Robust.Shared.Prototypes; // _CS
using Content.Shared._NF.CCVar; // _CS
using Content.Shared.Shuttles.Components; // _CS
using Robust.Shared.Configuration;
using Content.Shared.Ghost;
using System.Numerics; // _CS

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles setup / teardown of salvage expeditions.
     */

    private const int MissionLimit = 5; // _CS: 3<5
    // _CS Start: shared board reset cadence
    private const float SharedExpeditionCooldown = 1500f; // Frontier: 25 minutes for open contract board
    // _CS End: shared board reset cadence

    private readonly JobQueue _salvageQueue = new();
    private readonly List<(SpawnSalvageMissionJob Job, CancellationTokenSource CancelToken)> _salvageJobs = new();
    private const double SalvageJobTime = 0.002;
    private readonly List<(ProtoId<SalvageDifficultyPrototype> id, int value)> _missionDifficulties = [("NFModerate", 0), ("NFHazardous", 1), ("NFExtreme", 2)]; // _CS: mission difficulties with order

    [Dependency] private readonly IConfigurationManager _cfgManager = default!; // _CS

    private float _cooldown;
    private float _failedCooldown; // _CS
    public float TravelTime { get; private set; } // _CS
    public bool ProximityCheck { get; private set; } // _CS

    private void InitializeExpeditions()
    {
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ComponentInit>(OnSalvageConsoleInit);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, EntParentChangedMessage>(OnSalvageConsoleParent);
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, ClaimSalvageMessage>(OnSalvageClaimMessage);
        SubscribeLocalEvent<ExpeditionSpawnCompleteEvent>(OnExpeditionSpawnComplete); // _CS: more gracefully handle expedition generation failures
        SubscribeLocalEvent<SalvageExpeditionConsoleComponent, FinishSalvageMessage>(OnSalvageFinishMessage); // _CS: For early finish

        SubscribeLocalEvent<SalvageExpeditionComponent, MapInitEvent>(OnExpeditionMapInit);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentShutdown>(OnExpeditionShutdown);
        SubscribeLocalEvent<SalvageExpeditionComponent, ComponentGetState>(OnExpeditionGetState);
        SubscribeLocalEvent<SalvageExpeditionComponent, EntityTerminatingEvent>(OnMapTerminating); // _CS

        SubscribeLocalEvent<SalvageStructureComponent, ExaminedEvent>(OnStructureExamine);

        _cooldown = _cfgManager.GetCVar(CCVars.SalvageExpeditionCooldown);
        Subs.CVar(_cfgManager, CCVars.SalvageExpeditionCooldown, SetCooldownChange);
        _failedCooldown = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionFailedCooldown); // _CS
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionFailedCooldown, SetFailedCooldownChange); // _CS
        TravelTime = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionTravelTime); // _CS
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionTravelTime, SetTravelTime); // _CS
        ProximityCheck = _cfgManager.GetCVar(NFCCVars.SalvageExpeditionProximityCheck); // _CS
        Subs.CVar(_cfgManager, NFCCVars.SalvageExpeditionProximityCheck, SetProximityCheck); // _CS
    }

    private void OnExpeditionGetState(EntityUid uid, SalvageExpeditionComponent component, ref ComponentGetState args)
    {
        args.State = new SalvageExpeditionComponentState()
        {
            Stage = component.Stage,
            SelectedSong = component.SelectedSong // _CS: note, not dirtied on map init (not needed)
        };
    }

    private void SetCooldownChange(float obj)
    {
        // Update the active cooldowns if we change it.
        // Note: shared boards use their own fixed cooldown and are not updated here.
        _cooldown = obj;
    }

    // _CS: failed cooldowns
    private void SetFailedCooldownChange(float obj)
    {
        // Note: we don't know whether or not players have failed missions, so let's not punish/reward them if this gets changed.
        _failedCooldown = obj;
    }

    private void SetTravelTime(float obj)
    {
        TravelTime = obj;
    }

    private void SetProximityCheck(bool obj)
    {
        ProximityCheck = obj;
    }
    // _CS End

    private void OnExpeditionMapInit(EntityUid uid, SalvageExpeditionComponent component, MapInitEvent args)
    {
        // Ensure any old shuttle pause cache for a reused map UID cannot affect a fresh expedition.
        ClearPausedShuttleCacheForMap(uid);
        component.SelectedSong = _audio.ResolveSound(component.Sound);
        // _CS Start: map-lifecycle weather init
        InitExpeditionWeather(uid, component);
        // _CS End: map-lifecycle weather init
    }

    private void OnExpeditionShutdown(EntityUid uid, SalvageExpeditionComponent component, ComponentShutdown args)
    {
        // _CS Start: ensure weather/audio cleanup on expedition shutdown
        StopExpeditionWeather(uid, component);
        // _CS End: ensure weather/audio cleanup on expedition shutdown

        // Drop shuttle pause cache when expedition map is shutting down.
        ClearPausedShuttleCacheForMap(uid);

        // component.Stream = _audio.Stop(component.Stream); // _CS: moved to client

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            if (job.Station == component.Station)
            {
                cancelToken.Cancel();
                _salvageJobs.Remove((job, cancelToken));
            }
        }

        if (Deleted(component.Station))
            return;

        // Finish mission
        if (TryComp<SalvageExpeditionDataComponent>(component.Station, out var data))
        {
            FinishExpedition((component.Station, data), component, uid); // _CS: add component
        }
    }

    private void UpdateExpeditions()
    {
        var currentTime = _timing.CurTime;
        _salvageQueue.Process();

        foreach (var (job, cancelToken) in _salvageJobs.ToArray())
        {
            switch (job.Status)
            {
                case JobStatus.Finished:
                    _salvageJobs.Remove((job, cancelToken));
                    break;
            }
        }

        foreach (var board in _sharedExpeditionBoards.Values)
        {
            if (board.NextOffer > currentTime)
                continue;

            board.Cooldown = false;
            board.NextOffer = currentTime + TimeSpan.FromSeconds(SharedExpeditionCooldown);
            board.CooldownTime = TimeSpan.FromSeconds(SharedExpeditionCooldown);
            board.ActiveMission = 0;
            board.JoinableExpedition = null;
            ClearPendingClaims(board);
            GenerateMissions(board);

            UpdateEconomyConsoles(board.EconomyId);
        }

        // _CS: Per-station mission generation
        var stationDataQuery = EntityQueryEnumerator<SalvageExpeditionDataComponent>();
        while (stationDataQuery.MoveNext(out var stationUid, out var stationData))
        {
            // Frontier: private board regeneration is fully independent from shared board and open contracts.
            // Skip entirely while any mission (private or open contract) is active.
            if (stationData.ActiveMission != 0)
                continue;

            // Regenerate private missions when:
            // - offers are uninitialized/missing (startup/post-clear)
            // - OR private timer reaches NextOffer (normal periodic reroll and post-cooldown refresh)
            var needsRegeneration = stationData.Missions.Count == 0
                || stationData.NextOffer <= currentTime;

            if (!needsRegeneration)
                continue;

            stationData.Cooldown = false;
            // Keep private offer refresh behavior consistent with historical expedition flow:
            // when offers regenerate, start a new refresh timer for the next reroll.
            stationData.NextOffer = currentTime + TimeSpan.FromSeconds(_cooldown);
            stationData.CooldownTime = TimeSpan.FromSeconds(_cooldown);
            GenerateMissions(stationData);
            UpdateStationConsoles(stationUid);
        }
        // _CS End: Per-station mission generation
    }

    private void FinishExpedition(Entity<SalvageExpeditionDataComponent> expedition, SalvageExpeditionComponent expeditionComp, EntityUid uid)
    {
        var announcement = expeditionComp.Completed
            ? Loc.GetString("salvage-expedition-completed")
            : Loc.GetString("salvage-expedition-failed");

        var participantCooldownSecs = expeditionComp.Completed ? _cooldown : _failedCooldown;
        foreach (var participant in expeditionComp.ParticipantStations)
        {
            if (!TryComp<SalvageExpeditionDataComponent>(participant, out var data))
                continue;

            data.ActiveMission = 0;
            data.CanFinish = false;

            // Frontier: open contract expeditions don't impose a cooldown on individual ship boards;
            // only the shared board has its own independent cooldown.
            if (!expeditionComp.MissionParams.OpenContract)
            {
                data.Cooldown = true;
                data.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(participantCooldownSecs);
                data.CooldownTime = TimeSpan.FromSeconds(participantCooldownSecs);
            }
            // End Frontier

            UpdateStationConsoles(participant);
        }

        if (_sharedExpeditionBoards.TryGetValue(expeditionComp.EconomyId, out var board) &&
            board.JoinableExpedition == uid)
        {
            board.JoinableExpedition = null;
            if (board.ActiveMission == expeditionComp.MissionParams.Index)
                board.ActiveMission = 0;

            board.Cooldown = true;
                // Frontier: Missions are intentionally NOT cleared here — they remain visible (disabled) during
                // cooldown and are replaced with fresh missions when the cooldown timer expires.
            board.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(SharedExpeditionCooldown);
            board.CooldownTime = TimeSpan.FromSeconds(SharedExpeditionCooldown);
            ClearPendingClaims(board);
            UpdateEconomyConsoles(board.EconomyId);
        }

        expedition.Comp.ActiveMission = 0;
        expedition.Comp.CanFinish = false;

        // Keep private board timing fully independent from shared/open-contract completion.
        if (!expeditionComp.MissionParams.OpenContract)
        {
            // _CS: separate timeout/announcement for success/failures
            if (expeditionComp.Completed)
            {
                expedition.Comp.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_cooldown);
                expedition.Comp.CooldownTime = TimeSpan.FromSeconds(_cooldown);
            }
            else
            {
                expedition.Comp.NextOffer = _timing.CurTime + TimeSpan.FromSeconds(_failedCooldown);
                expedition.Comp.CooldownTime = TimeSpan.FromSeconds(_failedCooldown);
            }
            // _CS End: separate timeout/announcement for success/failures
            expedition.Comp.Cooldown = true;
        }

        UpdateConsoles(expedition);
        Announce(uid, announcement);
    }

    private void GenerateMissions(SalvageExpeditionDataComponent component)
    {
        component.Missions.Clear();

        // _CS: generate missions from an arbitrary set of difficulties
        if (_missionDifficulties.Count <= 0)
        {
            Log.Error("No expedition mission difficulties to pick from!");
            return;
        }

        // this doesn't support having more missions than types of ratings
        // but the previous system didn't do that either.
        var allDifficulties = _missionDifficulties; // _CS: Enum.GetValues<DifficultyRating>() < _missionDifficulties
        _random.Shuffle(allDifficulties);
        var difficulties = allDifficulties.Take(MissionLimit).ToList();

        // If we support more missions than there are accepted types, pick more until you're up to MissionLimit
        while (difficulties.Count < MissionLimit)
        {
            var difficultyIndex = _random.Next(_missionDifficulties.Count);
            difficulties.Add(_missionDifficulties[difficultyIndex]);
        }
        difficulties.Sort((x, y) => { return Comparer<int>.Default.Compare(x.value, y.value); });

        for (var i = 0; i < MissionLimit; i++)
        {
            var mission = new SalvageMissionParams
            {
                Index = component.NextIndex,
                MissionType = (SalvageMissionType)_random.NextByte((byte)SalvageMissionType.Max + 1), // _CS
                Seed = _random.Next(),
                Difficulty = difficulties[i].id,
            };

            component.Missions[component.NextIndex++] = mission;
        }
        // _CS End: generate missions from an arbitrary set of difficulties
    }

    private void GenerateMissions(SharedExpeditionBoard board)
    {
        board.Missions.Clear();

        if (_missionDifficulties.Count <= 0)
        {
            Log.Error("No expedition mission difficulties to pick from!");
            return;
        }

        var allDifficulties = _missionDifficulties.ToList();
        _random.Shuffle(allDifficulties);
        var difficulties = allDifficulties.Take(MissionLimit).ToList();

        while (difficulties.Count < MissionLimit)
        {
            var difficultyIndex = _random.Next(_missionDifficulties.Count);
            difficulties.Add(_missionDifficulties[difficultyIndex]);
        }

        difficulties.Sort((x, y) => Comparer<int>.Default.Compare(x.value, y.value));

        for (var i = 0; i < MissionLimit; i++)
        {
            var mission = new SalvageMissionParams
            {
                Index = board.NextIndex,
                MissionType = SalvageMissionType.Destruction,
                Seed = _random.Next(),
                Difficulty = difficulties[i].id,
            };

            board.Missions[board.NextIndex++] = mission;
        }
    }

    private void SpawnMission(SalvageMissionParams missionParams, EntityUid station, EntityUid? coordinatesDisk, string economyId)
    {
        var cancelToken = new CancellationTokenSource();
        var job = new SpawnSalvageMissionJob(
            SalvageJobTime,
            EntityManager,
            _timing,
            _logManager,
            _prototypeManager,
            _anchorable,
            _biome,
            _dungeon,
            _metaData,
            _mapSystem,
            _station, // _CS
            _shuttle, // _CS
            this, // _CS
            station,
            coordinatesDisk,
            economyId,
            missionParams,
            cancelToken.Token);

        _salvageJobs.Add((job, cancelToken));
        _salvageQueue.EnqueueJob(job);
    }

    private void OnStructureExamine(EntityUid uid, SalvageStructureComponent component, ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("salvage-expedition-structure-examine"));
    }

    // _CS: exped job handling, ghost reparenting
    // Handle exped spawn job failures gracefully - reset the console
    private void OnExpeditionSpawnComplete(ExpeditionSpawnCompleteEvent ev)
    {
        if (!_sharedExpeditionBoards.TryGetValue(ev.EconomyId, out var board))
            return;

        if (!ev.Success)
        {
            board.ActiveMission = 0;
            board.JoinableExpedition = null;
            ClearPendingClaims(board);
            if (TryComp<SalvageExpeditionDataComponent>(ev.Station, out var stationData))
            {
                stationData.ActiveMission = 0;
                stationData.Cooldown = false;
                UpdateConsoles((ev.Station, stationData));
            }
            UpdateEconomyConsoles(board.EconomyId);
            return;
        }

        if (board.ActiveMission != ev.MissionIndex || !ev.MapUid.IsValid())
            return;

        board.JoinableExpedition = ev.MapUid;

        if (!TryComp<SalvageExpeditionComponent>(ev.MapUid, out var expedition))
        {
            UpdateEconomyConsoles(board.EconomyId);
            return;
        }

        foreach (var pending in board.PendingClaims.ToArray())
        {
            if (TryJoinExistingExpedition(board, pending.Station, pending.ConsoleUid, pending.MissionIndex, ev.MapUid, expedition))
                continue;

            if (!TryComp<SalvageExpeditionDataComponent>(pending.Station, out var pendingData))
                continue;

            pendingData.ActiveMission = 0;
            pendingData.CanFinish = false;

            if (EntityManager.EntityExists(pending.ConsoleUid))
            {
                PlayDenySound((pending.ConsoleUid, Comp<SalvageExpeditionConsoleComponent>(pending.ConsoleUid)));
                _popupSystem.PopupEntity(Loc.GetString("salvage-expedition-no-valid-landing-zone"), pending.ConsoleUid, Content.Shared.Popups.PopupType.MediumCaution);
            }

            UpdateStationConsoles(pending.Station);
        }

        board.PendingClaims.Clear();
        UpdateEconomyConsoles(board.EconomyId);
    }

    // Send all ghosts (relevant for admins) back to the default map so they don't lose their stuff.
    private void OnMapTerminating(EntityUid uid, SalvageExpeditionComponent component, EntityTerminatingEvent ev)
    {
        // Ensure weather stream is force-cleared before map teardown.
        StopExpeditionWeather(uid, component);

        var ghosts = EntityQueryEnumerator<GhostComponent, TransformComponent>();
        var newCoords = new MapCoordinates(Vector2.Zero, _gameTicker.DefaultMap);
        while (ghosts.MoveNext(out var ghostUid, out _, out var xform))
        {
            if (xform.MapUid == uid)
                _transform.SetMapCoordinates(ghostUid, newCoords);
        }
    }
    // _CS End
}
