using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace EnglishMemory.Core;

public static class Imports
{
    public static IEnumerable<Transcript> Read(string path)
    {
        string text = File.ReadAllText(path); string ext = Path.GetExtension(path).ToLowerInvariant(); var now = DateTimeOffset.UtcNow;
        if (ext is ".srt" or ".vtt")
        {
            var blocks = Regex.Split(text.Replace("\r", ""), @"\n\s*\n");
            foreach (var b in blocks) { var lines = b.Split('\n'); int ti = Array.FindIndex(lines, l => l.Contains("-->")); if (ti < 0) continue; string stamp = lines[ti].Split("-->")[0].Trim(); var match = Regex.Match(stamp, @"^(?:(?<h>\d{1,3}):)?(?<m>\d{2}):(?<s>\d{2})(?:[.,](?<f>\d{1,3}))?$"); if (!match.Success) continue; int minutes = int.Parse(match.Groups["m"].Value), seconds = int.Parse(match.Groups["s"].Value); if (minutes > 59 || seconds > 59) continue; double time = (match.Groups["h"].Success ? int.Parse(match.Groups["h"].Value) * 3600 : 0) + minutes * 60 + seconds + (match.Groups["f"].Success ? int.Parse(match.Groups["f"].Value) / Math.Pow(10, match.Groups["f"].Length) : 0); var quote = Regex.Replace(string.Join(" ", lines.Skip(ti + 1)), "<[^>]+>", "").Trim(); if (quote.Length > 0) yield return new(quote, "subtitle", now, null, time); }
        }
        else if (ext == ".json")
        {
            using var doc = JsonDocument.Parse(text);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) throw new InvalidDataException("Import JSON must be an array of strings or objects with a text field.");
            foreach (var item in doc.RootElement.EnumerateArray()) { var s = item.ValueKind == JsonValueKind.String ? item.GetString() : item.GetProperty("text").GetString(); if (!string.IsNullOrWhiteSpace(s)) yield return new(s, "import", now); }
        }
        else if (ext == ".csv")
        {
            foreach (var row in Csv(text)) { if (row.Count == 0 || string.IsNullOrWhiteSpace(row[0]) || row[0].Equals("text", StringComparison.OrdinalIgnoreCase)) continue; yield return new(row[0], "import", now); }
        }
        else if (ext == ".txt") foreach (var paragraph in Regex.Split(text, @"\r?\n\s*\r?\n")) if (!string.IsNullOrWhiteSpace(paragraph)) yield return new(paragraph.Trim(), "import", now);
        else throw new InvalidDataException("Supported files: TXT, CSV, JSON, SRT, VTT.");
    }
    private static IEnumerable<List<string>> Csv(string text)
    {
        var row = new List<string>(); var field = new StringBuilder(); bool quoted = false;
        for (int i = 0; i < text.Length; i++) { char c = text[i]; if (c == '"') { if (quoted && i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; } else quoted = !quoted; } else if (!quoted && c == ',') { row.Add(field.ToString()); field.Clear(); } else if (!quoted && c == '\n') { row.Add(field.ToString().TrimEnd('\r')); field.Clear(); yield return row; row = []; } else field.Append(c); }
        if (quoted) throw new InvalidDataException("Unclosed CSV quote."); if (field.Length > 0 || row.Count > 0) { row.Add(field.ToString()); yield return row; }
    }
}
