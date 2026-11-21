using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using LabApi.Events.Arguments.WarheadEvents;
using LabApi.Events.Handlers;
using Mirror;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class WarheadAudio : IDisposable
{
    private const int SampleRate = 48000;
    private const int Packet = 480;
    private const double SpeakerWarmup = .15;
    private const double MusicTail = 4;
    private const double NativeFinaleLead = .65;
    private readonly string clipName = "md-warhead-" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<string, double> musicCues = new();
    private string directory = "";
    private AlphaWarheadController? controller;
    private AudioPlayer? audio;
    private AudioClipPlayback? track;
    private double warmUntil;
    private double trackLength;
    private double previousStart;
    private double fadeUntil;
    private double cutoff;
    private double lightsOffset;
    private bool nativeFinale;
    private bool cancelled;
    private bool enabled;

    internal bool OverridesCountdown(AlphaWarheadController warhead) => ReferenceEquals(controller, warhead);

    internal bool ShowsFinale => nativeFinale;
    internal bool EmergencyLights { get; private set; }

    internal void Enable(string audioDirectory)
    {
        if (string.IsNullOrWhiteSpace(audioDirectory)) return;
        if (!Path.IsPathRooted(audioDirectory) || !Directory.Exists(audioDirectory))
        {
            Logger.Warn("[METHADRENALINE] Warhead audio directory is unavailable.");
            return;
        }
        directory = audioDirectory;
        try
        {
            foreach (string line in File.ReadLines(Path.Combine(directory, "music-cues.tsv")))
            {
                var cue = line.Split('\t');
                if (cue.Length != 2 || !double.TryParse(cue[1], NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out double seconds) || !(seconds >= 0 && seconds < 120))
                    throw new InvalidDataException("Invalid warhead music cue.");
                musicCues.Add(cue[0], seconds);
            }
        }
        catch (Exception e)
        {
            musicCues.Clear();
            Logger.Warn("[METHADRENALINE] Cannot load warhead music cues: " + e.Message);
            return;
        }
        enabled = true;
        WarheadEvents.Started += Started;
        WarheadEvents.Stopped += Stopped;
        WarheadEvents.Detonated += Detonated;
        ServerEvents.WaitingForPlayers += Reset;
        ServerEvents.RoundRestarted += Reset;
        StaticUnityMethods.OnFixedUpdate += Update;
    }

    private void Started(WarheadStartedEventArgs _)
    {
        var warhead = AlphaWarheadController.Singleton;
        if (warhead == null || !warhead.Info.InProgress) return;
        Reset();
        if (BlackoutWarnings.Muted) return;
        try
        {
            var scenario = warhead.CurScenario;
            string prefix = warhead.Info.ScenarioType switch
            {
                WarheadScenarioType.Start => "start",
                WarheadScenarioType.Resume => "resume",
                WarheadScenarioType.DeadmanSwitch => "deadman",
                _ => throw new InvalidOperationException("Unknown warhead scenario.")
            };
            string variant = prefix + "-" + scenario.TimeToDetonate.ToString(CultureInfo.InvariantCulture);
            CreateTrack(variant, scenario.TotalTime + MusicTail);
            lightsOffset = musicCues[variant];
            var state = warhead.Info;
            state.StartTime = warmUntil;
            warhead.NetworkInfo = state;
            controller = warhead;
            previousStart = warhead.Info.StartTime;
            warhead.SetSyncVarDirtyBit(1UL);
            Logger.Info($"[METHADRENALINE] Warhead audio: {variant}, countdown {scenario.TotalTime:0.00}s.");
        }
        catch (Exception error)
        {
            Reset();
            Logger.Warn("[METHADRENALINE] Warhead audio fell back to stock: " + error.Message);
        }
    }

    private void Stopped(WarheadStoppedEventArgs _)
    {
        if (controller == null) return;
        bool playCancellation = !BlackoutWarnings.Muted && controller.Info.ScenarioType != WarheadScenarioType.DeadmanSwitch;
        Reset();
        if (!playCancellation) return;
        try
        {
            CreateTrack("cancel", null);
            cancelled = true;
            cutoff = warmUntil + trackLength;
        }
        catch (Exception error)
        {
            ClearTrack();
            Logger.Warn("[METHADRENALINE] Warhead cancellation audio unavailable: " + error.Message);
        }
    }

    private void Detonated(WarheadDetonatedEventArgs _)
    {
        if (controller == null) return;
        RevealFinale();
        SetEmergencyLights(true);
        cutoff = NetworkTime.time + MusicTail + SpeakerWarmup;
    }

    private void SetEmergencyLights(bool active)
    {
        if (EmergencyLights == active) return;
        EmergencyLights = active;
        foreach (var light in RoomLightController.Instances)
            if (light) light.SetSyncVarDirtyBit(3UL);
    }

    private void RevealFinale()
    {
        if (nativeFinale || controller == null) return;
        nativeFinale = true;
        controller.SetSyncVarDirtyBit(1UL);
    }

    private void CreateTrack(string variant, double? expectedLength)
    {
        string path = Path.Combine(directory, variant + ".ogg");
        if (!AudioClipStorage.LoadClip(File.ReadAllBytes(path), clipName))
            throw new InvalidDataException("Cannot decode warhead audio: " + variant);
        var clip = AudioClipStorage.AudioClips[clipName];
        trackLength = (double)clip.Samples.Length / SampleRate;
        if (clip.SampleRate != SampleRate || clip.Channels != 1 || clip.Samples.Length < Packet ||
            expectedLength.HasValue && Math.Abs(trackLength - expectedLength.Value) > .04)
            throw new InvalidDataException("Unexpected warhead audio format or duration: " + variant);
        audio = AudioPlayer.Create(clipName + "-" + Guid.NewGuid().ToString("N"), destroyWhenAllClipsPlayed: false, sendSoundGlobally: true)
            ?? throw new InvalidOperationException("No warhead audio controller available.");
        if (audio.AddSpeaker("warhead", volume: 1, isSpatial: false, maxDistance: 10000) == null)
            throw new InvalidOperationException("No warhead speaker available.");
        track = audio.AddClip(clipName, loop: false, destroyOnEnd: false);
        track.IsPaused = true;
        warmUntil = NetworkTime.time + SpeakerWarmup;
    }

    private void Update()
    {
        if (track == null) return;
        double now = NetworkTime.time;
        if (cutoff > 0 && now >= cutoff) { ClearTrack(); return; }
        if (now < warmUntil) return;

        double playAt;
        bool retimed = false;
        if (cancelled) playAt = warmUntil;
        else
        {
            if (controller == null) { Reset(); return; }
            if (!controller.Info.InProgress && cutoff <= 0) { Reset(); return; }
            double start = controller.Info.StartTime;
            retimed = start != previousStart;
            double remaining = start + controller.CurScenario.TotalTime - now;
            SetEmergencyLights(now >= start + lightsOffset);
            if (remaining <= NativeFinaleLead) RevealFinale();
            else if (nativeFinale && retimed)
            {
                nativeFinale = false;
                controller.SetSyncVarDirtyBit(1UL);
            }
            playAt = start;
            previousStart = start;
        }
        double elapsed = now - playAt;
        if (elapsed < 0) { track.IsPaused = true; return; }
        if (elapsed >= trackLength)
        {
            track.IsPaused = true;
            return;
        }
        int expected = (int)(elapsed * SampleRate) / Packet * Packet;
        if (audio != null && !audio.ClipsById.ContainsKey(track.Id))
        {
            track = audio.AddClip(clipName, loop: false, destroyOnEnd: false);
            retimed = true;
        }
        if (track.IsPaused || retimed || Math.Abs(track.ReadPosition - expected) > SampleRate / 8)
        {
            track.ReadPosition = expected;
            if (retimed || track.IsPaused && expected >= SampleRate / 2) fadeUntil = now + .35;
            track.IsPaused = false;
        }
        track.Volume = Mathf.Clamp01((float)(1 - (fadeUntil - now) / .35));
    }

    private void ClearTrack()
    {
        if (track != null) track.IsPaused = true;
        if (audio != null) audio.Destroy();
        audio = null;
        track = null;
        if (AudioClipStorage.AudioClips.ContainsKey(clipName)) AudioClipStorage.DestroyClip(clipName);
    }

    internal void Reset()
    {
        var old = controller;
        controller = null;
        SetEmergencyLights(false);
        if (old != null) old.SetSyncVarDirtyBit(1UL);
        ClearTrack();
        nativeFinale = cancelled = false;
        cutoff = fadeUntil = 0;
    }

    public void Dispose()
    {
        if (!enabled) return;
        enabled = false;
        WarheadEvents.Started -= Started;
        WarheadEvents.Stopped -= Stopped;
        WarheadEvents.Detonated -= Detonated;
        ServerEvents.WaitingForPlayers -= Reset;
        ServerEvents.RoundRestarted -= Reset;
        StaticUnityMethods.OnFixedUpdate -= Update;
        Reset();
        musicCues.Clear();
    }
}
