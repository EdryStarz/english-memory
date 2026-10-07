using System.Threading.Channels;
using System.Speech.Synthesis;
namespace EnglishMemory.Core;

public sealed class Engine : IAsyncDisposable
{
    public Database Db { get; }
    public UserSettings Settings { get; }
    public AudioCapture Audio { get; } = new();
    public LocalAi Ai { get; }
    public MediaTracker Media { get; }
    private readonly CancellationTokenSource lifetime = new();
    private CancellationTokenSource captureToken = new();
    private readonly Channel<(AudioChunk Chunk, int Generation)> audio = Channel.CreateBounded<(AudioChunk, int)>(new BoundedChannelOptions(8) { FullMode = BoundedChannelFullMode.Wait, SingleReader = true });
    private readonly Task speechWorker, languageWorker;
    private readonly object captureGate = new();
    private int generation; private DateTimeOffset started; private bool resumeAfterPrivate;
    public bool Listening { get; private set; }
    public bool Private { get; private set; }
    public bool Processing { get; private set; }
    public bool HasListened { get; private set; }
    public string? CaptureError { get; private set; }
    public int DroppedChunks { get; private set; }
    public event Action? Changed;
    public event Action<string>? Message;
    public event Action<Transcript>? Transcribed;
    public string Status => Private ? "Private mode · capture stopped" : CaptureError is not null ? "Capture error · listening stopped" : Listening ? (Processing ? "Listening · processing locally" : "Listening · audio stays on this PC") : (Processing ? "Processing locally" : HasListened ? "Paused · listening is off" : "Idle · listening is off");
    public Engine()
    {
        DataPaths.Ensure(); Settings = UserSettings.Load(DataPaths.Settings); Settings.KeepAudio = false;
        if (!File.Exists(Settings.SpeechModel) && File.Exists(Path.Combine(DataPaths.Models, "ggml-base.bin"))) Settings.SpeechModel = Path.Combine(DataPaths.Models, "ggml-base.bin");
        if (!File.Exists(Settings.LanguageModel) && File.Exists(Path.Combine(DataPaths.Models, "qwen2.5-1.5b-instruct-q4_k_m.gguf"))) Settings.LanguageModel = Path.Combine(DataPaths.Models, "qwen2.5-1.5b-instruct-q4_k_m.gguf");
        Db = new(DataPaths.Root); Ai = new(Settings); Media = new(Settings);
        Audio.Chunk += c => { if (!audio.Writer.TryWrite((c with { Media = Media.Snapshot(c.Source) }, generation))) { Array.Clear(c.Samples); DroppedChunks++; Message?.Invoke("Audio queue is full. One chunk was discarded; capture remains active."); } };
        Audio.Error += e => Task.Run(() => { Stop(); CaptureError = e.Message; Changed?.Invoke(); Message?.Invoke("Audio capture stopped: " + e.Message); });
        Audio.DefaultDeviceChanged += () => Task.Run(() => { if (Listening && Settings.MicrophoneId is null) { try { Stop(); Start(); } catch (Exception e) { Message?.Invoke(e.Message); } } });
        speechWorker = Task.Run(SpeechLoop); languageWorker = Task.Run(LanguageLoop);
    }
    public void Start()
    {
        lock (captureGate)
        {
            if (Private) throw new InvalidOperationException("Turn off Private Mode before listening.");
            if (!Ai.SpeechAvailable) throw new InvalidOperationException("Download or import a speech model in Models before listening.");
            if (Listening) return; captureToken.Dispose(); captureToken = new(); generation++; Audio.AutoCapture = Settings.AutoCapture; Audio.Start(Settings.Source, Settings.MicrophoneId); started = DateTimeOffset.UtcNow; Listening = HasListened = true; CaptureError = null; Changed?.Invoke();
        }
    }
    public void Stop()
    {
        lock (captureGate)
        {
            captureToken.Cancel(); generation++; Audio.Stop(); while (audio.Reader.TryRead(out var x)) Array.Clear(x.Chunk.Samples);
            if (Listening) Db.Session(started, (DateTimeOffset.UtcNow - started).TotalSeconds, Settings.Source); Listening = false; Changed?.Invoke();
        }
    }
    public void TogglePrivate()
    {
        lock (captureGate)
        {
            if (!Private) { resumeAfterPrivate = Listening; Private = true; Stop(); }
            else { Private = false; if (resumeAfterPrivate) Start(); }
            Changed?.Invoke();
        }
    }
    public void AddText(string text, string source = "manual")
    {
        if (string.IsNullOrWhiteSpace(text)) throw new ArgumentException("Enter a phrase or paragraph."); if (text.Length > 100000) throw new ArgumentException("Import shorter text (up to 100,000 characters per submission).");
        // Bound context to avoid truncation in a 4096-token model context.
        for (int i = 0; i < text.Length; i += 1800) Db.Enqueue(Media.Enrich(new(text.Substring(i, Math.Min(1800, text.Length - i)), source, DateTimeOffset.UtcNow, Media: Media.Snapshot(source)))); Changed?.Invoke();
    }
    public int Import(string path) { int n = 0; foreach (var t in Imports.Read(path)) { if (n >= 20000) throw new InvalidDataException("Import limit is 20,000 fragments."); for (int i = 0; i < t.Text.Length; i += 1800) Db.Enqueue(t with { Text = t.Text.Substring(i, Math.Min(1800, t.Text.Length - i)), Media = Media.Snapshot(t.Source, path) }); n++; } Changed?.Invoke(); return n; }
    private async Task SpeechLoop()
    {
        try
        {
            await foreach (var entry in audio.Reader.ReadAllAsync(lifetime.Token))
            {
                try
                {
                    if (entry.Generation != generation || Private) continue;
                    var ct = captureToken.Token; string text = await Ai.Transcribe(entry.Chunk.Samples, ct);
                    if (entry.Generation != generation || Private || text.Length < 4 || text is "[BLANK_AUDIO]" or "[Music]" || text.StartsWith('[')) continue;
                    var t = new Transcript(text, entry.Chunk.Source, entry.Chunk.Time, Media: entry.Chunk.Media); try { t = Media.Enrich(t); } catch (Exception ex) { Message?.Invoke("Subtitle matching: " + ex.Message); } Db.Enqueue(t); Transcribed?.Invoke(t); Changed?.Invoke();
                }
                catch (OperationCanceledException) { }
                catch (Exception e) { Message?.Invoke("Transcription: " + e.Message); }
                finally { Array.Clear(entry.Chunk.Samples); }
            }
        }
        catch (OperationCanceledException) { }
    }
    private async Task LanguageLoop()
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
        try
        {
            while (await timer.WaitForNextTickAsync(lifetime.Token))
            {
                if (Settings.GamingMode || Private || !Ai.LanguageAvailable || (Settings.OnlyWhenIdle && WindowsIdle.Seconds < 60)) continue;
                var next = Db.NextQueued(); if (next is null) continue;
                // Collect speech for up to 30 seconds; manual input runs promptly.
                if (next.Value.Transcript.Source is "microphone" or "pc_audio" && (DateTimeOffset.UtcNow - next.Value.Transcript.Time).TotalSeconds < 30) continue;
                Processing = true; Changed?.Invoke();
                try { var t = next.Value.Transcript; var key = LocalAi.CacheKey(t.Text, Settings.LanguageModel); var basic = LearningFilter.ShouldSkipUtterance(t.Text, t.Source, Settings); var explicitBasic = !basic ? LearningFilter.ExplicitBasic(t.Text) : null; var a = basic ? new Analysis("en", t.Text, t.Text, []) : explicitBasic is not null ? new Analysis("en", t.Text, t.Text, [explicitBasic]) : Db.Cached(key) ?? await Ai.Analyze(t.Text, lifetime.Token); var saved = Db.SaveAnalysis(a, t, Settings); if (!basic) Db.Cache(key, a); Db.Finish(next.Value.Id); if (saved > 0) Message?.Invoke(Settings.InterfaceLanguage == "ru" ? $"Добавлено фраз: {saved}. Они появятся в миксе." : $"{saved} phrases added to the mix."); }
                catch (OperationCanceledException) { break; }
                catch (Exception e) { Db.Finish(next.Value.Id, e.Message); Message?.Invoke(e.Message); }
                finally { Processing = false; Changed?.Invoke(); }
            }
        }
        catch (OperationCanceledException) { }
    }
    public void SaveSettings() => Settings.Save(DataPaths.Settings);
    public async ValueTask DisposeAsync() { Stop(); lifetime.Cancel(); audio.Writer.TryComplete(); await Task.WhenAll(speechWorker, languageWorker); Audio.Dispose(); Ai.Dispose(); SaveSettings(); captureToken.Dispose(); lifetime.Dispose(); }
}
public static class WindowsIdle
{
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)] private struct LastInput { public uint Size, Tick; }
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LastInput info);
    public static double Seconds { get { var i = new LastInput { Size = 8 }; return GetLastInputInfo(ref i) ? unchecked((uint)Environment.TickCount - i.Tick) / 1000d : 0; } }
}
public sealed class Speaker : IDisposable
{
    private readonly SpeechSynthesizer synth = new();
    public List<string> Voices => synth.GetInstalledVoices().Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en").Select(v => v.VoiceInfo.Name).ToList();
    public void Speak(string text, string? voice, int rate) { synth.SpeakAsyncCancelAll(); if (!string.IsNullOrWhiteSpace(voice)) synth.SelectVoice(voice); else { var english = Voices.FirstOrDefault(); if (english is null) throw new InvalidOperationException("Install an English Windows voice in Language settings."); synth.SelectVoice(english); } synth.Rate = Math.Clamp(rate, -5, 5); synth.SpeakAsync(text); }
    public void Dispose() => synth.Dispose();
}
