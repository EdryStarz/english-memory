using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
namespace EnglishMemory.Core;

public record MediaContext(string Title, string Kind = "unknown", int? Season = null, int? Episode = null,
    string? EpisodeTitle = null, string? Reference = null, string Evidence = "manual", string? SubtitleFile = null)
{
    public string Label => Title + (Season.HasValue ? $" · S{Season:00}" : "") + (Episode.HasValue ? $"{(Season.HasValue ? "" : " · ")}E{Episode:00}" : "") + (string.IsNullOrWhiteSpace(EpisodeTitle) ? "" : " · " + EpisodeTitle);
    public string Describe(bool russian)
    {
        string evidence = Evidence switch { "manual" => russian ? "указано вами" : "entered by you", "video-file" => russian ? "из имени выбранного видеофайла" : "from selected video filename", "subtitle-file" => russian ? "из имени файла субтитров" : "from subtitle filename", "browser-window" => russian ? "название вкладки; источник звука не подтверждён" : "tab caption; audio origin unconfirmed", _ => russian ? "название окна плеера; источник звука не подтверждён" : "player caption; audio origin unconfirmed" };
        string kind = Kind switch { "movie" => russian ? "Фильм" : "Movie", "series" => russian ? "Сериал" : "Series", "animated-series" => russian ? "Мультсериал" : "Animated series", _ => russian ? "Видео" : "Video" };
        return $"{kind}: {Label}\n{evidence}" + (Reference is null ? "" : "\n" + Reference) + (SubtitleFile is null ? "" : "\n" + (russian ? "Субтитры: " : "Subtitles: ") + SubtitleFile);
    }
}
public record MediaWindow(long Handle, int ProcessId, string ProcessName, string Title)
{
    public override string ToString() => ProcessName + " · " + Title;
}
public record SubtitleMatch(double Seconds, string Text, double Score);

public static class MediaNames
{
    private static readonly Regex Episode = new(@"\b[Ss](?<s>\d{1,3})[ ._-]*[Ee](?<e>\d{1,3})\b|\b(?<s>\d{1,3})x(?<e>\d{1,3})\b|(?:season|сезон)\s*(?<s>\d{1,3})\D{0,15}(?:episode|серия)\s*(?<e>\d{1,3})", RegexOptions.IgnoreCase);
    public static MediaContext? Parse(string reference, string evidence = "player-window")
    {
        string name = Regex.Replace(reference.Trim(), @"\.(mkv|mp4|avi|webm|mov|m4v|srt|vtt)$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s*[-—|]\s*(Google Chrome|Microsoft Edge|Mozilla Firefox|Brave|Opera|VLC media player|MPC-HC|MPC-BE|PotPlayer|Media Player)\s*$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\.(mkv|mp4|avi|webm|mov|m4v|srt|vtt)$", "", RegexOptions.IgnoreCase);
        name = Regex.Replace(name, @"\s*[-—|]\s*(YouTube|Netflix|Disney\+|Prime Video|Crunchyroll|Hulu|Кинопоиск|Иви|Okko)\s*$", "", RegexOptions.IgnoreCase);
        if (Regex.IsMatch(name, @"^(?:[A-Za-z]:\\|\\\\)")) name = Path.GetFileName(name);
        name = name.Replace('.', ' ').Replace('_', ' ');
        var episode = Episode.Match(name);
        int? season = episode.Success ? int.Parse(episode.Groups["s"].Value) : null, number = episode.Success ? int.Parse(episode.Groups["e"].Value) : null;
        string title = (episode.Success ? name[..episode.Index] : name).Trim(' ', '-', '—', '[', ']', '(', ')');
        if (string.IsNullOrWhiteSpace(title) || Regex.IsMatch(title, @"^(VLC media player|MPC-HC|MPC-BE|PotPlayer|Google Chrome|Microsoft Edge|Mozilla Firefox|Media Player)$", RegexOptions.IgnoreCase)) return null;
        if (evidence is "player-window" or "browser-window" && Regex.IsMatch(title, @"^(YouTube|Netflix|Disney\+|Prime Video|Crunchyroll|Hulu|Кинопоиск|Иви|Okko)$", RegexOptions.IgnoreCase)) return null;
        // A caption is evidence of a title, never proof that mixed PC audio came from this window.
        return new(Regex.Replace(title, @"\s+", " "), episode.Success ? "series" : "unknown", season, number, Reference: reference, Evidence: evidence);
    }
}

