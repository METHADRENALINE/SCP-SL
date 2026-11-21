using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using AdminToys;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using RueI.API;
using RueI.API.Elements;
using RueI.API.Elements.Parameters;
using SecretLabNAudio.Core;
using UnityEngine;
using UserSettings.ServerSpecific;
using LightToy = LabApi.Features.Wrappers.LightSourceToy;
using PrimitiveToy = LabApi.Features.Wrappers.PrimitiveObjectToy;
using SpeakerToy = LabApi.Features.Wrappers.SpeakerToy;

namespace Methadrenaline.Server;

internal sealed class ParavozPresentation : IDisposable
{
    internal const int TrackHeaderId = 2026102601;
    internal const int TrackSettingId = 2026102602;
    private static readonly Tag TrackTip = new("methadrenaline.paravoz.track");

    internal sealed class Emitter(Player player)
    {
        internal readonly Player Player = player;
        internal readonly int Life = player.LifeId;
        internal readonly List<AdminToy> Toys = new();
        internal AudioPlayer? Playback;
        internal int TrackIndex;
        internal SpeakerToy Speaker = null!;
        internal SpeakerPersonalization Personalization = null!;
        internal LightToy Light = null!;
        internal readonly List<PrimitiveToy> Embers = new();
        internal readonly List<PrimitiveToy> Flames = new();
        internal bool TrackKeyPressed;
    }

    private readonly string clipPrefix = "md-paravoz-" + Guid.NewGuid().ToString("N");
    private readonly List<string> clips = new();
    private readonly Dictionary<uint, Emitter> emitters = new();
    private readonly Dictionary<Player, uint?> selected = new();
    private readonly Dictionary<ReferenceHub, uint> audible = new();
    private float nextUpdate;
    private bool enabled;
    internal IReadOnlyDictionary<uint, Emitter> Emitters => emitters;
    internal string ClipName(Emitter emitter) => clips[emitter.TrackIndex];
    internal int TrackCount => clips.Count;

    internal void Enable(string directory)
    {
        using var source = typeof(ServerPlugin).Assembly.GetManifestResourceStream("Methadrenaline.Server.Audio.paravoz-track.ogg")
            ?? throw new InvalidOperationException("Paravoz music resource is missing.");
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        LoadTrack(bytes.ToArray(), "01");
        if (!string.IsNullOrWhiteSpace(directory)) LoadAdditionalTracks(directory);
        LabApi.Features.Console.Logger.Info($"[METHADRENALINE] Paravoz music: {TrackCount} track(s).");
        enabled = true;
        ServerSpecificSettingsSync.ServerOnSettingValueReceived += TrackSettingReceived;
        StaticUnityMethods.OnFixedUpdate += Update;
    }

    private void LoadAdditionalTracks(string directory)
    {
        try
        {
            foreach (string file in Directory.GetFiles(directory, "paravoz-track-*.ogg").OrderBy(Path.GetFileName, StringComparer.Ordinal))
            {
                var name = Regex.Match(Path.GetFileName(file), @"^paravoz-track-(0[2-9]|[1-9][0-9])\.ogg$");
                if (!name.Success) continue;
                try { LoadTrack(File.ReadAllBytes(file), name.Groups[1].Value); }
                catch (Exception error) { LabApi.Features.Console.Logger.Warn($"Paravoz track {name.Groups[1].Value} skipped: {error.Message}"); }
            }
        }
        catch (Exception error) { LabApi.Features.Console.Logger.Warn("Paravoz playlist unavailable: " + error.Message); }
    }

    private void LoadTrack(byte[] bytes, string number)
    {
        string name = clipPrefix + "-" + number;
        try
        {
            if (!AudioClipStorage.LoadClip(bytes, name)) throw new InvalidDataException("Invalid Ogg Vorbis.");
            var clip = AudioClipStorage.AudioClips[name];
            if (clip.SampleRate != ParavozPresentationPolicy.SampleRate || clip.Channels != 1)
                throw new InvalidDataException("Paravoz music must be 48000 Hz mono Vorbis.");
            AudioClipStorage.AudioClips[name] = new AudioClipData(name, clip.SampleRate, 1, ParavozPresentationPolicy.LoopSamples(clip.Samples));
            clips.Add(name);
        }
        catch
        {
            if (AudioClipStorage.AudioClips.ContainsKey(name)) AudioClipStorage.DestroyClip(name);
            throw;
        }
    }

