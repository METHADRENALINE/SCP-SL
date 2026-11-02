using System;
using System.Collections.Generic;
using System.Linq;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class BraceletAudio : IDisposable
{
    private sealed class Emitter(Player wearer, AudioPlayer player, Speaker speaker, int cue)
    {
        internal Player Wearer = wearer;
        internal AudioPlayer Player = player;
        internal Speaker Speaker = speaker;
        internal int Cue = cue; // 0 activation, 1..5 warnings, -1 irreversible alarm.
        internal int PendingCue;
    }
    private readonly Dictionary<BraceletLife, Emitter> players = new();
    private readonly string prefix = "md-bracelet-" + Guid.NewGuid().ToString("N") + "-";
    private readonly List<string> clips = new();
    private bool initialized;
    internal int SignalsPlayed { get; private set; }
    internal const float AudibleRadius = 35;
    internal static Vector3 Position(Player wearer) => wearer.Position + Vector3.up * 0.6f;

    internal void Enable()
    {
        Add("confirmation", BraceletSignal.CreateConfirmation());
        for (int level = 1; level <= 5; level++) Add("warning" + level, BraceletSignal.CreateWarning(level));
        Add("sequence", BraceletSignal.CreateSequence());
        initialized = true;
        global::StaticUnityMethods.OnFixedUpdate += UpdatePositions;
    }
    private void Add(string key, float[] pcm)
    {
        string name = prefix + key;
        AudioClipStorage.AudioClips.Add(name, new AudioClipData(name, BraceletSignal.SampleRate, 1, pcm));
        clips.Add(name);
    }
    internal void PlayConfirmation(Player wearer) => PlayClip(wearer, "confirmation", 0);
    internal void Play(Player wearer, int level)
    {
        if (level >= 1 && level <= 5) PlayClip(wearer, "warning" + level, level);
    }
    internal void PlaySequence(Player wearer) => PlayClip(wearer, "sequence", -1);

    private void PlayClip(Player wearer, string clip, int cue)
    {
        if (!initialized) return;
        var life = BraceletController.Life(wearer);
        try
        {
            if (!players.TryGetValue(life, out var emitter) || emitter.Player == null)
            {
                Stop(life);
                var player = AudioPlayer.Create(prefix + Guid.NewGuid().ToString("N"),
                    destroyWhenAllClipsPlayed: false, sendSoundGlobally: true);
                if (player == null) throw new InvalidOperationException("No audio controller available.");
                try
                {
                    var speaker = player.AddSpeaker("bracelet", Position(wearer), volume: 0.8f,
                        isSpatial: true, minDistance: 2, maxDistance: AudibleRadius);
                    if (speaker == null) throw new InvalidOperationException("The bracelet speaker could not be created.");
                    speaker.Base.NetworkIsStatic = false;
                    players[life] = emitter = new Emitter(wearer, player, speaker, cue);
                }
                catch { player.Destroy(); throw; }
            }
            // Keep the speaker until release: destroying it at the last PCM
            // packet can cut off the client's remaining buffered sound.
            if (cue > 0 && emitter.Cue < 0) return;
            if (cue > 0 && emitter.Player.ClipsById.Count > 0)
            {
                emitter.PendingCue = Math.Max(emitter.PendingCue, cue);
                return;
            }
            Start(emitter, clip, cue);
        }
        catch (Exception error)
        {
            Stop(life);
            Logger.Error("[METHADRENALINE] Bracelet sound failed: " + error);
        }
    }
    private void Start(Emitter emitter, string clip, int cue)
    {
        // Only the irreversible alarm interrupts a running burst.
        foreach (int id in emitter.Player.ClipsById.Keys.ToArray()) emitter.Player.RemoveClipById(id);
        emitter.PendingCue = 0;
        emitter.Cue = cue;
        emitter.Speaker.Position = Position(emitter.Wearer);
        emitter.Speaker.Base.NetworkPosition = emitter.Speaker.Position;
        emitter.Player.AddClip(prefix + clip);
        SignalsPlayed++;
    }
    internal void UpdatePositions()
    {
        foreach (var pair in players.ToArray())
        {
            var e = pair.Value;
            if (e.Player == null || e.Speaker == null || e.Wearer.IsDestroyed || !e.Wearer.IsAlive || !BraceletController.Life(e.Wearer).Equals(pair.Key))
            { Stop(pair.Key); continue; }
            e.Speaker.Position = Position(e.Wearer);
            e.Speaker.Base.NetworkPosition = e.Speaker.Position;
        }
    }
    internal AudioPlayer? Playback(BraceletLife life) => players.TryGetValue(life, out var e) && e.Player != null ? e.Player : null;
    internal void CancelPendingWarning(BraceletLife life)
    {
        if (players.TryGetValue(life, out var e)) e.PendingCue = 0;
    }
    // Called after the controller observes the current distance, so a return
    // cancels stale warnings before a newly finished burst can dispatch them.
    internal void Flush(BraceletLife life, double distance)
    {
        if (!players.TryGetValue(life, out var e) || e.Player == null || e.Cue < 0 || e.PendingCue == 0 || e.Player.ClipsById.Count > 0) return;
        int cue = e.PendingCue;
        e.PendingCue = 0;
        if (distance >= BraceletPolicy.WarningDistance(cue)) Start(e, "warning" + cue, cue);
    }
    internal void Stop(BraceletLife life)
    {
        if (!players.TryGetValue(life, out var e)) return;
        players.Remove(life);
        if (e.Player == null) return;
        // Destroy is deferred. Silence the previous stream in this frame.
        e.Player.SendSoundGlobally = false;
        e.Player.Condition = null;
        e.Player.Owners.Clear();
        foreach (int id in e.Player.ClipsById.Keys.ToArray()) e.Player.RemoveClipById(id);
        if (e.Speaker != null) e.Speaker.Volume = 0;
        e.Player.Destroy();
    }
    internal void Reset()
    {
        foreach (var life in players.Keys.ToArray()) Stop(life);
    }
    public void Dispose()
    {
        global::StaticUnityMethods.OnFixedUpdate -= UpdatePositions;
        Reset();
        foreach (string clip in clips) AudioClipStorage.DestroyClip(clip);
        clips.Clear();
        initialized = false;
    }
}
