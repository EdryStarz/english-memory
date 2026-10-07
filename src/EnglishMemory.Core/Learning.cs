using System.Text.RegularExpressions;
namespace EnglishMemory.Core;

public static class LearningFilter
{
    private static readonly HashSet<string> Basic = new(("a|an|the|i|you|he|she|it|we|they|me|my|your|his|her|our|their|this|that|these|those|and|or|but|is|are|am|was|were|be|been|have|has|had|do|does|did|can|will|would|yes|no|not|hello|hi|hey|bye|goodbye|thanks|thank you|please|sorry|okay|ok|whoa|wow|oh|ah|huh|hmm|uh|um|yeah|yep|nope|well|gonna|wanna|gotta|man|woman|boy|girl|child|children|mother|father|mom|dad|brother|sister|friend|family|person|people|house|home|room|door|window|table|chair|bed|car|bus|train|school|book|pen|phone|water|milk|bread|apple|food|coffee|tea|cat|dog|bird|fish|day|week|month|year|today|tomorrow|yesterday|morning|night|time|one|two|three|four|five|six|seven|eight|nine|ten|first|second|big|small|good|bad|new|old|young|happy|sad|hot|cold|red|blue|green|black|white|nice|very|really|go|come|get|want|like|know|see|look|say|tell|make|take|give|eat|drink|sleep|run|walk|read|write|work|play|watch|listen|love|help|where|when|why|how|what|here|there|now|then|all|some|any|much|many|more|most|with|without|for|from|to|of|in|on|at|up|down|out|back|good morning|good night|good evening|good afternoon|how are you|i am fine|i'm fine|see you|see you later|good to see you|nice to meet you|what is your name|my name is|i love you|i like you|i know|i don't know|i do not know|i want|i need|i am|i'm|you are|he is|she is|it is|this is|that is|there is|there are|not this|like you|who what|thank you very much|you're welcome|you are welcome|come here|go home|let's go|lets go|let me see|a lot|a little|right now|of course|no problem|all right|have a nice day").Split('|'), StringComparer.Ordinal);
    public static int Level(string? level) => level switch { "A1" => 1, "A2" => 2, "B1" => 3, "B2" => 4, "C1" => 5, "C2" => 6, _ => 3 };
    public static bool IsAutomatic(string source) => source is "pc_audio" or "microphone" or "subtitle";
    public static bool IsBasic(string target) => Basic.Contains(Database.Normalize(target));
    public static Finding? ExplicitBasic(string text)
    {
        var target = Database.Normalize(text);
        var meaning = target switch { "hello" or "hi" or "hey" => "привет", "goodbye" or "bye" => "до свидания", "thank you" or "thanks" => "спасибо", "please" => "пожалуйста (просьба)", "sorry" => "извините", "yes" => "да", "no" => "нет", "good morning" => "доброе утро", "good night" => "спокойной ночи", "good evening" => "добрый вечер", "how are you" => "как дела?", "nice to meet you" => "приятно познакомиться", "you're welcome" or "you are welcome" => "пожалуйста (ответ на благодарность)", _ => null };
        return meaning is null ? null : new Finding(target, meaning, "phrase", "A1", .5, .8, "Conversation");
    }
    public static bool ShouldSkipUtterance(string text, string source, UserSettings settings) => settings.FilterBasicWords && IsAutomatic(source) && IsBasicUtterance(text);
    public static bool IsBasicUtterance(string text) { var clauses = Regex.Split(text, @"[,.;!?\r\n]+").Where(s => !string.IsNullOrWhiteSpace(s)).ToArray(); return clauses.Length > 0 && clauses.All(IsBasic); }
    public static bool IsEnglish(string target) => Regex.IsMatch(target, @"[A-Za-z]") && !Regex.IsMatch(target, @"[^A-Za-z0-9\s\p{P}\p{S}]");
    public static bool Accept(Finding item, UserSettings settings) => IsEnglish(item.Target) && Level(item.Cefr) >= Level(settings.MinimumCaptureLevel) && (!settings.FilterBasicWords || !IsBasic(item.Target));
    // Explicitly chosen learning cards are retained even after the capture level changes.
    public static bool Accept(Vocabulary item, UserSettings settings) => item.Status is ItemStatus.Learning or ItemStatus.Mastered || IsEnglish(item.Target) && Level(item.Cefr) >= Level(settings.MinimumCaptureLevel) && (!settings.FilterBasicWords || !IsBasic(item.Target));
}

