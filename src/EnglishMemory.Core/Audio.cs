using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
namespace EnglishMemory.Core;

public sealed class RollingBuffer
{
    private readonly float[] data; private int next, count; private readonly object gate = new();
    public RollingBuffer(int seconds = 30) => data = new float[seconds * 16000];
    public void Add(ReadOnlySpan<float> samples) { lock (gate) { foreach (var s in samples) { data[next] = s; next = (next + 1) % data.Length; count = Math.Min(count + 1, data.Length); } } }
    public float[] Snapshot() { lock (gate) { var a = new float[count]; for (int i = 0; i < count; i++) a[i] = data[(next - count + i + data.Length) % data.Length]; return a; } }
    public void Clear() { lock (gate) { Array.Clear(data); next = count = 0; } }
}
// Conservative energy VAD; not a music classifier. A production Silero backend can replace it.
public sealed class SpeechSegmenter
{
    private readonly List<float> segment = new(); private int silence; private bool active; private int voiced;
    public float[]? Add(float[] samples)
    {
        double rms = Math.Sqrt(samples.Sum(x => (double)x * x) / Math.Max(1, samples.Length)); bool speech = rms > .012;
        if (speech) { active = true; silence = 0; voiced += samples.Length; } else silence += samples.Length;
        if (active) segment.AddRange(samples);
        if (active && (silence >= 12800 || segment.Count >= 16000 * 25)) { var result = voiced >= 4000 ? segment.ToArray() : null; Clear(); return result; }
        return null;
    }
    public void Clear() { for (int i = 0; i < segment.Count; i++) segment[i] = 0; segment.Clear(); silence = voiced = 0; active = false; }
}
public sealed class AudioCapture : IDisposable, IMMNotificationClient
{
    private readonly List<(WasapiCapture Capture, MMDevice Device)> captures = new();
    private readonly Dictionary<string, RollingBuffer> buffers = new() { { "microphone", new() }, { "pc_audio", new() } };
    private readonly Dictionary<string, SpeechSegmenter> vad = new() { { "microphone", new() }, { "pc_audio", new() } };
    private readonly MMDeviceEnumerator enumerator = new(); private bool running; private readonly object gate = new();
    public event Action<AudioChunk>? Chunk;
    public event Action<Exception>? Error;
    public event Action? DefaultDeviceChanged;
    public bool AutoCapture { get; set; } = true;
    public AudioCapture() => enumerator.RegisterEndpointNotificationCallback(this);
    public List<(string Id, string Name)> Microphones() { var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active); return devices.Select(d => { var r = (d.ID, d.FriendlyName); d.Dispose(); return r; }).ToList(); }
    public void Start(CaptureSource source, string? microphoneId)
    {
        Stop(); running = true;
        try
        {
            if (source is CaptureSource.Microphone or CaptureSource.Both) Add(microphoneId is null ? enumerator.GetDefaultAudioEndpoint(DataFlow.Capture, Role.Multimedia) : enumerator.GetDevice(microphoneId), "microphone", false);
            if (source is CaptureSource.PcAudio or CaptureSource.Both) Add(enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia), "pc_audio", true);
        }
        catch { Stop(); throw; }
    }
    private void Add(MMDevice device, string source, bool loopback)
    {
        WasapiCapture cap = loopback ? new WasapiLoopbackCapture(device) : new WasapiCapture(device);
        var buffered = new BufferedWaveProvider(cap.WaveFormat) { BufferDuration = TimeSpan.FromSeconds(3), DiscardOnBufferOverflow = true, ReadFully = false };
        ISampleProvider samples = buffered.ToSampleProvider(); if (samples.WaveFormat.Channels > 1) samples = new MonoProvider(samples);
        var resampled = new WdlResamplingSampleProvider(samples, 16000); var frame = new float[4096];
        cap.DataAvailable += (_, e) =>
        {
            lock (gate)
            {
                if (!running) return;
                buffered.AddSamples(e.Buffer, 0, e.BytesRecorded); int n;
                while ((n = resampled.Read(frame, 0, frame.Length)) > 0) { var a = frame.AsSpan(0, n).ToArray(); buffers[source].Add(a); var chunk = vad[source].Add(a); if (chunk is not null && AutoCapture) Chunk?.Invoke(new(chunk, source, DateTimeOffset.UtcNow)); else if (chunk is not null) Array.Clear(chunk); Array.Clear(a); }
                Array.Clear(frame); Array.Clear(e.Buffer, 0, e.BytesRecorded);
            }
        };
        cap.RecordingStopped += (_, e) => { if (e.Exception is not null) Error?.Invoke(e.Exception); };
        captures.Add((cap, device)); cap.StartRecording();
    }
    public void CaptureRecent() { lock (gate) { if (!running) return; foreach (var (source, buffer) in buffers) { var a = buffer.Snapshot(); if (a.Length > 4000) Chunk?.Invoke(new(a, source, DateTimeOffset.UtcNow)); else Array.Clear(a); buffer.Clear(); } } }
    public void Stop()
    {
        lock (gate) { running = false; }
        foreach (var (c, d) in captures) { try { c.StopRecording(); } finally { c.Dispose(); d.Dispose(); } }
        captures.Clear();
        lock (gate) { foreach (var b in buffers.Values) b.Clear(); foreach (var v in vad.Values) v.Clear(); }
    }
    public void Dispose() { Stop(); enumerator.UnregisterEndpointNotificationCallback(this); enumerator.Dispose(); }
    public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) { if (role == Role.Multimedia) DefaultDeviceChanged?.Invoke(); }
    public void OnDeviceStateChanged(string deviceId, DeviceState newState) { }
    public void OnDeviceAdded(string deviceId) { }
    public void OnDeviceRemoved(string deviceId) => DefaultDeviceChanged?.Invoke();
    public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }
    private sealed class MonoProvider(ISampleProvider input) : ISampleProvider
    {
        public WaveFormat WaveFormat { get; } = WaveFormat.CreateIeeeFloatWaveFormat(input.WaveFormat.SampleRate, 1);
        public int Read(float[] buffer, int offset, int count) { int ch = input.WaveFormat.Channels; var temp = new float[count * ch]; int n = input.Read(temp, 0, temp.Length); int frames = n / ch; for (int i = 0; i < frames; i++) { float sum = 0; for (int j = 0; j < ch; j++) sum += temp[i * ch + j]; buffer[offset + i] = sum / ch; } Array.Clear(temp); return frames; }
    }
}