public sealed class SubtitleIndex
{
    private readonly (double Seconds, string Text, string Normalized)[] cues;
    public SubtitleIndex(string path)
    {
        if (new FileInfo(path).Length > 20 * 1024 * 1024) throw new InvalidDataException("Subtitle file limit is 20 MB.");
        cues = Imports.Read(path).Where(t => t.Source == "subtitle" && t.SourceSeconds.HasValue).Take(20001)
            .Select(t => (t.SourceSeconds!.Value, t.Text, NormalizeLine(t.Text))).ToArray();
        if (cues.Length is 0 or > 20000) throw new InvalidDataException("Subtitles must contain 1–20,000 timed cues (SRT/VTT).");
    }
    public SubtitleMatch? Match(string transcript)
    {
        string query = NormalizeLine(transcript); string[] words = query.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (query.Length < 12 || words.Length < 4) return null; // Short common lines cannot establish a timestamp.
        var matches = new List<SubtitleMatch>();
        for (int i = 0; i < cues.Length; i++)
        {
            string text = "", candidate = "";
            for (int span = 0; span < 3 && i + span < cues.Length; span++)
            {
                if (span > 0 && cues[i + span].Seconds - cues[i + span - 1].Seconds > 8) break;
                text += (span == 0 ? "" : " ") + cues[i + span].Text;
                candidate += (span == 0 ? "" : " ") + cues[i + span].Normalized;
                var tokens = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (Math.Abs(tokens.Length - words.Length) > Math.Max(2, words.Length / 4)) continue;
                if (new[] { "not", "never", "no", "не" }.Any(negative => words.Contains(negative) != tokens.Contains(negative))) continue;
                double overlap = (double)words.Distinct().Intersect(tokens.Distinct()).Count() / Math.Max(words.Distinct().Count(), tokens.Distinct().Count());
                if (overlap < .75) continue;
                double score = 1 - (double)TokenDistance(words, tokens) / Math.Max(words.Length, tokens.Length);
                if (score >= .90) matches.Add(new(cues[i].Seconds, text, score));
            }
        }
        var ranked = matches.OrderByDescending(m => m.Score).ToList();
        if (ranked.Count == 0) return null;
        var best = ranked[0];
        // Repeated or near-identical lines at different moments are ambiguous; do not invent a time.
        return ranked.Skip(1).Any(m => Math.Abs(m.Seconds - best.Seconds) > 2 && m.Score >= best.Score - .04) ? null : best;
    }
    private static int TokenDistance(string[] a, string[] b)
    {
        int[] row = Enumerable.Range(0, b.Length + 1).ToArray();
        for (int i = 1; i <= a.Length; i++) { int diagonal = row[0]; row[0] = i; for (int j = 1; j <= b.Length; j++) { int previous = row[j]; row[j] = Math.Min(Math.Min(row[j] + 1, row[j - 1] + 1), diagonal + (a[i - 1] == b[j - 1] ? 0 : 1)); diagonal = previous; } }
        return row[b.Length];
    }
    private static string NormalizeLine(string text)
    {
        string line = Database.Normalize(text);
        foreach (var (shortForm, full) in new[] { ("can't", "can not"), ("cannot", "can not"), ("won't", "will not"), ("shan't", "shall not"), ("i'm", "i am") }) line = Regex.Replace(line, @"\b" + Regex.Escape(shortForm) + @"\b", full);
        line = Regex.Replace(line, @"\b(\w+)n't\b", "$1 not");
        line = Regex.Replace(line, @"\b(\w+)'ll\b", "$1 will");
        line = Regex.Replace(line, @"\b(\w+)'re\b", "$1 are");
        return Regex.Replace(line, @"\b(\w+)'ve\b", "$1 have");
    }
}

