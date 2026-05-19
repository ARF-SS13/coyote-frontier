using System.Collections;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Atmos;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.EntitySystems;
using Robust.Shared.CPUJob.JobQueues;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Parallax;
using Content.Server.Procedural;
using Content.Server.Salvage.Expeditions;
using Content.Server.Salvage.Expeditions.Structure;
using Content.Shared.Atmos;
using Content.Shared.Construction.EntitySystems;
using Content.Shared.Dataset;
using Content.Shared.Gravity;
using Content.Shared.Parallax.Biomes;
using Content.Shared.Physics;
using Content.Shared.Procedural;
using Content.Shared.Procedural.Loot;
using Content.Shared.Random;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Salvage.Expeditions.Modifiers;
using Content.Shared.Shuttles.Components;
using Content.Shared.Storage;
using Robust.Shared.Collections;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Content.Server.Shuttles.Components;
// _CS Start: shared objective radar blips
using Content.Server._NF.Radar;
using Content.Server._NF.Salvage.Expeditions; // _CS
using Content.Server.Station.Components; // _CS
using Content.Server.Station.Systems; // _CS
using Content.Server.Shuttles.Systems;
using Content.Server._NF.Salvage.Expeditions.Structure; // _CS
using Content.Shared.NPC.Prototypes;
using Content.Shared._NF.Radar;
// _CS End: shared objective radar blips

namespace Content.Server.Salvage;

public sealed class SpawnSalvageMissionJob : Job<bool>
{
    private const int SharedExpeditionDungeonCount = 5;
    private const float SharedExpeditionMinClusterSpacing = 120f;
    private const float SharedExpeditionClusterPadding = 80f;
    private const float SharedExpeditionMinNonOverlapPadding = 16f;
    private const float SharedExpeditionMaxCompoundDistanceFromFirstShip = 96f;
    // _CS Start: shared cluster placement retries
    private const int SharedExpeditionPlacementMaxAttempts = 4;
    private const float SharedExpeditionPlacementRetryStep = 16f;
    // _CS End: shared cluster placement retries
    private const int SharedObjectiveMultiplier = 2;
    private static readonly float SharedLandingBufferTiles = SalvageExpeditionReservation.MinimumLandingClearanceTiles;

    private static readonly ProtoId<LocalizedDatasetPrototype> NamesDataset = "NamesBorer";

    private readonly IEntityManager _entManager;
    private readonly IGameTiming _timing;
    private readonly IPrototypeManager _prototypeManager;
    private readonly AnchorableSystem _anchorable;
    private readonly BiomeSystem _biome;
    private readonly DungeonSystem _dungeon;
    private readonly MetaDataSystem _metaData;
    private readonly SharedMapSystem _map;
    private readonly StationSystem _station; // _CS
    private readonly ShuttleSystem _shuttle; // _CS
    private readonly SalvageSystem _salvage; // _CS

    public readonly EntityUid Station;
    public readonly EntityUid? CoordinatesDisk;
    private readonly string _economyId;
    private readonly SalvageMissionParams _missionParams;

    private readonly ISawmill _sawmill;

    // _CS: Used for saving state between async job
#pragma warning disable IDE1006 // suppressing prefix warnings to reduce merge conflict area
    private EntityUid mapUid = EntityUid.Invalid;
#pragma warning restore IDE1006
    private static readonly ProtoId<SalvageDifficultyPrototype> FallbackDifficulty = "NFModerate";
    private static readonly ProtoId<SalvageDifficultyPrototype> OpenContractDifficulty = "NFExtreme";
    // _CS End

    public SpawnSalvageMissionJob(
        double maxTime,
        IEntityManager entManager,
        IGameTiming timing,
        ILogManager logManager,
        IPrototypeManager protoManager,
        AnchorableSystem anchorable,
        BiomeSystem biome,
        DungeonSystem dungeon,
        MetaDataSystem metaData,
        SharedMapSystem map,
        StationSystem stationSystem, // _CS
        ShuttleSystem shuttleSystem, // _CS
        SalvageSystem salvageSystem, // _CS
        EntityUid station,
        EntityUid? coordinatesDisk,
        string economyId,
        SalvageMissionParams missionParams,
        CancellationToken cancellation = default) : base(maxTime, cancellation)
    {
        _entManager = entManager;
        _timing = timing;
        _prototypeManager = protoManager;
        _anchorable = anchorable;
        _biome = biome;
        _dungeon = dungeon;
        _metaData = metaData;
        _map = map;
        _station = stationSystem; // _CS
        _shuttle = shuttleSystem; // _CS
        _salvage = salvageSystem; // _CS
        Station = station;
        CoordinatesDisk = coordinatesDisk;
        _economyId = economyId;
        _missionParams = missionParams;
        _sawmill = logManager.GetSawmill("salvage_job");
#if !DEBUG
        _sawmill.Level = LogLevel.Info;
#endif
    }

    protected override async Task<bool> Process()
    {
        // _CS: gracefully handle expedition failures
        var success = true;
        Exception? error = null;
        try
        {
            success = await InternalProcess();
        }
        catch (Exception e)
        {
            success = false;
            error = e;
        }
        finally
        {
            ExpeditionSpawnCompleteEvent ev = new(Station, success, _missionParams.Index, mapUid, _economyId);
            _entManager.EventBus.RaiseEvent(EventSource.Local, ev);
            if (error != null)
                _sawmill.Error("salvage", $"Expedition generation failed with exception: {error}");
            if (!success)
            {
                // Invalidate station, expedition cancellation will be handled by task handler
                if (_entManager.TryGetComponent<SalvageExpeditionComponent>(mapUid, out var salvage))
                    salvage.Station = EntityUid.Invalid;

                _entManager.QueueDeleteEntity(mapUid);
            }
        }
        return success;
        // _CS End: gracefully handle expedition failures
    }