    private void TrackSettingReceived(ReferenceHub hub, ServerSpecificSettingBase setting)
    {
        if (setting.SettingId != TrackSettingId || setting is not SSKeybindSetting key || TrackCount < 2 ||
            !emitters.TryGetValue(hub.netId, out var emitter) || emitter.Playback is not { } playback ||
            !emitter.Player.IsAlive || emitter.Player.LifeId != emitter.Life) return;
        if (emitter.TrackKeyPressed == key.SyncIsPressed) return;
        emitter.TrackKeyPressed = key.SyncIsPressed;
        if (!key.SyncIsPressed) return;

        int next = (emitter.TrackIndex + 1) % TrackCount;
        int previous = playback.ClipsById.Keys.Single();
        playback.AddClip(clips[next], loop: true, destroyOnEnd: false);
        playback.RemoveClipById(previous);
        emitter.TrackIndex = next;
    }

    internal void Apply(Player player)
    {
        if (!enabled) throw new InvalidOperationException("Paravoz presentation is not enabled.");
        Forget(player.NetworkId);
        uint source = player.NetworkId;
        var emitter = new Emitter(player) { TrackIndex = UnityEngine.Random.Range(0, TrackCount) };
        try
        {
            emitter.Playback = AudioPlayer.Create(clipPrefix + "-" + Guid.NewGuid().ToString("N"),
                destroyWhenAllClipsPlayed: false, sendSoundGlobally: false,
                condition: hub => audible.TryGetValue(hub, out uint chosen) && chosen == source)
                ?? throw new InvalidOperationException("No audio controller available for Paravoz music.");
            emitter.Playback.AddClip(ClipName(emitter), loop: true, destroyOnEnd: false);
            Transform parent = player.ReferenceHub.transform;
            emitter.Speaker = SpeakerToy.Create(Vector3.zero, parent, networkSpawn: false);
            emitter.Toys.Add(emitter.Speaker);
            emitter.Speaker.ControllerId = emitter.Playback.ControllerID;
            emitter.Speaker.IsSpatial = true;
            emitter.Speaker.Volume = 0;
            emitter.Speaker.MinDistance = ParavozPresentationPolicy.MusicNear;
            emitter.Speaker.MaxDistance = ParavozPresentationPolicy.MusicRange;
            emitter.Personalization = emitter.Speaker.GameObject.AddComponent<SpeakerPersonalization>();
            emitter.Light = LightToy.Create(Vector3.zero, parent, networkSpawn: false);
            emitter.Toys.Add(emitter.Light);
            emitter.Light.Type = LightType.Point;
            emitter.Light.ShadowType = LightShadows.None;
            emitter.Light.Range = 4;
            emitter.Light.Intensity = 1.8f;
            emitter.Light.Color = new Color(1, 0.24f, 0.025f);
            for (int i = 0; i < ParavozPresentationPolicy.EmberCount; i++)
            {
                var ember = PrimitiveToy.Create(Vector3.zero, parent, networkSpawn: false);
                emitter.Toys.Add(ember);
                emitter.Embers.Add(ember);
                ember.Type = PrimitiveType.Sphere;
                ember.Flags = PrimitiveFlags.Visible;
                ember.Color = i % 2 == 0 ? new Color(1, 0.35f, 0.035f) : new Color(1, 0.12f, 0.01f);
            }
            ParavozFlame.Create(parent, emitter.Flames, emitter.Toys);
            Animate(emitter, Time.realtimeSinceStartup);
            foreach (AdminToy toy in emitter.Toys)
            {
                toy.IsStatic = false;
                toy.SyncInterval = 0.125f;
                toy.MovementSmoothing = 128;
                SyncTransform(toy);
                toy.Spawn();
            }
            emitters.Add(player.NetworkId, emitter);
            RefreshNow();
            if (TrackCount > 1)
                RueDisplay.Get(player).Show(TrackTip, new DynamicElement(58, () =>
                    "<align=center><size=34><nobr>" + Localizer.Text(player.ReferenceHub, "Press {0} to play the next track.") + "</nobr></size></align>")
                {
                    Parameters = [new KeybindParameter(TrackSettingId, "<b>{0}</b>")]
                }, 30f);
        }
        catch
        {
            emitters.Remove(player.NetworkId);
            Destroy(emitter);
            throw;
        }
    }

    private void Update()
    {
        float now = Time.realtimeSinceStartup;
        if (now < nextUpdate || emitters.Count == 0) return;
        nextUpdate = now + 0.125f;
        RefreshNow();
    }

