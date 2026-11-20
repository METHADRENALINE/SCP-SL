using System;
using System.Collections.Generic;
using System.IO;
using LabApi.Features.Wrappers;
using MapGeneration;
using MEC;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class BlackoutAudio : IDisposable
{
    private const int Rate = 48000;
    private const int AmbienceSeconds = 60;
    private const int FadeSeconds = 3;
    internal const float SpeakerWarmup = .15f;
    private readonly string prefix = "md-blackout-" + Guid.NewGuid().ToString("N") + "-";
    private readonly List<string> intrusions = new();
    private AudioPlayer? outagePlayer, ambiencePlayer, intrusionPlayer;
    private int previousIntrusion = -1;
    internal bool HasIntrusions => intrusions.Count != 0;
    internal float OpeningSpeechSeconds { get; private set; }

    internal void Load(string privateFile, string effectsDirectory)
    {
        string ambience = prefix + "lcz";
        if (LoadClip(ambience, privateFile))
        {
            var source = AudioClipStorage.AudioClips[ambience].Samples;
            var samples = new float[(AmbienceSeconds + FadeSeconds) * Rate];
            for (int i = 0; i < samples.Length; i++)
            {
                float fade = Math.Min(1f, (samples.Length - 1 - i) / (float)(FadeSeconds * Rate));
                samples[i] = source[i % source.Length] * fade * Math.Min(1f, i / (Rate * .25f));
            }
            AudioClipStorage.AudioClips[ambience] = new AudioClipData(ambience, Rate, 1, samples);
        }

        if (string.IsNullOrWhiteSpace(effectsDirectory)) return;
        LoadClip(prefix + "power-trip", Path.Combine(effectsDirectory, "power-trip.ogg"));
        string openingSpeech = prefix + "079-speech";
        if (LoadClip(openingSpeech, Path.Combine(effectsDirectory, "079-speech.ogg")))
            OpeningSpeechSeconds = AudioClipStorage.AudioClips[openingSpeech].Samples.Length / (float)Rate;
        for (int i = 2; i <= 7; i++)
        {
            if (i == 3) continue;
            string name = "079-" + i;
            if (LoadClip(prefix + name, Path.Combine(effectsDirectory, name + ".ogg")))
                intrusions.Add(prefix + name);
        }
    }

    private static bool LoadClip(string name, string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return false;
        try
        {
            if (!Path.IsPathRooted(path)) throw new InvalidDataException("An absolute path is required.");
            if (!AudioClipStorage.LoadClip(File.ReadAllBytes(path), name)) throw new InvalidDataException("Invalid Ogg Vorbis clip.");
            var clip = AudioClipStorage.AudioClips[name];
            if (clip.SampleRate != Rate || clip.Channels != 1 || clip.Samples.Length == 0)
                throw new InvalidDataException("Expected nonempty 48 kHz mono audio.");
            return true;
        }
        catch (Exception e)
        {
            if (AudioClipStorage.AudioClips.ContainsKey(name)) AudioClipStorage.DestroyClip(name);
            Logger.Warn("Blackout audio unavailable: " + e.Message);
            return false;
        }
    }

    internal void PlayOutage()
    {
        outagePlayer = PlayClip(prefix + "power-trip", .95f, InFacility);
        ambiencePlayer = PlayClip(prefix + "lcz", .72f, InLightZone);
    }

    internal void PlayOpeningSpeech()
        => intrusionPlayer = PlayClip(prefix + "079-speech", .88f, HumanInFacility);

    internal void PlayIntrusion()
    {
        if (!HasIntrusions || intrusionPlayer != null) return;
        bool excludePrevious = previousIntrusion >= 0 && intrusions.Count > 1;
        int index = UnityEngine.Random.Range(0, intrusions.Count - (excludePrevious ? 1 : 0));
        if (excludePrevious && index >= previousIntrusion) index++;
        previousIntrusion = index;
        intrusionPlayer = PlayClip(intrusions[index], .76f, HumanInFacility);
    }

    internal static bool InLightZone(ReferenceHub hub)
        => Player.Get(hub)?.IsAlive == true && hub.GetCurrentZone() == FacilityZone.LightContainment;

    private static bool InFacility(ReferenceHub hub)
        => Player.Get(hub)?.IsAlive == true && hub.GetCurrentZone() is
            FacilityZone.LightContainment or FacilityZone.HeavyContainment or FacilityZone.Entrance;

    private static bool HumanInFacility(ReferenceHub hub)
        => Player.Get(hub)?.IsHuman == true && InFacility(hub);

    private static AudioPlayer? PlayClip(string clip, float volume, Func<ReferenceHub, bool> audience)
    {
        if (!AudioClipStorage.AudioClips.ContainsKey(clip)) return null;
        AudioPlayer? player = null;
        try
        {
            player = AudioPlayer.Create(clip, destroyWhenAllClipsPlayed: false, sendSoundGlobally: false, condition: audience);
            if (player == null) throw new InvalidOperationException("No audio controller available for Blackout.");
            if (player.AddSpeaker("pa", volume: volume, isSpatial: false, maxDistance: 10000f) == null)
                throw new InvalidOperationException("Blackout speaker unavailable.");
            Timing.CallDelayed(SpeakerWarmup, () =>
            {
                if (player == null) return;
                player.AddClip(clip, destroyOnEnd: false);
                player.DestroyWhenAllClipsPlayed = true;
            });
            return player;
        }
        catch (Exception e)
        {
            if (player != null) player.Destroy();
            Logger.Warn("Blackout playback failed: " + e.Message);
            return null;
        }
    }

    internal void Stop()
    {
        if (outagePlayer != null) outagePlayer.Destroy();
        if (ambiencePlayer != null) ambiencePlayer.Destroy();
        if (intrusionPlayer != null) intrusionPlayer.Destroy();
        outagePlayer = ambiencePlayer = intrusionPlayer = null;
        previousIntrusion = -1;
    }

    public void Dispose()
    {
        Stop();
        foreach (var name in intrusions) AudioClipStorage.DestroyClip(name);
        intrusions.Clear();
        foreach (var name in new[] { prefix + "lcz", prefix + "power-trip", prefix + "079-speech" })
            if (AudioClipStorage.AudioClips.ContainsKey(name)) AudioClipStorage.DestroyClip(name);
    }
}