    private async Task<bool> InternalProcess() // _CS: make process an internal function (for a try block indenting an entire), add "out EntityUid mapUid" param
    {
        _sawmill.Debug("salvage", $"Spawning salvage mission with seed {_missionParams.Seed}");
        mapUid = _map.CreateMap(out var mapId, runMapInit: false); // _CS: remove var
        MetaDataComponent? metadata = null;
        var grid = _entManager.EnsureComponent<MapGridComponent>(mapUid);
        var random = new Random(_missionParams.Seed);
        var destComp = _entManager.AddComponent<FTLDestinationComponent>(mapUid);
        destComp.BeaconsOnly = true;
        destComp.RequireCoordinateDisk = true;
        destComp.Enabled = true;
        _metaData.SetEntityName(
            mapUid,
            _entManager.System<SharedSalvageSystem>().GetFTLName(_prototypeManager.Index(NamesDataset), _missionParams.Seed));
        _entManager.AddComponent<FTLBeaconComponent>(mapUid);

        // Saving the mission mapUid to a CD is made optional, in case one is somehow made in a process without a CD entity
        if (CoordinatesDisk.HasValue)
        {
            var cd = _entManager.EnsureComponent<ShuttleDestinationCoordinatesComponent>(CoordinatesDisk.Value);
            cd.Destination = mapUid;
            _entManager.Dirty(CoordinatesDisk.Value, cd);
        }

        // Setup mission configs
        // As we go through the config the rating will deplete so we'll go for most important to least important.
        // _CS: custom difficulty
        if (_missionParams.OpenContract)
            _missionParams.Difficulty = OpenContractDifficulty;

        if (!_prototypeManager.TryIndex<SalvageDifficultyPrototype>(_missionParams.Difficulty, out var difficultyProto))
            difficultyProto = _prototypeManager.Index<SalvageDifficultyPrototype>(FallbackDifficulty);
        // _CS End

        var mission = _entManager.System<SharedSalvageSystem>()
            .GetMission(_missionParams.MissionType, difficultyProto, _missionParams.Seed); // _CS: add MissionType

        var missionBiome = _prototypeManager.Index<SalvageBiomeModPrototype>(mission.Biome);

        if (missionBiome.BiomePrototype != null)
        {
            var biome = _entManager.AddComponent<BiomeComponent>(mapUid);
            var biomeSystem = _entManager.System<BiomeSystem>();
            biomeSystem.SetTemplate(mapUid, biome, _prototypeManager.Index<BiomeTemplatePrototype>(missionBiome.BiomePrototype));
            biomeSystem.SetSeed(mapUid, biome, mission.Seed);
            _entManager.Dirty(mapUid, biome);

            // Gravity
            var gravity = _entManager.EnsureComponent<GravityComponent>(mapUid);
            gravity.Enabled = true;
            _entManager.Dirty(mapUid, gravity, metadata);

            // Atmos
            var air = _prototypeManager.Index<SalvageAirMod>(mission.Air);
            // copy into a new array since the yml deserialization discards the fixed length
            var moles = new float[Atmospherics.AdjustedNumberOfGases];
            air.Gases.CopyTo(moles, 0);
            var atmos = _entManager.EnsureComponent<MapAtmosphereComponent>(mapUid);
            _entManager.System<AtmosphereSystem>().SetMapSpace(mapUid, air.Space, atmos);
            _entManager.System<AtmosphereSystem>().SetMapGasMixture(mapUid, new GasMixture(moles, mission.Temperature), atmos);

            if (mission.Color != null)
            {
                var lighting = _entManager.EnsureComponent<MapLightComponent>(mapUid);
                lighting.AmbientLightColor = mission.Color.Value;
                _entManager.Dirty(mapUid, lighting);
            }
        }

        // Setup expedition component BEFORE map init so that MapInitEvent handlers can access it
        var expedition = _entManager.AddComponent<SalvageExpeditionComponent>(mapUid);
        expedition.EconomyId = _economyId;
        expedition.Station = Station;
        expedition.EndTime = _timing.CurTime + mission.Duration;
        expedition.MissionParams = _missionParams;

        _map.InitializeMap(mapId);
        _map.SetPaused(mapUid, true);

        var landingPadRadius = 4; // _CS: 24<4 - using this as a margin (4-16), not a radius
        var minDungeonOffset = landingPadRadius + 4;

        // We'll use the dungeon rotation as the spawn angle
        var dungeonRotation = _dungeon.GetDungeonRotation(_missionParams.Seed);

        var maxDungeonOffset = minDungeonOffset + 12;
        var dungeonOffsetDistance = minDungeonOffset + (maxDungeonOffset - minDungeonOffset) * random.NextFloat();
        var dungeonOffset = new Vector2(0f, dungeonOffsetDistance);
        dungeonOffset = dungeonRotation.RotateVec(dungeonOffset);
        var dungeonMod = _prototypeManager.Index<SalvageDungeonModPrototype>(mission.Dungeon);
        var stationData = _entManager.GetComponent<StationDataComponent>(Station);

        // Get ship bounding box relative to largest grid coords.
        var shuttleUid = _station.GetLargestGrid(stationData);
        Box2 shuttleBox = new Box2();

        if (shuttleUid is { Valid: true } vesselUid &&
            _entManager.TryGetComponent<MapGridComponent>(vesselUid, out var gridComp))
        {
            shuttleBox = gridComp.LocalAABB;
        }

        List<Dungeon> dungeons;

        if (_missionParams.OpenContract)
        {
            var sharedResult = await GenerateSharedDungeonsAsync(dungeonMod.Proto, mapUid, grid, dungeonOffset, shuttleBox, _missionParams.Seed);
            dungeons = sharedResult.dungeons;
            expedition.SharedDungeonCenters = sharedResult.centers;
        }
        else
        {
            var dungeonConfig = GetDungeonConfigForMission(dungeonMod.Proto);
            dungeons = await WaitAsyncTask(_dungeon.GenerateDungeonAsync(dungeonConfig, dungeonMod.Proto, mapUid, grid, (Vector2i)dungeonOffset,
                _missionParams.Seed));
        }

        var dungeon = MergeDungeons(dungeons);

        // Aborty
        if (dungeon.Rooms.Count == 0)
        {
            return false;
        }

        if (_missionParams.OpenContract)
            RepairSharedDungeonVoidTiles(dungeon, mapUid, grid);

        expedition.DungeonLocation = dungeonOffset;
        // Only reserve room and corridor tiles, not entrances - those need to be set by PostGen layers
        var reservedTiles = new HashSet<Vector2i>(dungeon.RoomTiles);
        reservedTiles.UnionWith(dungeon.RoomExteriorTiles);
        reservedTiles.UnionWith(dungeon.CorridorTiles);
        reservedTiles.UnionWith(dungeon.CorridorExteriorTiles);
        expedition.ReservedTiles = reservedTiles;

        // _CS: map generation and offset
        // _CS Start map generation

        // Get map bounding box
        Box2 dungeonBox = new Box2(dungeonOffset, dungeonOffset);
        foreach (var tile in dungeon.AllTiles)
        {
            dungeonBox = dungeonBox.ExtendToContain(tile);
        }

        // Shared expeditions can merge several distant dungeon clusters.
        // Use the first generated dungeon as the landing reference so shuttle placement
        // stays near a playable cluster instead of the full merged AABB center.
        var landingReference = _missionParams.OpenContract && dungeons.Count > 0
            ? dungeons[0]
            : dungeon;

        Box2 landingBox = new Box2(dungeonOffset, dungeonOffset);
        foreach (var tile in landingReference.AllTiles)
        {
            landingBox = landingBox.ExtendToContain(tile);
        }

        expedition.DungeonBounds = dungeonBox;
        expedition.ParticipantStations.Add(Station);

        // Offset ship spawn point from bounding boxes
        float sin = (float)Math.Sin(dungeonRotation);
        float cos = (float)Math.Cos(dungeonRotation);
        Vector2 dungeonProjection = new Vector2(landingBox.Width * -sin / 2, landingBox.Height * cos / 2); // Project boxes to get relevant offset for dungeon rotation.
        Vector2 shuttleProjection = new Vector2(shuttleBox.Width * -sin / 2, shuttleBox.Height * cos / 2); // Note: sine is negative because of CCW rotation (starting north, then west)

        Vector2 coords;
        if (_missionParams.OpenContract)
        {
            coords = FindSharedInitialLandingOrigin(landingBox, shuttleBox, expedition.DungeonLocation, expedition.ReservedTiles);

            var landingCenter = coords + shuttleBox.Center;
            var sharedLandingVector = landingCenter - expedition.DungeonLocation;
            expedition.SharedLandingRadius = sharedLandingVector.Length();
            expedition.SharedLandingAngle = MathF.Atan2(sharedLandingVector.Y, sharedLandingVector.X);
        }
        else
        {
            // Preserve existing private/standard expedition placement behavior.
            Vector2 scaledProjection = dungeonProjection * 1.5f + new Vector2(cos, sin) * 8f;
            coords = landingBox.Center - scaledProjection - shuttleProjection - shuttleBox.Center;
        }

        coords = coords.Rounded(); // Ensure grid is aligned to map coords
        expedition.ReservedLandingZones.Add(SalvageExpeditionReservation.GetLandingZone(shuttleBox, coords, SharedLandingBufferTiles));

        // _CS Start: exclude landing zones from map atmosphere
        var atmosExclusion = _entManager.AddComponent<ExpeditionAtmosphereExclusionComponent>(mapUid);
        foreach (var zone in expedition.ReservedLandingZones)
        {
            atmosExclusion.ExcludedZones.Add(zone);
        }
        // _CS End: exclude landing zones from map atmosphere

        // List<Vector2i> reservedTiles = new();

        // foreach (var tile in _map.GetTilesIntersecting(mapUid, grid, new Circle(Vector2.Zero, landingPadRadius), false))
        // {
        //     if (!_biome.TryGetBiomeTile(mapUid, grid, tile.GridIndices, out _))
        //         continue;

        //     reservedTiles.Add(tile.GridIndices);
        // }
        // _CS End map generation
        // _CS End: map generation and offset

        // _CS: mission setup
        switch (_missionParams.MissionType)
        {
            case SalvageMissionType.Destruction:
                await SetupStructure(mission, dungeon, grid, random);
                break;
            case SalvageMissionType.Elimination:
                await SetupElimination(mission, dungeon, grid, random);
                break;
            default:
                _sawmill.Warning($"No setup function for salvage mission type {_missionParams.MissionType}!");
                break;
        }
        // _CS End: mission setup

        var budgetEntries = new List<IBudgetEntry>();

        /*
         * GUARANTEED LOOT
         */

        // We'll always add this loot if possible
        // mainly used for ore layers.
        foreach (var lootProto in _prototypeManager.EnumeratePrototypes<SalvageLootPrototype>())
        {
            if (!lootProto.Guaranteed)
                continue;

            try
            {
                await SpawnDungeonLoot(lootProto, mapUid);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to spawn guaranteed loot {lootProto.ID}: {e}");
            }
        }

        // Handle boss loot (when relevant).

        // Handle mob loot.

        // Handle remaining loot

        /*
         * MOB SPAWNS
         */

        var mobBudget = difficultyProto.MobBudget;
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var randomSystem = _entManager.System<RandomSystem>();

        foreach (var entry in faction.MobGroups)
        {
            budgetEntries.Add(entry);
        }

        var probSum = budgetEntries.Sum(x => x.Prob);

        while (mobBudget > 0f)
        {
            var entry = randomSystem.GetBudgetEntry(ref mobBudget, ref probSum, budgetEntries, random);
            if (entry == null)
                break;

            try
            {
                await SpawnRandomEntry((mapUid, grid), entry, dungeon, random);
            }
            catch (Exception e)
            {
                _sawmill.Error($"Failed to spawn mobs for {entry.Proto}: {e}");
            }
        }

        // _CS: difficulty-based loot tables
        var lootTable = difficultyProto.LootTable ?? SharedSalvageSystem.ExpeditionsLootProto;
        var allLoot = _prototypeManager.Index<SalvageLootPrototype>(lootTable);
        // _CS End
        var lootBudget = difficultyProto.LootBudget;

        foreach (var rule in allLoot.LootRules)
        {
            switch (rule)
            {
                case RandomSpawnsLoot randomLoot:
                    budgetEntries.Clear();

                    foreach (var entry in randomLoot.Entries)
                    {
                        budgetEntries.Add(entry);
                    }

                    probSum = budgetEntries.Sum(x => x.Prob);

                    while (lootBudget > 0f)
                    {
                        var entry = randomSystem.GetBudgetEntry(ref lootBudget, ref probSum, budgetEntries, random);
                        if (entry == null)
                            break;

                        _sawmill.Debug($"Spawning dungeon loot {entry.Proto}");
                        await SpawnRandomEntry((mapUid, grid), entry, dungeon, random);
                    }
                    break;
                default:
                    throw new NotImplementedException();
            }
        }

        // _CS: delay ship FTL
        if (shuttleUid is { Valid: true })
        {
            var shuttle = _entManager.GetComponent<ShuttleComponent>(shuttleUid.Value);
            _shuttle.FTLToCoordinates(shuttleUid.Value, shuttle, new EntityCoordinates(mapUid, coords), 0f, 5.5f, _salvage.TravelTime);
        }
        // _CS End

        return true;
    }

