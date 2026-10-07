using EnglishMemory.Core;
using System.Diagnostics;
using System.Speech.Synthesis;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using System.Text.Json;

var root = Path.Combine(Path.GetTempPath(), "EnglishMemory-checks-" + Guid.NewGuid()); Directory.CreateDirectory(root); int count = 0;
void Check(bool valid, string name) { if (!valid) throw new Exception("FAIL: " + name); count++; Console.WriteLine("PASS: " + name); }
try
{
    var db = new Database(root); var now = DateTimeOffset.UtcNow;
    var a = new Analysis("en", "I'll figure it out.", "I'll figure it out.", [new("figure out", "разобраться", "phrase", "B1", .8, .9, "Conversation")]);
    Check(db.Stats().Inbox == 0 && db.Stats().Encounters == 0, "seed library does not invent encounters or Inbox items"); db.DeleteAll(); db.SaveAnalysis(a, new(a.Quote, "pc_audio", now)); db.SaveAnalysis(a, new(a.Quote, "pc_audio", now)); db.SaveAnalysis(a, new("We can figure it out.", "microphone", now.AddSeconds(1)));
    var card = db.List().Single(); Check(card.Encounters == 2, "idempotent encounters; real sources preserved"); Check(db.List("figure").Count == 1 && db.List("разобраться").Count == 1, "target and Cyrillic search"); Check(Database.Normalize("FIGURE—OUT!") == "figure out", "Unicode normalization");
    db.SetStatus(card.Id, ItemStatus.Learning); Check(db.List(dueOnly: true).Count == 1, "accept finding into review queue"); db.SetStatus(card.Id, ItemStatus.Mastered); Check(db.List(dueOnly: true).Count == 1, "Mastered cards remain eligible when due"); db.SetStatus(card.Id, ItemStatus.Learning);
    var easy = Srs.Schedule(0, 0, null, Rating.Easy, now); var good = Srs.Schedule(0, 0, null, Rating.Good, now); var hard = Srs.Schedule(0, 0, null, Rating.Hard, now); var again = Srs.Schedule(0, 0, null, Rating.Again, now);
    Check(Math.Abs(good.Stability - 2.3065) < 1e-8 && good.Due == now.AddDays(2), "FSRS-6 initial Good reference vector"); Check(easy.Due == now.AddDays(8) && hard.Due == now.AddMinutes(10) && again.Due == now.AddMinutes(1), "four ratings and learning steps");
    Check(Math.Abs(Srs.Retrievability(10, 10) - .9) < 1e-10, "FSRS forgetting curve 90% at stability");
    var recallGood = Srs.Schedule(10, 5, now.AddDays(-10), Rating.Good, now); var recallEasy = Srs.Schedule(10, 5, now.AddDays(-10), Rating.Easy, now); var recallHard = Srs.Schedule(10, 5, now.AddDays(-10), Rating.Hard, now); var forgotten = Srs.Schedule(10, 5, now.AddDays(-10), Rating.Again, now);
    Check(recallEasy.Stability > recallGood.Stability && recallGood.Stability > recallHard.Stability && forgotten.Stability < 10, "FSRS long-term recall and forgetting");
    db.Review(db.List(dueOnly: true).Single(), Rating.Good, .9); Check(db.Stats().Reviews == 1 && db.List(dueOnly: true).Count == 0, "atomic review persistence");
    var rb = new RollingBuffer(1); rb.Add(Enumerable.Range(0, 20000).Select(i => (float)i).ToArray()); var snap = rb.Snapshot(); Check(snap.Length == 16000 && snap[0] == 4000 && snap[^1] == 19999, "bounded RAM rolling buffer"); rb.Clear(); Check(rb.Snapshot().Length == 0, "privacy buffer erase");
    var vad = new SpeechSegmenter(); Check(vad.Add(new float[16000]) is null, "silence does not reach Whisper"); Check(vad.Add(Enumerable.Repeat(.1f, 5000).ToArray()) is null && vad.Add(new float[13000]) is not null, "speech segmentation and silence boundary");
    var srt = Path.Combine(root, "sample.srt"); File.WriteAllText(srt, "1\n00:00:12,500 --> 00:00:15,000\nI'll figure it out.\n\n2\n00:00:20,000 --> 00:00:22,000\nSecond phrase\n"); var imports = Imports.Read(srt).ToArray(); Check(imports.Length == 2 && imports[0].SourceSeconds == 12.5, "subtitle timestamp preservation");
    var csv = Path.Combine(root, "sample.csv"); File.WriteAllText(csv, "text\n\"Hello, world\"\n\"Line one\nLine two\"\n"); Check(Imports.Read(csv).Count() == 2, "quoted CSV and multiline text");
    var valid = JsonSerializer.Serialize(a, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }); Check(LocalAi.Parse(valid, "actual quote").Quote == "actual quote", "never trust model quotation over original"); bool rejects = false; try { LocalAi.Parse("{\"items\":[]}", "q"); } catch { rejects = true; }
    Check(rejects, "invalid AI JSON rejected");
    long tid = db.Enqueue(new("test text", "manual", now)); Check(db.NextQueued()?.Id == tid, "durable processing queue"); db.Finish(tid, "bad output"); Check(db.QueueCount == 0 && db.RecentTranscripts()[0].State == "needs_review", "failed analysis preserves text");
    var export = Path.Combine(root, "export.json"); db.Export(export); Check(JsonDocument.Parse(File.ReadAllText(export)).RootElement.GetProperty("reviews").GetArrayLength() == 1, "complete JSON learning-data export"); var backup = Path.Combine(root, "backup.sqlite"); db.Backup(backup); Check(new FileInfo(backup).Length > 0, "consistent SQLite backup");
    if (args.Contains("--audio-probe"))
    {
        using var capture = new AudioCapture { AutoCapture = false }; var seen = new HashSet<string>(); capture.Chunk += c => { seen.Add(c.Source); Check(c.Samples.Length > 0, "capture source delivered RAM audio: " + c.Source); Array.Clear(c.Samples); };
        foreach (var mode in new[] { CaptureSource.Microphone, CaptureSource.PcAudio, CaptureSource.Both }) { capture.Start(mode, null); await Task.Delay(600); capture.CaptureRecent(); capture.Stop(); Console.WriteLine("AUDIO INITIALIZED: " + mode); }
        Check(seen.Contains("microphone"), "microphone device capture");
        // Loopback delivers no frames while the endpoint is silent. Initialization verifies COM access, not a full speech fixture.
        Console.WriteLine("PC AUDIO: endpoint initialized; silence produces no frames by WASAPI design.");
    }
    if (args.Contains("--scale"))
    {
        var sw = Stopwatch.StartNew(); for (int i = 0; i < 20000; i++) db.SaveAnalysis(a with { Items = [a.Items[0] with { Target = $"test phrase {i}" }] }, new($"Long Russian context {i} · Я сегодня вообще не выспался.", "test", now)); sw.Stop(); Console.WriteLine($"BENCH: 20000 inserts {sw.Elapsed.TotalSeconds:F2}s"); sw.Restart(); var items = db.List("test phrase 19999"); sw.Stop(); Check(items.Count == 1, "20000 phrase search"); Console.WriteLine($"BENCH: search {sw.Elapsed.TotalMilliseconds:F1}ms"); Check(db.List(limit: 500, offset: 19500).Count == 500, "large library paging");
    }
    {
        var parsed = MediaNames.Parse("Friends.S01E02.The.One.With.The.Sonogram.mkv");
        Check(parsed?.Title == "Friends" && parsed.Season == 1 && parsed.Episode == 2, "movie filename season/episode parsing");
        Check(MediaNames.Parse("Futurama 2x03 — Google Chrome", "browser-window") is { Title: "Futurama", Season: 2, Episode: 3, Evidence: "browser-window" }, "active tab caption parsing keeps provenance");
        Check(MediaNames.Parse("VLC media player") is null && MediaNames.Parse("My Movie.mp4") is { Title: "My Movie", Season: null, Episode: null }, "no fabricated episode or empty player title");
        Check(MediaNames.Parse("My.Movie.mp4 - VLC media player")?.Title == "My Movie", "player-caption suffix and video extension stripped together");
        Check(MediaNames.Parse(@"C:\Movies\My.Movie.mp4 - VLC media player")?.Title == "My Movie" && MediaNames.Parse("Netflix - Google Chrome", "browser-window") is null, "absolute video path parsed; generic service caption not mistaken for a film");
        var subtitle = Path.Combine(root, "Friends.S01E02.en.srt");
        File.WriteAllText(subtitle, "1\n00:01:05,200 --> 00:01:08,000\nI will figure it out before tomorrow.\n\n2\n00:01:10,000 --> 00:01:13,000\nWe are working on a different plan.\n\n3\n00:02:00,000 --> 00:02:03,000\nI really do not know what you mean.\n\n4\n00:09:00,000 --> 00:09:03,000\nI really do not know what you mean.\n");
        var vtt = Path.Combine(root, "Example.Show.S01E01.vtt"); File.WriteAllText(vtt, "WEBVTT\n\n00:10.500 --> 00:13.000\nI will figure it out before tomorrow.\n");
        Check(new SubtitleIndex(vtt).Match("I will figure it out before tomorrow.")?.Seconds == 10.5, "WebVTT minute/second timestamps parsed without culture ambiguity");
        var index = new SubtitleIndex(subtitle);
        Check(index.Match("I will figure it out before tomorrow!")?.Seconds == 65.2, "unique subtitle line gives exact cue timestamp");
        Check(index.Match("I'll figure it out before tomorrow.")?.Seconds == 65.2 && index.Match("I will not figure it out before tomorrow.") is null, "subtitle alignment handles contractions and rejects changed negation");
        Check(index.Match("I really do not know what you mean.") is null && index.Match("I know") is null, "ambiguous and short subtitle lines have no invented time");
        var settings = new UserSettings { MediaEnabled = true, MediaTitle = "Friends", MediaKind = "series", MediaSeason = 1, MediaEpisode = 2, MediaSubtitlePath = subtitle };
        var tracker = new MediaTracker(settings);
        Check(tracker.Snapshot("microphone") is null, "film association does not leak onto microphone speech");
        var enriched = tracker.Enrich(new("I will figure it out before tomorrow.", "pc_audio", now, Media: tracker.Snapshot("pc_audio")));
        Check(enriched.Media is { Evidence: "manual", Season: 1, Episode: 2, SubtitleFile: not null } && enriched.SourceSeconds == 65.2, "manual source and subtitle timing retained together");
        settings.MediaEpisode = 3;
        Check(tracker.Enrich(enriched with { Media = tracker.Snapshot("pc_audio"), SourceSeconds = null }).SourceSeconds is null, "subtitles from another episode do not supply a timestamp");
        settings.MediaEnabled = false; Check(tracker.Snapshot("pc_audio") is null, "source tracking can be disabled");
        settings.MediaEnabled = true; settings.MediaTitleEvidence = "video-file"; settings.MediaTitleReference = "Friends.S01E03.mkv";
        Check(tracker.Snapshot("pc_audio") is { Evidence: "video-file", Reference: "Friends.S01E03.mkv" }, "chosen video filename provenance is preserved");
        var mediaRoot = Path.Combine(root, "media-db"); var mediaDb = new Database(mediaRoot); mediaDb.DeleteAll();
        mediaDb.Enqueue(enriched); var queued = mediaDb.NextQueued()!.Value.Transcript;
        Check(queued.Media?.Label == enriched.Media?.Label && queued.SourceSeconds == 65.2, "durable analysis queue preserves media context");
        mediaDb.SaveAnalysis(a, queued); var mediaItem = new Database(mediaRoot).List("friends").Single();
        Check(mediaItem.Media?.Title == "Friends" && mediaItem.SourceSeconds == 65.2 && mediaDb.Encounters(mediaItem.Id).Single().Media?.Episode == 2, "media context survives restart and is searchable");
        mediaDb.SaveAnalysis(a, enriched with { Time = now.AddHours(1), Media = new MediaContext("Another Movie", "movie") });
        Check(mediaDb.List("friends").Single().Media?.Title == "Friends" && mediaDb.List("another movie").Single().Media?.Title == "Another Movie", "film search shows the matching encounter even after a different film");
        var legacyRoot = Path.Combine(root, "legacy-db"); var legacy = new Database(legacyRoot); legacy.DeleteAll(); legacy.SaveAnalysis(a, new(a.Quote, "pc_audio", now));
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection("Data Source=" + legacy.FilePath + ";Pooling=False")) { connection.Open(); using var sql = connection.CreateCommand(); sql.CommandText = "ALTER TABLE encounters DROP COLUMN media_json; ALTER TABLE encounters DROP COLUMN media_search; ALTER TABLE transcripts DROP COLUMN media_json; PRAGMA user_version=1;"; sql.ExecuteNonQuery(); }
        var migrated = new Database(legacyRoot); Check(migrated.List().Single().Encounters == 1 && migrated.Encounters(migrated.List().Single().Id).Single().Media is null, "schema-v1 upgrade keeps original encounters");
        migrated.SaveAnalysis(a, enriched with { Time = now.AddMinutes(1) }); Check(migrated.List("friends").Single().Encounters == 2, "upgraded database accepts and searches new media encounters");
    }
    {
        var mixDb = new Database(Path.Combine(root, "mix-db")); mixDb.DeleteAll(); var preferences = new UserSettings();
        Check(preferences.MinimumCaptureLevel == "B1" && preferences.MixNewPercent == 75, "new installations default to B1+ and 75% new mix");
        Analysis Findings(params Finding[] items) => new("en", "A real example.", "A real example.", items);
        var basic = new Finding("hello", "привет", "word", "B1", .9, .9, "Conversation");
        Check(LearningFilter.ShouldSkipUtterance("Hello.","pc_audio",preferences) && !LearningFilter.ShouldSkipUtterance("Hello.","manual",preferences) && !LearningFilter.ShouldSkipUtterance("Hello.","pc_audio",new UserSettings {FilterBasicWords=false}), "basic utterance shortcut respects capture source and the user toggle");
        var advanced = new Finding("make a decision", "принять решение", "phrase", "B1", .9, .9, "Work");
        var low = advanced with { Target = "wearing", Cefr = "A2" };
        var bad = advanced with { Target = "�������" };
        Check(!LearningFilter.Accept(basic, preferences) && !LearningFilter.Accept(low, preferences) && !LearningFilter.Accept(bad, preferences) && LearningFilter.Accept(advanced, preferences), "capture gate rejects mislabeled basics, below-level words and damaged non-English text");
        Check(mixDb.SaveAnalysis(Findings(basic, low, bad, advanced), new("quote", "pc_audio", now), preferences) == 1 && mixDb.List().Count == 1, "actual save path filters automatic PC audio findings");
        mixDb.SaveAnalysis(Findings(low), new("manual quote", "manual", now), preferences);
        Check(mixDb.List("wearing").Count == 1, "explicit manual input remains saved below capture level");
        var lowCard = mixDb.List("wearing").Single(); mixDb.SetStatus(lowCard.Id, ItemStatus.Learning);
        Check(mixDb.SaveAnalysis(Findings(low), new("another real quote", "microphone", now.AddSeconds(1)), preferences) == 1, "capture filtering preserves encounters for explicitly learned lower-level cards");
        Check(LocalAi.Parse(JsonSerializer.Serialize(Findings()), "hello").Items.Length == 0, "valid empty extraction does not force a junk finding");
        mixDb.DeleteAll();
        for (int i = 0; i < 24; i++) mixDb.SaveAnalysis(Findings(advanced with { Target = $"decision strategy {i}" }), new($"Context {i}", "pc_audio", now.AddSeconds(i)));
        for (int i = 0; i < 4; i++) { mixDb.SaveAnalysis(Findings(advanced with { Target = $"solve a problem {i}" }), new($"Old context {i}", "pc_audio", now.AddMinutes(-i-1))); mixDb.SetStatus(mixDb.List($"solve a problem {i}").Single().Id, ItemStatus.Learning); }
        var session = new MixSession(mixDb, preferences); int newCount = 0, repeats = 0; var firstCycle = new List<bool>();
        for (int i = 0; i < 12; i++) { var next = session.Next(); Check(next is not null, $"mix card {i+1} available without Inbox acceptance"); firstCycle.Add(session.CurrentIsNew); if (session.CurrentIsNew) newCount++; else repeats++; session.Rate(Rating.Good); }
        Check(newCount == 9 && repeats == 3 && firstCycle.Take(4).SequenceEqual(new[] { true,true,true,false }), "continuous mix uses 3 new then 1 repeat with both queues available");
        var retrySession = new MixSession(mixDb, preferences); var missed = retrySession.Next()!; retrySession.Rate(Rating.Again);
        for (int i = 0; i < 3; i++) { var other = retrySession.Next()!; Check(other.Id != missed.Id, $"missed card has intervening card {i+1}"); retrySession.Rate(Rating.Good); }
        Check(retrySession.Next()!.Id == missed.Id, "Again returns after three intervening cards");
        var oldStatuses = mixDb.SetStatuses(mixDb.List(mixKind:"new").Take(3).Select(v=>v.Id), ItemStatus.Known);
        var hidden = mixDb.SetStatuses(mixDb.List(mixKind:"new").Take(3).Select(v=>v.Id), ItemStatus.Suspended);
        var excluded = oldStatuses.Keys.Concat(hidden.Keys).ToHashSet(); var cleanSession = new MixSession(mixDb, preferences);
        var drawn = new List<long>(); for (int i=0;i<16;i++) { var v=cleanSession.Next(); if(v is null) break; drawn.Add(v.Id); cleanSession.Rate(Rating.Good); }
        Check(!drawn.Any(excluded.Contains), "known and hidden cards do not reappear in replenished mix");
        mixDb.RestoreStatuses(oldStatuses); mixDb.RestoreStatuses(hidden);
        Check(mixDb.List(mixKind:"new").Count >= 6 && mixDb.Stats().Encounters == 28, "batch undo restores states without deleting source history");
        mixDb.SaveAnalysis(Findings(basic, low, bad), new("old junk", "manual", now));
        var learningCount = mixDb.List(mixKind:"repeat",limit:1000).Count; var clean = mixDb.HideBasicNew(preferences); var kept = mixDb.List(mixKind:"repeat",limit:1000).Count;
        Check(clean.Count == 3 && kept == learningCount, "one-action cleanup only hides basic new cards and preserves learning"); mixDb.RestoreStatuses(clean);
        Check(mixDb.List(status:ItemStatus.Suspended).Count == 0 && mixDb.List(libraryState:"known").Count==0, "bulk cleanup can be undone and library status views work");
        var practiceDb = new Database(Path.Combine(root,"practice-db")); practiceDb.DeleteAll(); practiceDb.SaveAnalysis(Findings(advanced),new("Practice context", "pc_audio",now)); practiceDb.Review(practiceDb.List().Single(),Rating.Good,.9);
        var beforePractice = practiceDb.List().Single(); var practice = new MixSession(practiceDb,preferences); practice.Next(); practice.Rate(Rating.Good); var afterPractice=practiceDb.List().Single();
        Check(practice.CurrentIsPractice && afterPractice.Due==beforePractice.Due && afterPractice.Reviews==beforePractice.Reviews, "extra practice does not postpone FSRS due date or inflate scheduled reviews");
        var pagingDb = new Database(Path.Combine(root,"paging-mix")); pagingDb.DeleteAll();
        for(int i=0;i<140;i++) pagingDb.SaveAnalysis(Findings(low with {Target=$"basic item {i}",Importance=1,PersonalRelevance=1}),new("Basic", "manual",now));
        pagingDb.SaveAnalysis(Findings(advanced with {Importance=.1,PersonalRelevance=.1}),new("Good", "manual",now));
        Check(new MixSession(pagingDb,preferences).Next()?.Target==advanced.Target,"filtered first page does not starve an eligible card in a larger library");
        var settingsPath=Path.Combine(root,"mix-settings.json"); preferences.Save(settingsPath);
        Check(UserSettings.Load(settingsPath).MixNewPercent==75 && UserSettings.Load(settingsPath).ShowCardPictures,"mix preferences survive restart");
    }
    if (args.Contains("--mix-e2e"))
    {
        var installedModels = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"EnglishMemory","Models");
        var pipelineRoot = Path.Combine(root,"mix-pipeline"); Directory.CreateDirectory(pipelineRoot); Environment.SetEnvironmentVariable("ENGLISH_MEMORY_DATA",pipelineRoot);
        var settings = new UserSettings { SpeechModel=Path.Combine(installedModels,"ggml-base.bin"),LanguageModel=Path.Combine(installedModels,"qwen2.5-1.5b-instruct-q4_k_m.gguf"),MinimumCaptureLevel="B1" }; settings.Save(Path.Combine(pipelineRoot,"settings.json"));
        await using var pipeline = new Engine(); pipeline.Db.DeleteAll();
        pipeline.Db.Enqueue(new("I'll figure it out before tomorrow.","pc_audio",DateTimeOffset.UtcNow.AddMinutes(-2)));
        pipeline.Db.Enqueue(new("Hello, thank you, goodbye.","pc_audio",DateTimeOffset.UtcNow.AddMinutes(-2)));
        pipeline.Db.Enqueue(new("Hello.","manual",DateTimeOffset.UtcNow.AddMinutes(-2)));
        var timeout=Stopwatch.StartNew(); while(pipeline.Db.QueueCount>0 && timeout.Elapsed<TimeSpan.FromMinutes(3)) await Task.Delay(250);
        var states=pipeline.Db.RecentTranscripts(); Check(states.Count==3 && states.All(t=>t.State=="done"),"real Qwen grammar and engine queue process useful and filler speech without analysis failures");
        foreach(var t in states) Console.WriteLine("RAW ANALYSIS: "+JsonSerializer.Serialize(pipeline.Db.Cached(LocalAi.CacheKey(t.Text,settings.LanguageModel))));
        var findings=pipeline.Db.List(); Console.WriteLine("MIX PIPELINE: "+JsonSerializer.Serialize(findings.Select(v=>new{v.Target,v.Cefr})));
        Check(findings.Any(v=>v.Target.Contains("figure",StringComparison.OrdinalIgnoreCase)) && findings.Where(v=>pipeline.Db.Encounters(v.Id).Any(e=>e.Source=="pc_audio")).All(v=>LearningFilter.Accept(v,settings)),"real engine saves useful B1 findings and applies configured gate after LLM extraction");
        Check(!findings.Where(v=>pipeline.Db.Encounters(v.Id).Any(e=>e.Source=="pc_audio")).Any(v=>LearningFilter.IsBasic(v.Target)) && states.Any(t=>t.Text.StartsWith("Hello")),"filler does not enter vocabulary while original transcript remains available");
        Check(findings.Any(v=>pipeline.Db.Encounters(v.Id).Any(e=>e.Source=="manual")),"explicit manual basic phrase survives queue processing");
    }
    if (args.Contains("--models"))
    {
        DataPaths.Ensure(); foreach (var m in ModelCatalog.All.Where(x => x.Id is "speech-fast" or "language-fast")) { if (!m.Installed) { Console.WriteLine("DOWNLOAD: " + m.Name); int last = -1; await ModelCatalog.Download(m, new Progress<double>(p => { int percent = (int)(p * 10) * 10; if (percent != last) { last = percent; Console.WriteLine($"{m.Id}: {percent}%"); } }), CancellationToken.None); } }
        var settings = new UserSettings { SpeechModel = ModelCatalog.All[0].LocalPath, LanguageModel = ModelCatalog.All[3].LocalPath }; using var ai = new LocalAi(settings);
        foreach (string input in new[] { "I'll figure it out.", "Я сегодня вообще не выспался." }) { var sw = Stopwatch.StartNew(); var output = await ai.Analyze(input, CancellationToken.None); Console.WriteLine("AI RESULT: " + JsonSerializer.Serialize(output, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping })); Console.WriteLine($"BENCH: analysis {sw.Elapsed.TotalSeconds:F2}s"); Check(output.Items.Length > 0 && output.Quote == input && output.Items.All(f => f.Translation.Any(c => c >= 'А' && c <= 'я')) && !output.NaturalEnglish.Any(c => c >= 'А' && c <= 'я'), "real local LLM: " + input); if (input.StartsWith("Я")) Check(output.NaturalEnglish.Contains("sleep", StringComparison.OrdinalIgnoreCase) || output.NaturalEnglish.Contains("rest", StringComparison.OrdinalIgnoreCase), "Russian translation meaning: sleep / rest"); else Check(output.Items.Any(f => f.Target.Contains("figure", StringComparison.OrdinalIgnoreCase)), "English phrasal verb extraction"); }
        using var synth = new SpeechSynthesizer(); foreach (var language in new[] { "en", "ru" })
        {
            var voice = synth.GetInstalledVoices().FirstOrDefault(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == language); if (voice is null) { Console.WriteLine("UNVERIFIED: no installed " + language + " TTS voice for speech fixture"); continue; }
            var wav = Path.Combine(root, language + ".wav"); synth.SelectVoice(voice.VoiceInfo.Name); synth.SetOutputToWaveFile(wav); synth.Speak(language == "en" ? "I will figure it out. I am working on it." : "Я сегодня вообще не выспался."); synth.SetOutputToNull();
            using var reader = new AudioFileReader(wav); NAudio.Wave.ISampleProvider provider = reader; if (reader.WaveFormat.Channels == 2) provider = new StereoToMonoSampleProvider(reader); var resample = new WdlResamplingSampleProvider(provider, 16000); var all = new List<float>(); var buffer = new float[4096]; int n; while ((n = resample.Read(buffer, 0, buffer.Length)) > 0) all.AddRange(buffer.Take(n)); var sw = Stopwatch.StartNew(); string result = await ai.Transcribe(all.ToArray(), CancellationToken.None); Console.WriteLine($"WHISPER {language}: {result} · {sw.Elapsed.TotalSeconds:F2}s"); Check(Database.Normalize(result) == Database.Normalize(language == "en" ? "I will figure it out. I am working on it." : "Я сегодня вообще не выспался."), "exact local multilingual transcription fixture: " + language);
        }
    }
    if (args.Contains("--media-e2e"))
    {
        string models = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnglishMemory", "Models"), appRoot = Path.Combine(root, "media-engine"); Directory.CreateDirectory(appRoot); Environment.SetEnvironmentVariable("ENGLISH_MEMORY_DATA", appRoot);
        var subtitle = Path.Combine(appRoot, "Example.Show.S02E04.srt"); File.WriteAllText(subtitle, "1\n00:01:05,200 --> 00:01:08,000\nI will figure it out before tomorrow.\n");
        new UserSettings { SpeechModel = Path.Combine(models, "ggml-base.bin"), LanguageModel = Path.Combine(models, "qwen2.5-1.5b-instruct-q4_k_m.gguf"), MediaEnabled = true, MediaTitle = "Example Show", MediaKind = "animated-series", MediaSeason = 2, MediaEpisode = 4, MediaSubtitlePath = subtitle }.Save(Path.Combine(appRoot, "settings.json"));
        await using var engine = new Engine(); engine.AddText("I will figure it out before tomorrow.", "media_manual"); var deadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (engine.Db.List("example show").Count == 0 && engine.Db.RecentTranscripts().All(t => t.State != "needs_review") && DateTimeOffset.UtcNow < deadline) await Task.Delay(500);
        var findings = engine.Db.List("example show"); Check(findings.Count > 0 && findings.Any(f => f.Target.Contains("figure", StringComparison.OrdinalIgnoreCase)), "media quote → real local LLM → generated phrase cards");
        Check(findings.All(f => f.Media is { Title: "Example Show", Kind: "animated-series", Season: 2, Episode: 4, Evidence: "manual" } && f.SourceSeconds == 65.2 && f.Quote == "I will figure it out before tomorrow."), "actual LLM worker preserves source, exact original quote, episode and subtitle time");
        var resumed = new Database(appRoot).List("example show"); Check(resumed.Count == findings.Count && resumed.All(f => f.Media?.Episode == 4), "real media pipeline attribution survives reopening database");
    }
    if (args.Contains("--e2e"))
    {
            if (args.Contains("--models")) throw new ArgumentException("Run --e2e separately to isolate application data.");
        string actualModels = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnglishMemory", "Models"), appRoot = Path.Combine(root, "engine"); Directory.CreateDirectory(appRoot); Environment.SetEnvironmentVariable("ENGLISH_MEMORY_DATA", appRoot);
        new UserSettings { SpeechModel = Path.Combine(actualModels, "ggml-base.bin"), LanguageModel = Path.Combine(actualModels, "qwen2.5-1.5b-instruct-q4_k_m.gguf"), Source = CaptureSource.Both, AutoCapture = false }.Save(Path.Combine(appRoot, "settings.json"));
        await using var engine = new Engine(); var errors = new List<string>(); engine.Message += m => { lock (errors) { errors.Add(m); } Console.WriteLine("ENGINE: " + m); };
        engine.AddText("Я опоздал, потому что пропустил автобус."); var deadline = DateTimeOffset.UtcNow.AddSeconds(120); while (engine.Db.Stats().Inbox == 0 && engine.Db.RecentTranscripts().All(t => t.State != "needs_review") && DateTimeOffset.UtcNow < deadline) await Task.Delay(500);
        var found = engine.Db.List(status: ItemStatus.New); Check(found.Count > 0, "end-to-end Russian text → local LLM → Inbox → SQLite"); Console.WriteLine("BUS FIXTURE TRANSLATION: " + string.Join(" | ", found.Select(f => f.NaturalEnglish))); Check(found.Any(f => f.NaturalEnglish.Contains("bus", StringComparison.OrdinalIgnoreCase)), "novel Russian sentence translation retains meaning");
        engine.Db.SetStatus(found[0].Id, ItemStatus.Learning); var due = engine.Db.List(dueOnly: true).First(); engine.Db.Review(due, Rating.Good, .9); Check(engine.Db.Stats().Reviews == 1, "end-to-end Inbox acceptance → FSRS review");
        var mediaSubtitle = Path.Combine(appRoot, "Example.Show.S01E02.srt"); File.WriteAllText(mediaSubtitle, "1\n00:01:05,200 --> 00:01:08,000\nI will figure it out before tomorrow.\n");
        engine.Settings.MediaEnabled = true; engine.Settings.MediaTitle = "Example Show"; engine.Settings.MediaKind = "series"; engine.Settings.MediaSeason = 1; engine.Settings.MediaEpisode = 2; engine.Settings.MediaSubtitlePath = mediaSubtitle; engine.SaveSettings();
        engine.AddText("I will figure it out before tomorrow.", "media_manual"); var mediaDeadline = DateTimeOffset.UtcNow.AddSeconds(120);
        while (engine.Db.List("example show").Count == 0 && engine.Db.RecentTranscripts().All(t => t.State != "needs_review") && DateTimeOffset.UtcNow < mediaDeadline) await Task.Delay(500);
        var mediaFindings = engine.Db.List("example show"); Check(mediaFindings.Count > 0, "real local LLM pipeline retains film context on generated cards");
        Check(mediaFindings.All(item => item.Media is { Season: 1, Episode: 2, Evidence: "manual" } && item.SourceSeconds == 65.2), "real media analysis preserves exact subtitle cue time and episode");
        engine.Start(); await Task.Delay(600); engine.TogglePrivate(); Check(engine.Private && !engine.Listening, "Private Mode stops Both capture"); engine.Audio.CaptureRecent(); Check(engine.Db.QueueCount == 0, "Private Mode cannot enqueue recent audio"); engine.TogglePrivate(); Check(engine.Listening, "Private Mode restores explicit prior Listening state"); engine.Stop(); Check(engine.Db.Stats().ListeningSeconds > 0, "listening session duration persisted");
    }
    if (args.Contains("--soak"))
    {
        if (args.Contains("--models") || args.Contains("--e2e")) throw new ArgumentException("Run --soak alone to isolate data.");
        string modelRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "EnglishMemory", "Models"), soakRoot = Path.Combine(root, "soak"); Directory.CreateDirectory(soakRoot); Environment.SetEnvironmentVariable("ENGLISH_MEMORY_DATA", soakRoot);
        new UserSettings { SpeechModel = Path.Combine(modelRoot, "ggml-base.bin"), LanguageModel = Path.Combine(modelRoot, "qwen2.5-1.5b-instruct-q4_k_m.gguf"), Source = CaptureSource.Both }.Save(Path.Combine(soakRoot, "settings.json"));
        using var stop = new CancellationTokenSource(); Console.CancelKeyPress += (_, e) => { e.Cancel = true; stop.Cancel(); }; await using var engine = new Engine(); var hoursArg = args.FirstOrDefault(x => x.StartsWith("--hours=")); double hours = hoursArg is null ? 8 : double.Parse(hoursArg[8..], System.Globalization.CultureInfo.InvariantCulture); if (hours <= 0 || hours > 24) throw new ArgumentException("Hours must be >0 and <=24.");
        Console.WriteLine($"SOAK: Both audio capture for {hours} hours. Raw audio is not saved. Temporary transcripts are deleted after the test. Ctrl+C stops capture.");
        await engine.Ai.Analyze("How was your day?", stop.Token); await engine.Ai.Transcribe(new float[16000], stop.Token); engine.Start(); var clock = Stopwatch.StartNew(); var logPath = Path.GetFullPath("EnglishMemory-soak.csv"); await File.WriteAllTextAsync(logPath, "seconds,private_bytes,working_set,handles,queued,dropped\n");
        try { while (clock.Elapsed.TotalHours < hours && !stop.IsCancellationRequested) { using var p = Process.GetCurrentProcess(); p.Refresh(); var line = $"{clock.Elapsed.TotalSeconds:F0},{p.PrivateMemorySize64},{p.WorkingSet64},{p.HandleCount},{engine.Db.QueueCount},{engine.DroppedChunks}"; Console.WriteLine(line); await File.AppendAllTextAsync(logPath, line + Environment.NewLine); await Task.Delay(TimeSpan.FromMinutes(1), stop.Token); } } catch (OperationCanceledException) { } finally { engine.Stop(); Console.WriteLine("SOAK STOPPED: " + clock.Elapsed + ". Metrics: " + logPath); }
    }
    var uiData = args.FirstOrDefault(x => x.StartsWith("--ui-data="));
    if (uiData is not null)
    {
        var fixture = new Database(uiData[10..]); using var sql = new Microsoft.Data.Sqlite.SqliteConnection(new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder { DataSource = fixture.FilePath, Pooling = false }.ToString()); sql.Open(); using var tx = sql.BeginTransaction();
        using var c = sql.CreateCommand(); c.Transaction = tx; c.CommandText = "INSERT OR IGNORE INTO vocabulary(normalized,target,translation,type,cefr,topic,status,importance,relevance,first_seen,last_seen,due) VALUES($n,$t,$tr,'phrase','B1','Work',0,.3,.3,$now,$now,$now)"; c.Parameters.Add("$n", Microsoft.Data.Sqlite.SqliteType.Text); c.Parameters.Add("$t", Microsoft.Data.Sqlite.SqliteType.Text); c.Parameters.Add("$tr", Microsoft.Data.Sqlite.SqliteType.Text); c.Parameters.AddWithValue("$now", now.ToString("O"));
        for (int i = 0; i < 20000; i++) { c.Parameters["$n"].Value = $"stress phrase {i}"; c.Parameters["$t"].Value = $"stress phrase {i}"; c.Parameters["$tr"].Value = "Очень длинный русский перевод для проверки переноса строк и устойчивости интерфейса. " + new string('я', 180); c.ExecuteNonQuery(); }
        tx.Commit(); Console.WriteLine("UI FIXTURES: 20000 phrases created.");
    }
    db.DeleteAll(); Check(db.Stats().Total == 0 && db.Stats().Reviews == 0, "delete-all cascades"); Console.WriteLine($"ALL {count} CHECKS PASSED");
}
finally { Directory.Delete(root, true); }
