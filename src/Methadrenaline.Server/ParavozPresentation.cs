using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using AdminToys;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using SecretLabNAudio.Core;
using UnityEngine;
using LightToy = LabApi.Features.Wrappers.LightSourceToy;
using PrimitiveToy = LabApi.Features.Wrappers.PrimitiveObjectToy;
using SpeakerToy = LabApi.Features.Wrappers.SpeakerToy;

namespace Methadrenaline.Server;

internal sealed class ParavozPresentation : IDisposable
{
    internal sealed class Emitter(Player player)
    {
        internal readonly Player Player = player;
        internal readonly int Life = player.LifeId;
        internal readonly List<AdminToy> Toys = new();
        internal SpeakerToy Speaker = null!;
        internal SpeakerPersonalization Personalization = null!;
        internal LightToy Light = null!;
        internal readonly List<PrimitiveToy> Embers = new();
        internal readonly List<PrimitiveToy> Flames = new();
    }

    private readonly string clipName = "md-paravoz-" + Guid.NewGuid().ToString("N");
    private readonly Dictionary<uint, Emitter> emitters = new();
    private readonly Dictionary<Player, uint?> selected = new();
    private readonly HashSet<ReferenceHub> audible = new();
    private float nextUpdate;
    private bool enabled;
    internal AudioPlayer? Playback { get; private set; }
    internal IReadOnlyDictionary<uint, Emitter> Emitters => emitters;
    internal string ClipName => clipName;

    internal void Enable()
    {
        using var source = typeof(ServerPlugin).Assembly.GetManifestResourceStream("Methadrenaline.Server.Audio.paravoz-track.ogg")
            ?? throw new InvalidOperationException("Paravoz music resource is missing.");
        using var bytes = new MemoryStream();
        source.CopyTo(bytes);
        if (!AudioClipStorage.LoadClip(bytes.ToArray(), clipName)) throw new InvalidOperationException("Paravoz music could not be decoded.");
        var clip = AudioClipStorage.AudioClips[clipName];
        if (clip.SampleRate != ParavozPresentationPolicy.SampleRate || clip.Channels != 1)
            throw new InvalidOperationException("Paravoz music must be 48000 Hz mono Vorbis.");
        AudioClipStorage.AudioClips[clipName] = new AudioClipData(clipName, clip.SampleRate, 1, ParavozPresentationPolicy.LoopSamples(clip.Samples));
        enabled = true;
        StaticUnityMethods.OnFixedUpdate += Update;
    }

    internal void Apply(Player player)
    {
        if (!enabled) throw new InvalidOperationException("Paravoz presentation is not enabled.");
        Forget(player.NetworkId);
        if (Playback == null)
        {
            Playback = AudioPlayer.Create(clipName, destroyWhenAllClipsPlayed: false, sendSoundGlobally: false,
                condition: hub => audible.Contains(hub));
            if (Playback == null) throw new InvalidOperationException("No audio controller available for Paravoz music.");
            Playback.AddClip(clipName, loop: true, destroyOnEnd: false);
        }
        var emitter = new Emitter(player);
        try
        {
            Transform parent = player.ReferenceHub.transform;
            emitter.Speaker = SpeakerToy.Create(Vector3.zero, parent, networkSpawn: false);
            emitter.Toys.Add(emitter.Speaker);
            emitter.Speaker.ControllerId = Playback.ControllerID;
            emitter.Speaker.IsSpatial = true;
            // The spawn message is silent. Only one source receives a nonzero
            // per-viewer volume, including for late joiners and spectators.
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
        }
        catch
        {
            emitters.Remove(player.NetworkId);
            Destroy(emitter);
            if (emitters.Count == 0) StopPlayback();
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
        // NPCs are included for native diagnostics; only ready network clients
        // can receive the target sync or audio packets.
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
            if (chosen.HasValue) audible.Add(viewer.ReferenceHub);
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
            // Stay below and behind the eyes; no first-person screen overlay.
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
        Destroy(e);
        if (emitters.Count == 0) StopPlayback();
    }

    private static void Destroy(Emitter e)
    {
        if (e.Personalization != null)
            foreach (Player player in Player.List) e.Personalization.ClearOverride(player);
        foreach (AdminToy toy in e.Toys) if (!toy.IsDestroyed) toy.Destroy();
    }

    private void StopPlayback()
    {
        audible.Clear();
        selected.Clear();
        if (Playback == null) return;
        Playback.Condition = _ => false;
        Playback.SendSoundGlobally = false;
        foreach (int id in Playback.ClipsById.Keys.ToArray()) Playback.RemoveClipById(id);
        Playback.Destroy();
        Playback = null;
    }

    internal void Reset()
    {
        foreach (uint id in emitters.Keys.ToArray()) Forget(id);
        StopPlayback();
    }

    public void Dispose()
    {
        StaticUnityMethods.OnFixedUpdate -= Update;
        enabled = false;
        Reset();
        if (AudioClipStorage.AudioClips.ContainsKey(clipName)) AudioClipStorage.DestroyClip(clipName);
    }
}