    private DungeonConfig GetDungeonConfigForMission(ProtoId<DungeonConfigPrototype> dungeonProto)
    {
        return _prototypeManager.Index(dungeonProto);
    }

    private DungeonConfig GetSharedDungeonConfigForMission(ProtoId<DungeonConfigPrototype> dungeonProto)
    {
        var baseConfig = _prototypeManager.Index(dungeonProto);

        return new DungeonConfig
        {
            Layers = baseConfig.Layers,
            ReserveTiles = true,
            MinCount = 1,
            MaxCount = 1,
            MinOffset = 0,
            MaxOffset = 0,
        };
    }

    private async Task<(List<Dungeon> dungeons, List<Vector2> centers)> GenerateSharedDungeonsAsync(ProtoId<DungeonConfigPrototype> dungeonProto, EntityUid mapUid, MapGridComponent grid, Vector2 dungeonOffset, Box2 shuttleBox, int seed)
    {
        var config = GetSharedDungeonConfigForMission(dungeonProto);
        var sharedDungeons = new List<Dungeon>();
        var clusterCenters = new List<Vector2>();
        var clusterHalfExtents = new List<float>();
        var origin = (Vector2i)dungeonOffset;

        var firstBatch = await WaitAsyncTask(_dungeon.GenerateDungeonAsync(config, dungeonProto, mapUid, grid, origin, seed));
        if (firstBatch.Count == 0)
            return (sharedDungeons, clusterCenters);

        sharedDungeons.AddRange(firstBatch);

        var firstBounds = GetDungeonBounds(firstBatch[0], origin);
        // _CS Start: use actual generated bounds center/radius for overlap checks
        clusterCenters.Add(firstBounds.Center);

        var firstHalfExtent = GetSharedClusterRadius(firstBounds);
        var estimatedHalfExtent = firstHalfExtent;
        // _CS End: use actual generated bounds center/radius for overlap checks
        var minNonOverlappingSpacing = firstHalfExtent * 2f + SharedExpeditionMinNonOverlapPadding;
        clusterHalfExtents.Add(firstHalfExtent);
        var preferredSpacing = MathF.Max(SharedExpeditionMinClusterSpacing,
            MathF.Max(firstBounds.Width, firstBounds.Height) + SharedExpeditionClusterPadding);
        var estimatedLandingRadius = EstimateSharedInitialLandingRadius(firstBounds, shuttleBox, dungeonOffset);
        var maxAllowedSpacing = MathF.Max(0f, SharedExpeditionMaxCompoundDistanceFromFirstShip - estimatedLandingRadius);
        var cappedSpacing = MathF.Min(preferredSpacing, maxAllowedSpacing);
        var spacing = MathF.Max(minNonOverlappingSpacing, cappedSpacing);

        if (spacing > maxAllowedSpacing + 0.01f)
        {
            _sawmill.Warning($"Shared expedition spacing had to exceed distance cap to avoid overlap. " +
                             $"Spacing={spacing:0.##}, maxAllowed={maxAllowedSpacing:0.##}, estimatedLandingRadius={estimatedLandingRadius:0.##}");
        }

        var baseAngle = (float)_dungeon.GetDungeonRotation(seed);
        // _CS Start: cache probe results per (seed, origin) to avoid redundant temporary dungeon generations
        var probeBoundsCache = new Dictionary<(int seed, Vector2i origin), Box2?>();
        // _CS End: cache probe results

        for (var i = 1; i < SharedExpeditionDungeonCount; i++)
        {
            var theta = baseAngle + MathF.Tau * (i - 1) / (SharedExpeditionDungeonCount - 1);
            var direction = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
            var localSeed = seed + i * 9973;
            // _CS Start: probe candidate cluster bounds and retry outward until overlap-free
            var radialDistance = spacing;
            var positionedOrigin = ((dungeonOffset + direction * radialDistance).Floored());
            var probeHalfExtent = estimatedHalfExtent;

            for (var attempt = 0; attempt < SharedExpeditionPlacementMaxAttempts; attempt++)
            {
                var resolvedCenter = ResolveSequentialSharedClusterCenter(
                    dungeonOffset,
                    direction,
                    radialDistance,
                    estimatedHalfExtent,
                    clusterCenters,
                    clusterHalfExtents,
                    SharedExpeditionMinNonOverlapPadding);

                positionedOrigin = resolvedCenter.Floored();

                // _CS Start: cache by (seed, origin) - skip probe if already computed
                var probeKey = (localSeed, positionedOrigin);
                if (!probeBoundsCache.TryGetValue(probeKey, out var probeBounds))
                {
                    probeBounds = await ProbeSharedClusterBoundsAsync(config, dungeonProto, positionedOrigin, localSeed);
                    probeBoundsCache[probeKey] = probeBounds;
                }
                // _CS End: cache by (seed, origin)

                // _CS Start: exponential outward step on failure to reduce probe count
                var stepMultiplier = attempt + 1;
                // _CS End: exponential outward step

                if (probeBounds == null)
                {
                    radialDistance += SharedExpeditionPlacementRetryStep * stepMultiplier;
                    continue;
                }

                probeHalfExtent = GetSharedClusterRadius(probeBounds.Value);

                if (!IntersectsExistingSharedClusters(
                        probeBounds.Value.Center,
                        probeHalfExtent,
                        clusterCenters,
                        clusterHalfExtents,
                        SharedExpeditionMinNonOverlapPadding))
                {
                    break;
                }

                radialDistance += SharedExpeditionPlacementRetryStep * stepMultiplier;
            }
            // _CS End: probe candidate cluster bounds and retry outward until overlap-free

            var batch = await WaitAsyncTask(_dungeon.GenerateDungeonAsync(config, dungeonProto, mapUid, grid, positionedOrigin, localSeed));
            sharedDungeons.AddRange(batch);

            // _CS Start: use final generated bounds center, not origin, for overlap tracking (fixes double-add)
            if (batch.Count > 0)
            {
                var bounds = GetDungeonBounds(batch[0], positionedOrigin);
                clusterCenters.Add(bounds.Center);
                var halfExtent = GetSharedClusterRadius(bounds);
                clusterHalfExtents.Add(halfExtent);
                estimatedHalfExtent = MathF.Max(estimatedHalfExtent, halfExtent);
            }
            else
            {
                clusterCenters.Add(new Vector2(positionedOrigin.X, positionedOrigin.Y));
                clusterHalfExtents.Add(estimatedHalfExtent);
            }
            // _CS End: use final generated bounds center
        }

        return (sharedDungeons, clusterCenters);
    }

