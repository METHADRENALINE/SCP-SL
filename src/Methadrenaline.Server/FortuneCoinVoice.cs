using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using LabApi.Features.Wrappers;
using Methadrenaline.Server.Core;
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

    internal sealed class SpeechPlayer(Player source) : IDisposable
    {
        internal const float Warmup = .25f;
        private SpeakerToy? speaker;
        private readonly string name = "md-coin-" + Guid.NewGuid().ToString("N");
        private AudioPlayer? audio;
        private float readyAt;
        private bool ownsClip;

        internal float ReadyIn => UnityEngine.Mathf.Max(0, readyAt - UnityEngine.Time.unscaledTime);

        internal bool Prepare()
        {
            if (audio != null) return true;
            audio = AudioPlayer.Create(name, destroyWhenAllClipsPlayed: false, sendSoundGlobally: false,
                condition: CanHear);
            if (audio == null) return false;
            speaker = SpeakerToy.Create(Vector3.zero, source.ReferenceHub.transform, networkSpawn: false);
            speaker.ControllerId = audio.ControllerID;
            speaker.Volume = 1f;
            speaker.IsSpatial = true;
            speaker.MinDistance = 5f;
            speaker.MaxDistance = FortuneCoinPolicy.VoiceRange;
            speaker.Position = Vector3.zero;
            speaker.Base.NetworkPosition = Vector3.zero;
            speaker.Spawn();
            readyAt = Time.unscaledTime + Warmup;
            return true;
        }

        private bool CanHear(ReferenceHub hub)
        {
            if (source.IsDestroyed || hub == null) return false;
            if (hub == source.ReferenceHub) return true;
            return Player.Get(hub) is { IsAlive: true } listener &&
                FortuneCoinPolicy.InEarshot((listener.Position - source.Position).sqrMagnitude);
        }

        internal void PlayCue(string clip) => audio?.AddClip(clip, loop: false);

        internal float Play(float[] samples)
        {
            if (audio == null) return 0;
            Clear();
            AudioClipStorage.AudioClips.Add(name, new AudioClipData(name, SampleRate, 1, samples));
            ownsClip = true;
            audio.AddClip(name, loop: false);
            return (float)samples.Length / SampleRate;
        }

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
            if (speaker is { IsDestroyed: false }) speaker.Destroy();
            speaker = null;
            if (audio == null) return;
            audio.Condition = null;
            audio.Owners.Clear();
            audio.Destroy();
            audio = null;
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
