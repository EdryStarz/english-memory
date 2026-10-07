using System.Text.Json;
namespace EnglishMemory.Core;

public enum CaptureSource { Microphone, PcAudio, Both }
public enum ItemStatus { New, Learning, Known, Mastered, Suspended, Available }
public enum Rating { Again = 1, Hard, Good, Easy }
public record Finding(string Target, string Translation, string Type, string Cefr, double Importance, double PersonalRelevance, string Topic, string? Correction = null);
public record Analysis(string SourceLanguage, string Quote, string NaturalEnglish, Finding[] Items);
public record Encounter(long Id, long ItemId, string Quote, string NaturalEnglish, string Source, string? Application, DateTimeOffset Timestamp, double? SourceSeconds, MediaContext? Media = null);
public record Vocabulary(long Id, string Target, string Translation, string Type, string Cefr, string Topic, ItemStatus Status, double Importance, double PersonalRelevance, int Encounters, DateTimeOffset FirstSeen, DateTimeOffset LastSeen, string Quote, string NaturalEnglish, string Source, double Stability, double Difficulty, DateTimeOffset Due, DateTimeOffset? LastReview, int Reviews, MediaContext? Media = null, double? SourceSeconds = null)
{
    public bool HasMedia => Media is not null;
    public string MediaLabel => Media is null ? "" : Media.Label + (SourceSeconds.HasValue ? $" · {TimeSpan.FromSeconds(SourceSeconds.Value):hh\\:mm\\:ss}" : "");
    public string Metadata => $"{Cefr} · {Topic} · {Encounters} encounter{(Encounters == 1 ? "" : "s")} · {Status}";
    public string Prompt => Source == "microphone" && !string.IsNullOrWhiteSpace(NaturalEnglish) && Quote.Any(c => c >= 'А' && c <= 'я') ? Quote : Target;
    public string Answer => Prompt == Quote ? NaturalEnglish : Translation;
}
public record Transcript(string Text, string Source, DateTimeOffset Time, string? Application = null, double? SourceSeconds = null, MediaContext? Media = null);
public record AudioChunk(float[] Samples, string Source, DateTimeOffset Time, MediaContext? Media = null);
public record Statistics(int Total, int Inbox, int Due, int Learning, int Mastered, int Reviews, double Retention, int Streak, double ListeningSeconds, int Encounters, double? Coverage);
public sealed class UserSettings
{
    public string Theme { get; set; } = "System";
    public string InterfaceLanguage { get; set; } = "ru";
    public bool MediaEnabled { get; set; }
    public bool MediaAutoDetect { get; set; } = true;
    public string MediaTitle { get; set; } = "";
    public string MediaTitleEvidence { get; set; } = "manual";
    public string? MediaTitleReference { get; set; }
    public string MediaKind { get; set; } = "unknown";
    public int? MediaSeason { get; set; }
    public int? MediaEpisode { get; set; }
    public string MediaEpisodeTitle { get; set; } = "";
    public string? MediaSubtitlePath { get; set; }
    public long MediaWindowHandle { get; set; }
    public int MediaWindowProcess { get; set; }
    public CaptureSource Source { get; set; } = CaptureSource.Microphone;
    public string? MicrophoneId { get; set; }
    public bool MinimizeToTray { get; set; } = true;
    public bool AutoCapture { get; set; } = true;
    public bool GamingMode { get; set; }
    public bool OnlyWhenIdle { get; set; }
    public bool SaveApplicationName { get; set; }
    public bool KeepAudio { get; set; }
    public int Threads { get; set; } = Math.Max(1, Environment.ProcessorCount / 2);
    public string Performance { get; set; } = "Balanced";
    public string MinimumCaptureLevel { get; set; } = "B1";
    public bool FilterBasicWords { get; set; } = true;
    public int MixNewPercent { get; set; } = 75;
    public bool ShowCardPictures { get; set; } = true;
    public int NewPerDay { get; set; } = 10;
    public int ReviewsPerDay { get; set; } = 100;
    public double Retention { get; set; } = .9;
    public string? SpeechModel { get; set; }
    public string? LanguageModel { get; set; }
    public string? Voice { get; set; }
    public int SpeechRate { get; set; }
    public string ListeningHotkey { get; set; } = "Ctrl+Shift+L";
    public string PrivateHotkey { get; set; } = "Ctrl+Shift+P";
    public string CaptureHotkey { get; set; } = "Ctrl+Shift+Space";
    public int WindowWidth { get; set; } = 1180;
    public int WindowHeight { get; set; } = 780;
    public int WindowX { get; set; } = 100;
    public int WindowY { get; set; } = 100;
    public static UserSettings Load(string path) { try { return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(path)) ?? new(); } catch { return new(); } }
    public void Save(string path) { File.WriteAllText(path + ".tmp", JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true })); File.Move(path + ".tmp", path, true); }
}
public static class DataPaths
{
    public static string Root { get; } = Environment.GetEnvironmentVariable("ENGLISH_MEMORY_DATA") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnglishMemory");
    public static string Models => Path.Combine(Root, "Models");
    public static string Settings => Path.Combine(Root, "settings.json");
    public static void Ensure() { Directory.CreateDirectory(Root); Directory.CreateDirectory(Models); }
}