    // _CS Start: shared cluster overlap probing helpers
    private async Task<Box2?> ProbeSharedClusterBoundsAsync(
        DungeonConfig config,
        ProtoId<DungeonConfigPrototype> dungeonProto,
        Vector2i probeOrigin,
        int seed)
    {
        var probeMapUid = _map.CreateMap(out var probeMapId, runMapInit: false);
        var probeGrid = _entManager.EnsureComponent<MapGridComponent>(probeMapUid);

        try
        {
            _map.InitializeMap(probeMapId);
            _map.SetPaused(probeMapUid, true);

            var probeBatch = await WaitAsyncTask(_dungeon.GenerateDungeonAsync(config, dungeonProto, probeMapUid, probeGrid, probeOrigin, seed));
            if (probeBatch.Count == 0)
                return null;

            return GetDungeonBounds(probeBatch[0], probeOrigin);
        }
        finally
        {
            _entManager.QueueDeleteEntity(probeMapUid);
        }
    }

    private static bool IntersectsExistingSharedClusters(
        Vector2 candidateCenter,
        float candidateHalfExtent,
        IReadOnlyList<Vector2> existingCenters,
        IReadOnlyList<float> existingHalfExtents,
        float extraPadding)
    {
        for (var i = 0; i < existingCenters.Count; i++)
        {
            var required = candidateHalfExtent + existingHalfExtents[i] + extraPadding;
            var actual = (candidateCenter - existingCenters[i]).Length();
            if (actual < required)
                return true;
        }

        return false;
    }

