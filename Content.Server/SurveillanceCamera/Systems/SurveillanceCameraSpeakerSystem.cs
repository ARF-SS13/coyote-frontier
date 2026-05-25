using Content.Server.Chat.Systems;
using Content.Server.Instruments;
using Content.Server.Speech;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Speech;
using Content.Shared.Chat;
using Content.Shared.Verbs;
using Content.Shared.Popups;
using System;
using System.Collections.Generic;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server.SurveillanceCamera;

/// <summary>
///     This handles speech for surveillance camera monitors.
/// </summary>
public sealed class SurveillanceCameraSpeakerSystem : EntitySystem
{
    private const float MinRelayVolumeDb = -20f;
    private const float RelayVolumeStepDb = 5f;
    private static readonly TimeSpan MidiRelayIdleTimeout = TimeSpan.FromSeconds(1.5f);
    private const string EntertainmentFrequencyId = "SurveillanceCameraEntertainment";

    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly SpeechSoundSystem _speechSound = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!;
    [Dependency] private readonly InstrumentSystem _instrumentSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    // Numeric frequency value for SurveillanceCameraEntertainment, resolved at init.
    private uint _entertainmentFrequency;

    // Tracks speakers that currently have active MIDI relay sources so Update can skip idle ones.
    private readonly HashSet<EntityUid> _activeMidiRelays = new();
    private readonly List<EntityUid> _relayCleanupBuffer = new();
    private readonly List<EntityUid> _staleMidiSourceBuffer = new();

    private EntityQuery<SurveillanceCameraMonitorComponent> _monitorQuery;
    private EntityQuery<DeviceNetworkComponent> _deviceNetQuery;