public sealed class MixSession(Database database, UserSettings settings)
{
    private readonly Dictionary<long, int> lastShown = [];
    private readonly Dictionary<long, DateTimeOffset> shownAt = [];
    private readonly List<(long Id, int After)> retries = [];
    private readonly Queue<long> recentlyShown = [];
    private int draws, newCredit;
    public int Completed { get; private set; }
    public int NewCompleted { get; private set; }
    public Vocabulary? Current { get; private set; }
    public bool CurrentIsNew { get; private set; }
    public bool CurrentIsPractice { get; private set; }
    public Vocabulary? Next()
    {
        var retry = retries.FirstOrDefault(r => r.After <= draws);
        Vocabulary? chosen = null;
        if (retry != default)
        {
            retries.Remove(retry);
            chosen = database.List(exclude: [], mixKind: "repeat", limit: int.MaxValue).FirstOrDefault(v => v.Id == retry.Id);
        }
        // A credit accumulator permits an adjustable new:repeat mix without random droughts.
        var percent = Math.Clamp(settings.MixNewPercent, 0, 100);
        bool preferNew = draws == 0 ? percent > 0 : newCredit < percent;
        if (chosen is null)
        {
            chosen = Candidate(preferNew ? "new" : "repeat");
            if (chosen is null && !preferNew && percent > 0) chosen = PracticeCandidate();
            chosen ??= Candidate(preferNew ? "repeat" : "new");
            if (chosen is null && percent > 0) chosen = PracticeCandidate();
        }
        Current = chosen; CurrentIsNew = chosen?.Status is ItemStatus.New or ItemStatus.Available;
        CurrentIsPractice = chosen is not null && !CurrentIsNew && chosen.Due > DateTimeOffset.UtcNow;
        if (chosen is null) return null;
        if (CurrentIsNew) newCredit += 100 - percent; else newCredit -= percent;
        newCredit = Math.Clamp(newCredit, -100, 100);
        lastShown[chosen.Id] = draws; shownAt[chosen.Id] = DateTimeOffset.UtcNow; recentlyShown.Enqueue(chosen.Id); while (recentlyShown.Count > 128) recentlyShown.Dequeue();
        draws++; return chosen;
    }
    private Vocabulary? Candidate(string kind)
    {
        // Reading another page avoids starving a large library when the first page has only rejected basic words.
        for (int offset = 0; ; offset += 128)
        {
            var candidates = database.List(mixKind: kind, dueOnly: kind == "repeat", limit: 128, offset: offset, exclude: kind == "repeat" ? lastShown.Where(x => draws - x.Value < 4 && DateTimeOffset.UtcNow - shownAt[x.Key] < TimeSpan.FromMinutes(1)).Select(x => x.Key) : []);
            var match = candidates.FirstOrDefault(v => LearningFilter.Accept(v, settings));
            if (match is not null) return match;
            if (candidates.Count < 128) return null;
        }
    }
    private Vocabulary? PracticeCandidate()
    {
        // Short practice is explicitly separate from the scheduled FSRS review: it does not postpone the due date.
        var repeat = database.List(mixKind: "repeat", limit: int.MaxValue);
        return repeat.Where(v => !lastShown.TryGetValue(v.Id, out var at) || draws - at >= 4 || DateTimeOffset.UtcNow - shownAt[v.Id] >= TimeSpan.FromMinutes(1))
            .OrderBy(v => lastShown.GetValueOrDefault(v.Id, -1)).ThenBy(v => v.Due).FirstOrDefault();
    }
    public void Rate(Rating rating)
    {
        if (Current is null) return;
        if (!CurrentIsPractice || rating == Rating.Again) database.Review(Current, rating, settings.Retention);
        // A missed item comes back after three intervening cards when available.
        if (rating == Rating.Again) { retries.RemoveAll(r => r.Id == Current.Id); retries.Add((Current.Id, draws + 3)); }
        Completed++; if (CurrentIsNew) NewCompleted++;
    }
    public void Forget(long id) { retries.RemoveAll(r => r.Id == id); }
    public void Reset() { lastShown.Clear(); shownAt.Clear(); retries.Clear(); recentlyShown.Clear(); draws = newCredit = Completed = NewCompleted = 0; Current = null; }
}