    private static float GetSharedClusterRadius(Box2 bounds)
    {
        return bounds.Size.Length() / 2f;
    }
    // _CS End: shared cluster overlap probing helpers

    private static Vector2 ResolveSequentialSharedClusterCenter(
        Vector2 anchor,
        Vector2 direction,
        float baseSpacing,
        float candidateHalfExtent,
        IReadOnlyList<Vector2> existingCenters,
        IReadOnlyList<float> existingHalfExtents,
        float extraPadding)
    {
        var norm = direction.LengthSquared() > 0.0001f
            ? Vector2.Normalize(direction)
            : new Vector2(1f, 0f);

        var radialDistance = baseSpacing;

        for (var pass = 0; pass < 16; pass++)
        {
            var changed = false;
            var candidateCenter = anchor + norm * radialDistance;

            for (var i = 0; i < existingCenters.Count; i++)
            {
                var required = candidateHalfExtent + existingHalfExtents[i] + extraPadding;
                var actual = (candidateCenter - existingCenters[i]).Length();
                if (actual >= required)
                    continue;

                radialDistance += required - actual;
                changed = true;
            }

            if (!changed)
                break;
        }

        return anchor + norm * radialDistance;
    }

    private static float EstimateSharedInitialLandingRadius(Box2 dungeonBox, Box2 shuttleBox, Vector2 dungeonLocation)
    {
        var outwardDir = dungeonLocation.LengthSquared() > 0.001f
            ? Vector2.Normalize(-dungeonLocation)
            : new Vector2(0f, -1f);

        var dungeonHalfExtents = dungeonBox.Size / 2f;
        var shuttleHalfExtents = shuttleBox.Size / 2f;
        var dungeonExtentAlongDir = MathF.Abs(outwardDir.X) * dungeonHalfExtents.X + MathF.Abs(outwardDir.Y) * dungeonHalfExtents.Y;
        var shuttleExtentAlongDir = MathF.Abs(outwardDir.X) * shuttleHalfExtents.X + MathF.Abs(outwardDir.Y) * shuttleHalfExtents.Y;
        return dungeonExtentAlongDir + SharedLandingBufferTiles + shuttleExtentAlongDir;
    }