    internal void RefreshNow()
    {
        foreach (var pair in emitters.ToArray())
        {
            var e = pair.Value;
            if (e.Player.IsDestroyed || !e.Player.IsAlive || e.Player.LifeId != e.Life) { Forget(pair.Key); continue; }
            Animate(e, Time.realtimeSinceStartup);
            foreach (var toy in e.Toys) if (!toy.IsDestroyed) SyncTransform(toy);
        }
        var viewers = Player.List.Where(p => !p.IsHost && !p.IsDestroyed).ToArray();
        foreach (Player left in selected.Keys.Except(viewers).ToArray())
        {
            foreach (var e in emitters.Values) e.Personalization.ClearOverride(left);
            selected.Remove(left);
        }
        audible.Clear();
        var distances = new Dictionary<uint, double>(emitters.Count);
        foreach (Player viewer in viewers)
        {
            Player target = viewer.CurrentlySpectating ?? viewer;
            Vector3 position = target.RoleBase is ICameraController camera ? camera.CameraPosition : target.ReferenceHub.PlayerCameraReference.position;
            distances.Clear();
            foreach (var pair in emitters) distances.Add(pair.Key, (pair.Value.Speaker.Transform.position - position).sqrMagnitude);
            selected.TryGetValue(viewer, out uint? previous);
            uint? chosen = ParavozPresentationPolicy.Select(target.NetworkId, previous, distances);
            selected[viewer] = chosen;
            if (chosen.HasValue) audible.Add(viewer.ReferenceHub, chosen.Value);
            foreach (var pair in emitters)
                pair.Value.Personalization.Override(viewer, new SpeakerSettings
                {
                    IsSpatial = true,
                    Volume = chosen == pair.Key ? ParavozPresentationPolicy.MusicVolume : 0,
                    MinDistance = ParavozPresentationPolicy.MusicNear,
                    MaxDistance = ParavozPresentationPolicy.MusicRange
                });
        }
    }

    private static void Animate(Emitter e, float now)
    {
        ParavozFlame.Animate(e.Player.ReferenceHub.transform, e.Player.ReferenceHub.PlayerCameraReference, e.Flames, now);
        Vector3 head = e.Player.ReferenceHub.transform.InverseTransformPoint(e.Player.ReferenceHub.PlayerCameraReference.position);
        e.Speaker.Position = head;
        e.Light.Position = head + Vector3.down * 0.75f;
        for (int i = 0; i < e.Embers.Count; i++)
        {
            float phase = Mathf.Repeat(now * 0.8f + i * 0.25f, 1);
            float angle = i * 2.4f + phase * 0.9f;
            var ember = e.Embers[i];
            ember.Position = head + new Vector3(Mathf.Cos(angle) * 0.28f, -1.1f + phase * 0.75f, -0.22f + Mathf.Sin(angle) * 0.15f);
            float size = Mathf.Lerp(0.08f, 0.025f, phase);
            ember.Scale = new Vector3(size, size * 1.8f, size);
        }
    }

    private static void SyncTransform(AdminToy toy)
    {
        toy.Base.NetworkPosition = toy.Transform.localPosition;
        toy.Base.NetworkRotation = toy.Transform.localRotation;
        toy.Base.NetworkScale = toy.Transform.localScale;
    }

    internal void Forget(uint id)
    {
        if (!emitters.TryGetValue(id, out var e)) return;
        emitters.Remove(id);
        if (!e.Player.IsDestroyed) RueDisplay.Get(e.Player).Remove(TrackTip);
        Destroy(e);
        if (emitters.Count == 0)
        {
            audible.Clear();
            selected.Clear();
        }
    }

    private static void Destroy(Emitter e)
    {
        if (e.Playback != null)
        {
            e.Playback.Condition = _ => false;
            foreach (int id in e.Playback.ClipsById.Keys.ToArray()) e.Playback.RemoveClipById(id);
            e.Playback.Destroy();
        }
        if (e.Personalization != null)
            foreach (Player player in Player.List) e.Personalization.ClearOverride(player);
        foreach (AdminToy toy in e.Toys) if (!toy.IsDestroyed) toy.Destroy();
    }

    internal void Reset()
    {
        foreach (uint id in emitters.Keys.ToArray()) Forget(id);
    }

    public void Dispose()
    {
        StaticUnityMethods.OnFixedUpdate -= Update;
        ServerSpecificSettingsSync.ServerOnSettingValueReceived -= TrackSettingReceived;
        enabled = false;
        Reset();
        foreach (string clip in clips) AudioClipStorage.DestroyClip(clip);
        clips.Clear();
    }
}
