using Content.Server.Salvage.Expeditions;
using Content.Server.Weather;
using Content.Shared.Procedural;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Weather;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    // _CS Start: expedition weather system
    [Dependency] private readonly WeatherSystem _weatherSystem = default!;

    /// <summary>
    /// Interval between weather rolls (15 minutes).
    /// Rolls continue while the expedition map exists.
    /// </summary>
    private static readonly TimeSpan WeatherRollInterval = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Chance for a weather roll to produce weather.
    /// Kept low to avoid back-to-back events.
    /// </summary>
    private const float WeatherRollChance = 0.3f;

    private static readonly string[] SnowWeatherPhases =
    [
        "SnowfallLight",
        "SnowfallMedium",
        "SnowfallHeavy",
        "SnowfallMedium",
        "SnowfallLight",
    ];

    private static readonly string[] CaveWeatherPhases =
    [
        "Sandstorm",
        "SandstormHeavy",
        "Sandstorm",
    ];

    private static readonly string[] LavaWeatherPhases =
    [
        "AshfallLight",
        "Ashfall",
        "AshfallHeavy",
        "Ashfall",
        "AshfallLight",
    ];

    /// <summary>
    /// Initialises weather for an expedition map.
    /// Caches the biome, performs the initial roll, and schedules recurring rolls.
    /// </summary>
    private void InitExpeditionWeather(EntityUid uid, SalvageExpeditionComponent comp)
    {
        // Already initialized for this expedition map.
        if (!string.IsNullOrEmpty(comp.BiomeId))
            return;

        if (string.IsNullOrEmpty(comp.MissionParams.Difficulty))
            return;

        if (!_prototypeManager.TryIndex<SalvageDifficultyPrototype>(comp.MissionParams.Difficulty, out var diff))
            return;

        var mission = GetMission(comp.MissionParams.MissionType, diff, comp.MissionParams.Seed);
        comp.BiomeId = mission.Biome;

        var mapId = Comp<MapComponent>(uid).MapId;

        // Roll immediately when map weather is initialized.
        TryRollExpeditionWeather(comp, mapId, initialSpawnRoll: true);

        // Schedule recurring rolls.
        comp.WeatherNextRoll = _timing.CurTime + WeatherRollInterval;
    }

    private void StopExpeditionWeather(EntityUid uid, SalvageExpeditionComponent comp)
    {
        comp.WeatherNextRoll = TimeSpan.MaxValue;
        comp.WeatherPhaseSequence = null;
        comp.WeatherPhaseIndex = 0;
        comp.WeatherPhaseEnd = TimeSpan.Zero;
        comp.WeatherPhaseDuration = TimeSpan.Zero;

        if (TryComp<MapComponent>(uid, out var mapComp))
            _weatherSystem.SetWeather(mapComp.MapId, null, null);
    }

    /// <summary>
    /// Ticks weather phase advancement and scheduled rolls for all running expeditions.
    /// Called from Update() each frame.
    /// </summary>
    private void UpdateExpeditionWeather()
    {
        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<SalvageExpeditionComponent, MapComponent>();

        while (query.MoveNext(out _, out var comp, out var mapComp))
        {
            // Advance staged weather phases.
            if (comp.WeatherPhaseSequence != null && now >= comp.WeatherPhaseEnd)
            {
                comp.WeatherPhaseIndex++;

                if (comp.WeatherPhaseIndex < comp.WeatherPhaseSequence.Count)
                {
                    var nextProto = comp.WeatherPhaseSequence[comp.WeatherPhaseIndex];
                    var phaseEnd = now + comp.WeatherPhaseDuration;
                    comp.WeatherPhaseEnd = phaseEnd;

                    if (_prototypeManager.TryIndex<WeatherPrototype>(nextProto, out var nextWeather))
                        _weatherSystem.SetWeather(mapComp.MapId, nextWeather, phaseEnd);
                }
                else
                {
                    comp.WeatherPhaseSequence = null;

                    // Only clear weather if a new roll isn't also firing this tick.
                    // If it is, let the roll start new weather directly — avoids a blank fade gap.
                    var rollImminent = comp.WeatherNextRoll != TimeSpan.MaxValue && now >= comp.WeatherNextRoll;
                    if (!rollImminent)
                        _weatherSystem.SetWeather(mapComp.MapId, null, null);
                }
            }

            // Perform recurring weather rolls while the expedition map exists.
            if (comp.WeatherNextRoll != TimeSpan.MaxValue && now >= comp.WeatherNextRoll)
            {
                comp.WeatherNextRoll = now + WeatherRollInterval;
                TryRollExpeditionWeather(comp, mapComp.MapId, initialSpawnRoll: false);
            }
        }
    }

    /// <summary>
    /// Rolls and optionally starts a weather event for the given expedition based on its biome.
    /// </summary>
    private void TryRollExpeditionWeather(SalvageExpeditionComponent comp, MapId mapId, bool initialSpawnRoll)
    {
        if (string.IsNullOrEmpty(comp.BiomeId))
            return;

        if (_random.NextFloat() > WeatherRollChance)
            return;

        switch (comp.BiomeId)
        {
            case "Grasslands":
            {
                var proto = _random.Next(2) == 0 ? "Rain" : "Storm";
                var endTime = _timing.CurTime + WeatherRollInterval;
                if (_prototypeManager.TryIndex<WeatherPrototype>(proto, out var grassWeather))
                    _weatherSystem.SetWeather(mapId, grassWeather, endTime);
                break;
            }

            case "Snow":
                StartStagedExpeditionWeather(comp, mapId, SnowWeatherPhases, initialSpawnRoll);
                break;

            case "Caves":
                StartStagedExpeditionWeather(comp, mapId, CaveWeatherPhases, initialSpawnRoll);
                break;

            case "Lava":
                StartStagedExpeditionWeather(comp, mapId, LavaWeatherPhases, initialSpawnRoll);
                break;

            case "Shadow":
            {
                var proto = _random.Next(2) == 0 ? "Rain" : "Storm";
                var endTime = _timing.CurTime + WeatherRollInterval;
                if (_prototypeManager.TryIndex<WeatherPrototype>(proto, out var shadowWeather))
                    _weatherSystem.SetWeather(mapId, shadowWeather, endTime);
                break;
            }
        }
    }

    /// <summary>
    /// Starts a staged sequence.
    /// Initial spawn rolls can start at stage 2/3 and then fade down to stage 1 before clearing.
    /// All staged sequences are normalized to exactly 10 minutes.
    /// </summary>
    private void StartStagedExpeditionWeather(SalvageExpeditionComponent comp, MapId mapId, string[] phases, bool initialSpawnRoll)
    {
        var sequence = BuildStagedWeatherSequence(phases, initialSpawnRoll);
        if (sequence.Count == 0)
            return;

        var now = _timing.CurTime;
        var phaseDuration = TimeSpan.FromTicks(WeatherRollInterval.Ticks / sequence.Count);
        var phaseEnd = now + phaseDuration;

        comp.WeatherPhaseSequence = sequence;
        comp.WeatherPhaseIndex = 0;
        comp.WeatherPhaseDuration = phaseDuration;
        comp.WeatherPhaseEnd = phaseEnd;

        if (_prototypeManager.TryIndex<WeatherPrototype>(sequence[0], out var firstWeather))
            _weatherSystem.SetWeather(mapId, firstWeather, phaseEnd);
    }

    /// Builds the phase sequence for staged weather.
    /// For initial spawn rolls, starts at stage 2 or 3 (where available) and fades down.
    /// </summary>
    private List<string> BuildStagedWeatherSequence(string[] phases, bool initialSpawnRoll)
    {
        var result = new List<string>();

        if (phases.Length == 0)
            return result;

        // Expected staged layouts are symmetrical: [S1, S2, S3, S2, S1] or [S1, S2, S1].
        var peakIndex = phases.Length / 2;

        if (!initialSpawnRoll || peakIndex <= 0)
        {
            result.AddRange(phases);
            return result;
        }

        // Start at elevated intensity: stage 2 or stage 3 where available.
        var startLevel = _random.Next(1, peakIndex + 1);

        // Fade down to stage 1 and then clear.
        for (var level = startLevel; level >= 0; level--)
        {
            result.Add(phases[level]);
        }

        return result;
    }

    // _CS End: expedition weather system
}