    private static Box2 GetDungeonBounds(Dungeon dungeon, Vector2i fallback)
    {
        var min = new Vector2(fallback.X, fallback.Y);
        var bounds = new Box2(min, min + Vector2.One);

        foreach (var tile in dungeon.AllTiles)
        {
            bounds = bounds.ExtendToContain(tile);
        }

        return bounds;
    }

    private void ConfigureObjectiveNpcSpawner(EntityUid objective, ProtoId<SalvageFactionPrototype> factionId)
    {
        if (!_prototypeManager.TryIndex(factionId, out var faction))
            return;

        if (!faction.Configs.TryGetValue("DefenseStructure", out var structureId))
            return;

        var spawner = _entManager.EnsureComponent<SalvageObjectiveNpcSpawnerComponent>(objective);

        switch (structureId)
        {
            case "AberrantFleshDigestiveSack":
                spawner.NearbyFactions = new() { "AberrantFleshExpeditionNF" };
                spawner.SpawnPrototypes = new()
                {
                    "SpawnMobAberrantFleshExpeditions",
                    "SpawnMobAberrantFleshNewbornExpeditions",
                    "MobHorrorExpeditions",
                };
                break;
            case "RogueAiNode":
                spawner.NearbyFactions = new() { "SiliconsExpeditionNF" };
                spawner.SpawnPrototypes = new()
                {
                    "MobRogueSiliconScrap",
                    "SpawnMobRogueDronesT1",
                    "MobRogueSiliconHerder",
                    "MobRogueSiliconHunter",
                    "MobRogueSiliconCatcher",
                    "MobRogueSiliconTesla",
                    "MobRogueSiliconScrapFlayer",
                    "MobRogueSiliconBoss",
                    "MobRogueSiliconGuardian",
                };
                break;
            case "NFZombiePile":
                spawner.NearbyFactions = new() { "Zombie" };
                spawner.SpawnPrototypes = new()
                {
                    "NFSpawnMobZombie",
                    "NFSpawnMobZombieSpecial",
                    "NFSpawnMobZombieRandom",
                };
                break;
            case "CybersunDataMiner":
                spawner.NearbyFactions = new() { "NFSyndicate" };
                spawner.SpawnPrototypes = new()
                {
                    "SpawnMobSyndicateNavalDeckhand",
                    "SpawnMobSyndicateNavalEngineer",
                    "SpawnMobSyndicateNavalMedic",
                    "SpawnMobSyndicateNavalOperator",
                    "SpawnMobSyndicateNavalCaptain",
                };
                break;
            case "XenoWardingTower":
                spawner.NearbyFactions = new() { "Xeno" };
                spawner.SpawnPrototypes = new()
                {
                    "NFMobXeno",
                    "NFMobXenoDrone",
                    "NFMobXenoPraetorian",
                    "NFMobXenoRavager",
                    "NFMobXenoRunner",
                    "NFMobXenoSpitter",
                };
                break;
            case "MercenaryCounterfeitCache":
                spawner.NearbyFactions = new() { "MercenariesExpeditionNF" };
                spawner.SpawnPrototypes = new()
                {
                    "MobMercenarySoldierKnife",
                    "MobMercenarySoldierPistol",
                    "MobMercenarySoldierNovalite",
                    "MobMercenaryBreacherMachete",
                    "MobMercenaryBreacherShotgun",
                };
                break;
            // _CS Start
            case "BloodCollector":
                spawner.NearbyFactions = new() { "BloodCultNF" };
                spawner.SpawnPrototypes = new()
                {
                    "SpawnMobBloodCultistZealotMelee",
                    "SpawnMobBloodCultLeech",
                    "SpawnMobBloodCultistZealotRanged",
                    "SpawnMobBloodCultistCaster",
                    "SpawnMobBloodCultistAcolyte",
                    "SpawnMobBloodCultistPriest",
                    "SpawnMobBloodCultistJanitor",
                    "MobBloodCultistAscended",
                };
                break;
        }
    }

    private static Dungeon MergeDungeons(IReadOnlyList<Dungeon> dungeons)
    {
        if (dungeons.Count == 0)
            return Dungeon.Empty;

        if (dungeons.Count == 1)
            return dungeons[0];

        var merged = new Dungeon();

        foreach (var source in dungeons)
        {
            foreach (var room in source.Rooms)
            {
                merged.AddRoom(room);
            }

            merged.CorridorTiles.UnionWith(source.CorridorTiles);
            merged.CorridorExteriorTiles.UnionWith(source.CorridorExteriorTiles);
            merged.Entrances.UnionWith(source.Entrances);
        }

        merged.RefreshAllTiles();
        return merged;
    }