    /// <inheritdoc/>
    public override void Initialize()
    {
        base.Initialize();

        if (_prototypeManager.TryIndex<DeviceFrequencyPrototype>(EntertainmentFrequencyId, out var freqProto))
            _entertainmentFrequency = freqProto.Frequency;

        _monitorQuery = GetEntityQuery<SurveillanceCameraMonitorComponent>();
        _deviceNetQuery = GetEntityQuery<DeviceNetworkComponent>();

        SubscribeLocalEvent<SurveillanceCameraSpeakerComponent, SurveillanceCameraSpeechSendEvent>(OnSpeechSent);
        SubscribeLocalEvent<SurveillanceCameraSpeakerComponent, SurveillanceCameraMidiSendEvent>(OnMidiSent);
        SubscribeLocalEvent<SurveillanceCameraSpeakerComponent, SurveillanceCameraMidiChannelFilterSyncEvent>(OnMidiChannelFilterSync);
        SubscribeLocalEvent<SurveillanceCameraSpeakerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetAlternativeVerbs);
        SubscribeLocalEvent<SurveillanceCameraSpeakerComponent, ComponentShutdown>(OnSpeakerShutdown);
    }

    public override void Update(float frameTime)
    {
        if (_activeMidiRelays.Count == 0)
            return;

        var now = _gameTiming.CurTime;
        _relayCleanupBuffer.Clear();

        foreach (var uid in _activeMidiRelays)
        {
            if (!TryComp<SurveillanceCameraSpeakerComponent>(uid, out var speaker) ||
                speaker.RelayMidiSources.Count == 0)
            {
                _relayCleanupBuffer.Add(uid);
                continue;
            }

            _staleMidiSourceBuffer.Clear();
            foreach (var (source, lastTick) in speaker.RelayMidiSources)
            {
                if (now - lastTick > MidiRelayIdleTimeout)
                    _staleMidiSourceBuffer.Add(source);
            }

            foreach (var source in _staleMidiSourceBuffer)
                speaker.RelayMidiSources.Remove(source);

            if (speaker.RelayMidiSources.Count == 0)
            {
                _instrumentSystem.StopRelayPlayback(uid);
                _relayCleanupBuffer.Add(uid);
            }
        }

        foreach (var uid in _relayCleanupBuffer)
            _activeMidiRelays.Remove(uid);
    }

    private void OnSpeakerShutdown(EntityUid uid, SurveillanceCameraSpeakerComponent component, ComponentShutdown args)
    {
        _activeMidiRelays.Remove(uid);
    }

    private void OnGetAlternativeVerbs(EntityUid uid, SurveillanceCameraSpeakerComponent component, GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;

        var user = args.User;

        // Only expose these controls on TVs/entertainment monitors.
        if (!component.RequiresEntertainmentCamera)
            return;

        if (component.RelayMuted)
        {
            args.Verbs.Add(CreateMuteVerb(uid, user, false));
        }
        else
        {
            args.Verbs.Add(CreateMuteVerb(uid, user, true));

            if (component.RelayVolumeDb > MinRelayVolumeDb)
            {
                args.Verbs.Add(CreateVolumeDownVerb(uid, user));
            }

            if (component.RelayVolumeDb < 0f)
            {
                args.Verbs.Add(CreateVolumeUpVerb(uid, user));
            }
        }
    }

    private AlternativeVerb CreateMuteVerb(EntityUid uid, EntityUid user, bool muted)
    {
        var text = muted
            ? Loc.GetString("surveillance-camera-tv-verb-mute")
            : Loc.GetString("surveillance-camera-tv-verb-unmute");

        return new AlternativeVerb
        {
            Text = text,
            Act = () => SetMuted(uid, user, muted)
        };
    }

    private AlternativeVerb CreateVolumeDownVerb(EntityUid uid, EntityUid user)
    {
        return new AlternativeVerb
        {
            Text = Loc.GetString("surveillance-camera-tv-verb-volume-down"),
            Act = () => DecreaseRelayVolume(uid, user)
        };
    }

    private AlternativeVerb CreateVolumeUpVerb(EntityUid uid, EntityUid user)
    {
        return new AlternativeVerb
        {
            Text = Loc.GetString("surveillance-camera-tv-verb-volume-up"),
            Act = () => IncreaseRelayVolume(uid, user)
        };
    }

    private void IncreaseRelayVolume(EntityUid uid, EntityUid user)
    {
        if (!TryComp<SurveillanceCameraSpeakerComponent>(uid, out var speaker))
            return;

        speaker.RelayVolumeDb = MathF.Min(0f, speaker.RelayVolumeDb + RelayVolumeStepDb);

        var volumePercent = VolumePercent(speaker.RelayVolumeDb);
        _popup.PopupEntity(Loc.GetString("surveillance-camera-tv-popup-volume", ("percent", volumePercent)), uid, user, PopupType.Medium);
    }

    private void DecreaseRelayVolume(EntityUid uid, EntityUid user)
    {
        if (!TryComp<SurveillanceCameraSpeakerComponent>(uid, out var speaker))
            return;

        speaker.RelayVolumeDb = MathF.Max(MinRelayVolumeDb, speaker.RelayVolumeDb - RelayVolumeStepDb);

        var volumePercent = VolumePercent(speaker.RelayVolumeDb);
        _popup.PopupEntity(Loc.GetString("surveillance-camera-tv-popup-volume", ("percent", volumePercent)), uid, user, PopupType.Medium);
    }

    private void SetMuted(EntityUid uid, EntityUid user, bool muted)
    {
        if (!TryComp<SurveillanceCameraSpeakerComponent>(uid, out var speaker))
            return;

        speaker.RelayMuted = muted;
        var key = muted ? "surveillance-camera-tv-popup-muted" : "surveillance-camera-tv-popup-unmuted";
        _popup.PopupEntity(Loc.GetString(key), uid, user, PopupType.Medium);
    }

    private static int VolumePercent(float relayVolumeDb)
    {
        var gain = MathF.Pow(10f, relayVolumeDb / 20f);
        return Math.Clamp((int) MathF.Round(gain * 100f), 0, 100);
    }

    /// <summary>
    ///     Returns true when the monitor's active camera is on the entertainment subnet,
    ///     or when the speaker does not restrict to entertainment cameras.
    /// </summary>
    private bool IsEntertainmentFilterSatisfied(EntityUid uid, SurveillanceCameraSpeakerComponent component)
    {
        if (!component.RequiresEntertainmentCamera)
            return true;

        if (!_monitorQuery.TryGetComponent(uid, out var monitor) || monitor.ActiveCamera == null)
            return false;

        return _deviceNetQuery.TryGetComponent(monitor.ActiveCamera.Value, out var deviceNet)
            && deviceNet.ReceiveFrequency == _entertainmentFrequency;
    }

    private void OnSpeechSent(EntityUid uid, SurveillanceCameraSpeakerComponent component,
        SurveillanceCameraSpeechSendEvent args)
    {
        if (!component.SpeechEnabled)
            return;

        if (!IsEntertainmentFilterSatisfied(uid, component))
            return;

        var time = _gameTiming.CurTime;
        var cd = TimeSpan.FromSeconds(component.SpeechSoundCooldown);

        // Play speech sound effect for spoken lines only.
        if (args.Type == InGameICChatType.Speak
            && !component.RelayMuted
            && time - component.LastSoundPlayed >= cd
            && TryComp<SpeechComponent>(args.Speaker, out var speech))
        {
            var sound = _speechSound.GetSpeechSound((args.Speaker, speech), args.Message);
            var audioParams = sound?.Params.AddVolume(component.RelayVolumeDb);
            _audioSystem.PlayPvs(sound, uid, audioParams);

            component.LastSoundPlayed = time;
        }

        var nameEv = new TransformSpeakerNameEvent(args.Speaker, Name(args.Speaker));
        RaiseLocalEvent(args.Speaker, nameEv);

        var name = Loc.GetString("speech-name-relay-broadcast", ("originalName", nameEv.VoiceName));

        // Frontier: Do not send TV messages to admins that are out of range. (GhostRangeLimit>GhostRangeLimitNoAdminCheck)
        // log to chat so people can identity the speaker/source, but avoid clogging ghost chat if there are many radios
        _chatSystem.TrySendInGameICMessage(uid, args.Message, args.Type, ChatTransmitRange.GhostRangeLimitNoAdminCheck, nameOverride: name, ignoreActionBlocker: true);
    }

    private void OnMidiSent(EntityUid uid, SurveillanceCameraSpeakerComponent component, SurveillanceCameraMidiSendEvent args)
    {
        if (component.RelayMuted || args.MidiEvents.Length == 0)
            return;

        if (!IsEntertainmentFilterSatisfied(uid, component))
            return;

        // Sync program/bank from the source instrument so live keyboard performance
        // plays through the correct instrument sound on the relay TV.
        // MIDI file playback already embeds ProgramChange events in the stream; this
        // handles the case where the musician picked a sound via the UI before playing.
        if (TryComp<InstrumentComponent>(args.Source, out var srcInstrument) &&
            TryComp<InstrumentComponent>(uid, out var tvInstrument))
        {
            // _CS Start: Mirror source bank/program and per-channel filter state onto the relay instrument.
            if (tvInstrument.InstrumentProgram != srcInstrument.InstrumentProgram ||
                tvInstrument.InstrumentBank != srcInstrument.InstrumentBank)
            {
                _instrumentSystem.SetInstrumentProgram(uid, tvInstrument,
                    srcInstrument.InstrumentProgram, srcInstrument.InstrumentBank);
            }

            // Keep relay channel filtering in lockstep with the source even when
            // state changes occurred outside explicit filter toggle messages.
            for (var channel = 0; channel < Robust.Shared.Audio.Midi.RobustMidiEvent.MaxChannels; channel++)
            {
                var sourceFiltered = srcInstrument.FilteredChannels[channel];
                if (tvInstrument.FilteredChannels[channel] == sourceFiltered)
                    continue;

                _instrumentSystem.SetFilteredChannel(uid, tvInstrument, channel, sourceFiltered);

                if (sourceFiltered)
                {
                    _instrumentSystem.RelayMidiEvents(uid,
                        [Robust.Shared.Audio.Midi.RobustMidiEvent.AllNotesOff((byte) channel, 0)]);
                }
            }
            // _CS End: Mirror source bank/program and per-channel filter state onto the relay instrument.
        }

        component.RelayMidiSources[args.Source] = _gameTiming.CurTime;
        _activeMidiRelays.Add(uid);
        _instrumentSystem.RelayMidiEvents(uid, args.MidiEvents);
    }

    private void OnMidiChannelFilterSync(EntityUid uid, SurveillanceCameraSpeakerComponent component, SurveillanceCameraMidiChannelFilterSyncEvent args)
    {
        if (!IsEntertainmentFilterSatisfied(uid, component))
            return;

        if (!TryComp<InstrumentComponent>(uid, out var tvInstrument))
            return;

        if (args.Channel < 0 || args.Channel >= Robust.Shared.Audio.Midi.RobustMidiEvent.MaxChannels)
            return;

        // _CS Start: Apply explicit channel filter sync payloads and stop ringing notes when filtering on.
        // Only mutate when needed; this keeps the sync path cheap for rapid channel clicks.
        if (tvInstrument.FilteredChannels[args.Channel] == args.Filtered)
            return;

        _instrumentSystem.SetFilteredChannel(uid, tvInstrument, args.Channel, args.Filtered);

        if (args.Filtered)
        {
            _instrumentSystem.RelayMidiEvents(uid,
                [Robust.Shared.Audio.Midi.RobustMidiEvent.AllNotesOff((byte) args.Channel, 0)]);
        }
        // _CS End: Apply explicit channel filter sync payloads and stop ringing notes when filtering on.
    }

}
