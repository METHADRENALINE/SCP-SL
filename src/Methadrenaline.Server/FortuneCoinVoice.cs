using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabApi.Features.Wrappers;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace Methadrenaline.Server;

internal sealed class FortuneCoinVoice : IDisposable
{
    private const int SampleRate = 48000;
    private const int MaxSamples = SampleRate * 28;
    private readonly SemaphoreSlim pending = new(4);
    private readonly string misfortuneClip = "md-coin-misfortune-" + Guid.NewGuid().ToString("N");
    private Process? worker;
    private int port;
    internal string? MisfortuneClip { get; private set; }

    internal void Enable(Config config)
    {
        try
        {
            if (Path.IsPathRooted(config.FortuneCoinMisfortuneFile) &&
                AudioClipStorage.LoadClip(File.ReadAllBytes(config.FortuneCoinMisfortuneFile), misfortuneClip))
            {
                PrepareMisfortune(AudioClipStorage.AudioClips[misfortuneClip]);
                MisfortuneClip = misfortuneClip;
            }
        }
        catch (Exception error)
        {
            Logger.Warn("[METHADRENALINE] Fortune Coin misfortune audio unavailable: " + error.Message);
        }

        if (!Path.IsPathRooted(config.FortuneCoinVoiceCommand)) return;
        try
        {
            worker = Process.Start(new ProcessStartInfo(config.FortuneCoinVoiceCommand, config.FortuneCoinVoicePort.ToString())
            {
                UseShellExecute = false,
                CreateNoWindow = true
            });
            if (worker != null) port = config.FortuneCoinVoicePort;
        }
        catch (Exception error)
        {
            Logger.Warn("[METHADRENALINE] Fortune Coin voice unavailable: " + error.Message);
        }
    }

    internal bool Available => port != 0 && worker?.HasExited == false;

    internal Task<float[]?> Speak(string text, CancellationToken cancellation)
    {
        if (!Available || text.Length > 480 || !pending.Wait(0)) return Task.FromResult<float[]?>(null);
        int destination = port;
        return Task.Run(() =>
        {
            try
            {
                cancellation.ThrowIfCancellationRequested();
                byte[] payload = Encoding.UTF8.GetBytes("ru\n" + text);
                using var connection = new TcpClient(AddressFamily.InterNetwork) { SendTimeout = 2000, ReceiveTimeout = 6500 };
                using var abort = cancellation.Register(connection.Close);
                connection.Connect(IPAddress.Loopback, destination);
                using var stream = connection.GetStream();
                using var writer = new BinaryWriter(stream);
                writer.Write(payload.Length);
                writer.Write(payload);
                writer.Flush();
                using var reader = new BinaryReader(stream);
                int bytes = reader.ReadInt32();
                if (bytes < 2 || bytes > MaxSamples * 2 || bytes % 2 != 0) return null;
                var samples = new float[bytes / 2];
                for (int i = 0; i < samples.Length; i++) samples[i] = reader.ReadInt16() / 32768f;
                Amplify(samples, 1.65f);
                return samples;
            }
            catch (Exception error) when (error is SocketException or IOException or OperationCanceledException or ObjectDisposedException)
            {
                return null;
            }
            finally
            {
                pending.Release();
            }
        });
    }

    public void Dispose()
    {
        port = 0;
        if (worker != null)
        {
            try { if (!worker.HasExited) worker.Kill(); }
            catch (InvalidOperationException) { }
            worker.Dispose();
            worker = null;
        }
        if (MisfortuneClip != null) AudioClipStorage.DestroyClip(MisfortuneClip);
        MisfortuneClip = null;
    }

    internal sealed class Listener : IDisposable
    {
        internal const float Warmup = .25f;
        internal const float AudibleRange = 10000f;
        internal const float MisfortuneRange = 40f;
        private readonly Player recipient;
        private readonly string name = "md-coin-" + Guid.NewGuid().ToString("N");
        private AudioPlayer? audio;
        private float readyAt;
        private bool ownsClip;

        internal Listener(Player recipient) => this.recipient = recipient;
        internal float ReadyIn => UnityEngine.Mathf.Max(0, readyAt - UnityEngine.Time.unscaledTime);