    private static bool IntersectsReservedDungeonTiles(HashSet<Vector2i> reservedTiles, Box2 area, float minimumClearance)
    {
        var checkArea = area.Enlarged(minimumClearance);
        var minX = (int)MathF.Floor(checkArea.Left);
        var minY = (int)MathF.Floor(checkArea.Bottom);
        var maxX = (int)MathF.Ceiling(checkArea.Right) - 1;
        var maxY = (int)MathF.Ceiling(checkArea.Top) - 1;

        for (var x = minX; x <= maxX; x++)
        {
            for (var y = minY; y <= maxY; y++)
            {
                if (!reservedTiles.Contains(new Vector2i(x, y)))
                    continue;

                var tileMin = new Vector2(x, y);
                var tileBox = new Box2(tileMin, tileMin + Vector2.One);
                if (SalvageExpeditionReservation.IsWithinClearance(area, tileBox, minimumClearance))
                    return true;
            }
        }

        return false;
    }

    private static Vector2 FindSharedInitialLandingOrigin(Box2 dungeonBox, Box2 shuttleBox, Vector2 dungeonLocation, HashSet<Vector2i> reservedTiles)
    {
        var center = dungeonBox.Center;
        var outwardDir = dungeonLocation.LengthSquared() > 0.001f
            ? Vector2.Normalize(-dungeonLocation)
            : new Vector2(0f, -1f);

        var dungeonHalfExtents = dungeonBox.Size / 2f;
        var shuttleHalfExtents = shuttleBox.Size / 2f;
        var dungeonExtentAlongDir = MathF.Abs(outwardDir.X) * dungeonHalfExtents.X + MathF.Abs(outwardDir.Y) * dungeonHalfExtents.Y;
        var shuttleExtentAlongDir = MathF.Abs(outwardDir.X) * shuttleHalfExtents.X + MathF.Abs(outwardDir.Y) * shuttleHalfExtents.Y;
        var baseRadius = dungeonExtentAlongDir + SharedLandingBufferTiles + shuttleExtentAlongDir;
        var baseAngle = MathF.Atan2(outwardDir.Y, outwardDir.X);

        ReadOnlySpan<float> angleOffsets =
        [
            0f,
            MathF.PI / 12f,
            -MathF.PI / 12f,
            MathF.PI / 6f,
            -MathF.PI / 6f,
            MathF.PI / 4f,
            -MathF.PI / 4f,
            MathF.PI / 3f,
            -MathF.PI / 3f,
        ];

        for (var ring = 0; ring < 8; ring++)
        {
            var radius = baseRadius + ring * 2f;
            for (var i = 0; i < angleOffsets.Length; i++)
            {
                var theta = baseAngle + angleOffsets[i];
                var dir = new Vector2(MathF.Cos(theta), MathF.Sin(theta));
                var landingCenter = center + dir * radius;
                var origin = (landingCenter - shuttleBox.Center).Rounded();
                var shuttleFootprint = SalvageExpeditionReservation.GetShuttleFootprint(shuttleBox, origin);

                if (IntersectsReservedDungeonTiles(reservedTiles, shuttleFootprint, SharedLandingBufferTiles))
                    continue;

                return origin;
            }
        }

        var fallbackCenter = center + outwardDir * baseRadius;
        return (fallbackCenter - shuttleBox.Center).Rounded();
    }

    private void RepairSharedDungeonVoidTiles(Dungeon dungeon, EntityUid gridUid, MapGridComponent grid)
    {
        var updates = new List<(Vector2i, Tile)>();
        Tile? fallbackTile = null;

        foreach (var tilePos in dungeon.AllTiles)
        {
            if (!_map.TryGetTileRef(gridUid, grid, tilePos, out var tileRef) || tileRef.Tile.IsEmpty)
                continue;

            fallbackTile = tileRef.Tile;
            break;
        }

        if (fallbackTile == null)
            return;

        foreach (var tilePos in dungeon.AllTiles)
        {
            if (_map.TryGetTileRef(gridUid, grid, tilePos, out var tileRef) && !tileRef.Tile.IsEmpty)
                continue;

            var replacement = fallbackTile.Value;

            var left = new Vector2i(tilePos.X - 1, tilePos.Y);
            if (dungeon.AllTiles.Contains(left) &&
                _map.TryGetTileRef(gridUid, grid, left, out var leftTileRef) &&
                !leftTileRef.Tile.IsEmpty)
            {
                replacement = leftTileRef.Tile;
            }
            else
            {
                var right = new Vector2i(tilePos.X + 1, tilePos.Y);
                if (dungeon.AllTiles.Contains(right) &&
                    _map.TryGetTileRef(gridUid, grid, right, out var rightTileRef) &&
                    !rightTileRef.Tile.IsEmpty)
                {
                    replacement = rightTileRef.Tile;
                }
                else
                {
                    var down = new Vector2i(tilePos.X, tilePos.Y - 1);
                    if (dungeon.AllTiles.Contains(down) &&
                        _map.TryGetTileRef(gridUid, grid, down, out var downTileRef) &&
                        !downTileRef.Tile.IsEmpty)
                    {
                        replacement = downTileRef.Tile;
                    }
                    else
                    {
                        var up = new Vector2i(tilePos.X, tilePos.Y + 1);
                        if (dungeon.AllTiles.Contains(up) &&
                            _map.TryGetTileRef(gridUid, grid, up, out var upTileRef) &&
                            !upTileRef.Tile.IsEmpty)
                        {
                            replacement = upTileRef.Tile;
                        }
                    }
                }
            }

            updates.Add((tilePos, replacement));
        }

        if (updates.Count > 0)
            _map.SetTiles(gridUid, grid, updates);
    }