public sealed class MediaTracker(UserSettings settings)
{
    private readonly object subtitleGate = new();
    private string? loadedPath; private DateTime loadedWrite; private SubtitleIndex? subtitles;
    public MediaContext? Snapshot(string source, string? subtitleFile = null)
    {
        if (!settings.MediaEnabled || source is not ("pc_audio" or "subtitle" or "media_manual")) return null;
        if (!string.IsNullOrWhiteSpace(settings.MediaTitle)) return new(settings.MediaTitle.Trim(), settings.MediaKind, settings.MediaSeason, settings.MediaEpisode, NullIfEmpty(settings.MediaEpisodeTitle), Reference: settings.MediaTitleReference, Evidence: settings.MediaTitleEvidence, SubtitleFile: source == "subtitle" && subtitleFile is not null ? Path.GetFileName(subtitleFile) : null);
        if (subtitleFile is not null) { var parsed = MediaNames.Parse(Path.GetFileName(subtitleFile), "subtitle-file"); return parsed is null ? null : parsed with { SubtitleFile = Path.GetFileName(subtitleFile) }; }
        if (settings.MediaAutoDetect && source == "pc_audio")
        {
            var window = MediaWindows.Read(settings.MediaWindowHandle == 0 ? MediaWindows.Foreground : settings.MediaWindowHandle);
            if (window is not null && (settings.MediaWindowHandle == 0 || window.ProcessId == settings.MediaWindowProcess))
            {
                var parsed = MediaNames.Parse(window.Title, MediaWindows.IsBrowser(window.ProcessName) ? "browser-window" : "player-window");
                // Unpinned browser windows may contain mail/documents rather than video.
                if (parsed is not null && (settings.MediaWindowHandle != 0 || !MediaWindows.IsBrowser(window.ProcessName) || parsed.Episode is not null || Regex.IsMatch(window.Title, @"YouTube|Netflix|Disney\+|Prime Video|Crunchyroll|Hulu|Кинопоиск|Иви|Okko", RegexOptions.IgnoreCase))) return parsed;
            }
        }
        return string.IsNullOrWhiteSpace(settings.MediaSubtitlePath) ? null : MediaNames.Parse(Path.GetFileName(settings.MediaSubtitlePath), "subtitle-file");
    }
    public Transcript Enrich(Transcript transcript)
    {
        if (transcript.Source is not ("pc_audio" or "media_manual") || transcript.Media is null || !File.Exists(settings.MediaSubtitlePath)) return transcript;
        var name = MediaNames.Parse(Path.GetFileName(settings.MediaSubtitlePath), "subtitle-file");
        if (name is not null && ((name.Season.HasValue && transcript.Media.Season.HasValue && name.Season != transcript.Media.Season) || (name.Episode.HasValue && transcript.Media.Episode.HasValue && name.Episode != transcript.Media.Episode))) return transcript;
        if (name is not null && transcript.Media.Evidence != "manual" && !Database.Normalize(name.Title).Contains(Database.Normalize(transcript.Media.Title)) && !Database.Normalize(transcript.Media.Title).Contains(Database.Normalize(name.Title))) return transcript;
        lock (subtitleGate)
        {
            var write = File.GetLastWriteTimeUtc(settings.MediaSubtitlePath!);
            if (loadedPath != settings.MediaSubtitlePath || write != loadedWrite) { subtitles = new(settings.MediaSubtitlePath!); loadedPath = settings.MediaSubtitlePath; loadedWrite = write; }
            var match = subtitles!.Match(transcript.Text);
            return match is null ? transcript : transcript with { SourceSeconds = match.Seconds, Media = transcript.Media with { SubtitleFile = Path.GetFileName(settings.MediaSubtitlePath) } };
        }
    }
    private static string? NullIfEmpty(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

public static class MediaWindows
{
    private static readonly HashSet<string> Browsers = new(StringComparer.OrdinalIgnoreCase) { "chrome", "msedge", "firefox", "brave", "opera" };
    private static readonly HashSet<string> Players = new(StringComparer.OrdinalIgnoreCase) { "vlc", "mpc-hc", "mpc-hc64", "mpc-be", "mpc-be64", "PotPlayerMini", "PotPlayerMini64", "wmplayer", "Microsoft.Media.Player", "Video.UI", "mpv" };
    public static bool IsBrowser(string name) => Browsers.Contains(name);
    public static long Foreground => GetForegroundWindow().ToInt64();
    public static MediaWindow? Read(long handle)
    {
        try
        {
            if (handle == 0 || !IsWindowVisible(new(handle))) return null;
            GetWindowThreadProcessId(new(handle), out uint pid); using var process = Process.GetProcessById((int)pid);
            string name = process.ProcessName; if (!IsBrowser(name) && !Players.Contains(name)) return null;
            var title = new StringBuilder(1024); GetWindowText(new(handle), title, title.Capacity);
            return title.Length == 0 ? null : new(handle, (int)pid, name, title.ToString());
        }
        catch { return null; }
    }
    public static List<MediaWindow> List()
    {
        var windows = new List<MediaWindow>(); EnumWindows((handle, _) => { var window = Read(handle.ToInt64()); if (window is not null) windows.Add(window); return true; }, 0); return windows;
    }
    private delegate bool EnumWindowsProc(nint handle, nint parameter);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);
    [DllImport("user32.dll")] private static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(nint handle);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowText(nint handle, StringBuilder text, int length);
}