        internal bool Prepare()
        {
            if (audio != null) return true;
            audio = AudioPlayer.Create(name, destroyWhenAllClipsPlayed: false, sendSoundGlobally: false,
                owners: new List<ReferenceHub> { recipient.ReferenceHub });
            if (audio == null) return false;
            var speaker = audio.AddSpeaker("coin", recipient.Position, volume: 1f, isSpatial: false, maxDistance: AudibleRange);
            if (speaker != null)
            {
                readyAt = UnityEngine.Time.unscaledTime + Warmup;
                return true;
            }
            Dispose();
            return false;
        }

        internal float Play(float[] samples)
        {
            if (audio == null) return 0;
            Clear();
            AudioClipStorage.AudioClips.Add(name, new AudioClipData(name, SampleRate, 1, samples));
            ownsClip = true;
            audio.AddClip(name, loop: false);
            return (float)samples.Length / SampleRate;
        }

        internal void Stop() => Clear();

        private void Clear()
        {
            if (audio != null)
                foreach (int clip in System.Linq.Enumerable.ToArray(audio.ClipsById.Keys)) audio.RemoveClipById(clip);
            if (ownsClip) AudioClipStorage.DestroyClip(name);
            ownsClip = false;
        }

        public void Dispose()
        {
            Clear();
            if (audio == null) return;
            audio.Condition = null;
            audio.Owners.Clear();
            audio.Destroy();
            audio = null;
        }
    }

    internal sealed class MisfortuneEmitter : IDisposable
    {
        private readonly AudioPlayer playback;
        private readonly Speaker speaker;
        private readonly Player source;

        private MisfortuneEmitter(AudioPlayer playback, Speaker speaker, Player source)
        {
            this.playback = playback;
            this.speaker = speaker;
            this.source = source;
            speaker.Base.NetworkIsStatic = false;
        }

        internal static MisfortuneEmitter? Create(Player source)
        {
            var audio = AudioPlayer.Create("md-coin-curse-" + Guid.NewGuid().ToString("N"),
                destroyWhenAllClipsPlayed: false, sendSoundGlobally: false,
                condition: hub => hub != null && (hub == source.ReferenceHub ||
                    Player.Get(hub) is { IsAlive: true } listener &&
                    (listener.Position - source.Position).sqrMagnitude <= Listener.MisfortuneRange * Listener.MisfortuneRange));
            if (audio == null) return null;
            var speaker = audio.AddSpeaker("curse", source.Position + Vector3.up, volume: 1f, isSpatial: true,
                minDistance: 9f, maxDistance: Listener.MisfortuneRange);
            if (speaker != null) return new MisfortuneEmitter(audio, speaker, source);
            audio.Destroy();
            return null;
        }

        internal void Play(string clip) => playback.AddClip(clip, loop: false);

        internal void Follow()
        {
            if (speaker == null || source.IsDestroyed) return;
            speaker.Position = source.Position + Vector3.up;
            speaker.Base.NetworkPosition = speaker.Position;
        }

        public void Dispose()
        {
            if (playback == null) return;
            playback.Condition = _ => false;
            playback.Destroy();
        }
    }

    internal static void PrepareMisfortune(AudioClipData clip)
    {
        float[] samples = clip.Samples;
        int frames = samples.Length / clip.Channels;
        int cut = Mathf.Min(clip.SampleRate / 4, frames / 2);
        int fade = Mathf.Min(clip.SampleRate / 40, frames - cut);
        Amplify(samples, 6f);
        int end = frames - cut;
        for (int frame = end - fade; frame < end; frame++)
        {
            float gain = (end - 1 - frame) / (float)fade;
            for (int channel = 0; channel < clip.Channels; channel++)
                samples[frame * clip.Channels + channel] *= gain;
        }
        Array.Clear(samples, end * clip.Channels, samples.Length - end * clip.Channels);
    }

    internal static void Amplify(float[] samples, float gain)
    {
        for (int i = 0; i < samples.Length; i++)
        {
            float value = samples[i] * gain;
            float magnitude = Mathf.Abs(value);
            if (magnitude > .75f)
                value = Mathf.Sign(value) * (1f - .0625f / (magnitude - .5f));
            samples[i] = value;
        }
    }
}