    private async Task SpawnRandomEntry(Entity<MapGridComponent> grid, IBudgetEntry entry, Dungeon dungeon, Random random)
    {
        await SuspendIfOutOfTime();

        var availableRooms = new ValueList<DungeonRoom>(dungeon.Rooms);
        var availableTiles = new List<Vector2i>();

        while (availableRooms.Count > 0)
        {
            availableTiles.Clear();
            var roomIndex = random.Next(availableRooms.Count);
            var room = availableRooms.RemoveSwap(roomIndex);
            availableTiles.AddRange(room.Tiles);

            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree((mapUid, grid), tile, (int)CollisionGroup.MachineLayer,
                        (int)CollisionGroup.MachineLayer))
                {
                    continue;
                }

                var uid = _entManager.SpawnAtPosition(entry.Proto, _map.GridTileToLocal(grid, grid, tile));
                _entManager.RemoveComponent<GhostRoleComponent>(uid);
                _entManager.RemoveComponent<GhostTakeoverAvailableComponent>(uid);
                return;
            }
        }

        // oh noooooooooooo
    }

    private async Task SpawnDungeonLoot(SalvageLootPrototype loot, EntityUid gridUid)
    {
        for (var i = 0; i < loot.LootRules.Count; i++)
        {
            var rule = loot.LootRules[i];

            switch (rule)
            {
                case BiomeMarkerLoot biomeLoot:
                    {
                        if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
                        {
                            _biome.AddMarkerLayer(gridUid, biome, biomeLoot.Prototype);
                        }
                    }
                    break;
                case BiomeTemplateLoot biomeLoot:
                    {
                        if (_entManager.TryGetComponent<BiomeComponent>(gridUid, out var biome))
                        {
                            _biome.AddTemplate(gridUid, biome, "Loot", _prototypeManager.Index<BiomeTemplatePrototype>(biomeLoot.Prototype), i);
                        }
                    }
                    break;
            }
        }
    }

    // _CS: mission-specific setup functions
    private async Task SetupStructure(
        SalvageMission mission,
        Dungeon dungeon,
        MapGridComponent grid,
        Random random)
    {
        await SuspendIfOutOfTime();

        var structureComp = _entManager.EnsureComponent<SalvageDestructionExpeditionComponent>(mapUid);
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var difficulty = _prototypeManager.Index(mission.Difficulty);
        var objectiveTarget = Math.Max(1,
            difficulty.DestructionStructures * (_missionParams.OpenContract ? SharedObjectiveMultiplier : 1));

        var shaggy = faction.Configs["DefenseStructure"];

        var availableRooms = new ValueList<DungeonRoom>(dungeon.Rooms);
        var availableTiles = new List<Vector2i>();

        while (availableRooms.Count > 0 && structureComp.Structures.Count < objectiveTarget)
        {
            availableTiles.Clear();
            var roomIndex = random.Next(availableRooms.Count);
            var room = availableRooms.RemoveSwap(roomIndex);
            availableTiles.AddRange(room.Tiles);

            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree((mapUid, grid), tile, (int)CollisionGroup.MachineLayer,
                        (int)CollisionGroup.MachineLayer))
                {
                    continue;
                }

                var uid = _entManager.SpawnEntity(shaggy, _map.GridTileToLocal(mapUid, grid, tile));
                ConfigureObjectiveNpcSpawner(uid, mission.Faction);
                _entManager.AddComponent<SalvageStructureComponent>(uid);

                // _CS Start: shared objective radar blips
                if (_missionParams.OpenContract)
                {
                    var blip = _entManager.EnsureComponent<RadarBlipComponent>(uid);
                    blip.RadarColor = Color.Gold;
                    blip.HighlightedRadarColor = Color.Yellow;
                    blip.Scale = 3f;
                    blip.Shape = RadarBlipShape.Diamond;
                    blip.VisibleFromOtherGrids = true;
                    blip.RequireNoGrid = false;
                    blip.Enabled = true;
                }
                // _CS End: shared objective radar blips

                structureComp.Structures.Add(uid);
                break;
            }
        }
    }

    private async Task SetupElimination(
        SalvageMission mission,
        Dungeon dungeon,
        MapGridComponent grid,
        Random random)
    {
        await SuspendIfOutOfTime();

        // spawn megafauna in a random place
        var faction = _prototypeManager.Index<SalvageFactionPrototype>(mission.Faction);
        var prototype = faction.Configs["Megafauna"];

        var availableRooms = new ValueList<DungeonRoom>(dungeon.Rooms);
        var availableTiles = new List<Vector2i>();

        var uid = EntityUid.Invalid;
        while (availableRooms.Count > 0 && uid == EntityUid.Invalid)
        {
            availableTiles.Clear();
            var roomIndex = random.Next(availableRooms.Count);
            var room = availableRooms.RemoveSwap(roomIndex);
            availableTiles.AddRange(room.Tiles);

            while (availableTiles.Count > 0)
            {
                var tile = availableTiles.RemoveSwap(random.Next(availableTiles.Count));

                if (!_anchorable.TileFree((mapUid, grid), tile, (int)CollisionGroup.MachineLayer,
                        (int)CollisionGroup.MachineLayer))
                {
                    continue;
                }

                uid = _entManager.SpawnAtPosition(prototype, _map.GridTileToLocal(mapUid, grid, tile));
                break;
            }
        }

        var eliminationComp = _entManager.EnsureComponent<SalvageEliminationExpeditionComponent>(mapUid);
        if (uid != EntityUid.Invalid)
            eliminationComp.Megafauna.Add(uid);
    }
    // _CS End: mission-specific setup functions
}
