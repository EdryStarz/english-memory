using Microsoft.Data.Sqlite;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
namespace EnglishMemory.Core;

public sealed class Database
{
    private readonly string connectionString;
    public string FilePath { get; }
    public Database(string root)
    {
        Directory.CreateDirectory(root); FilePath = Path.Combine(root, "memory.sqlite");
        connectionString = new SqliteConnectionStringBuilder { DataSource = FilePath, Pooling = false }.ToString();
        using var db = Open(); using var cmd = db.CreateCommand();
        cmd.CommandText = """
        PRAGMA journal_mode=WAL;
        CREATE TABLE IF NOT EXISTS vocabulary(id INTEGER PRIMARY KEY, normalized TEXT NOT NULL UNIQUE, target TEXT NOT NULL, translation TEXT NOT NULL, type TEXT NOT NULL, cefr TEXT NOT NULL, topic TEXT NOT NULL, status INTEGER NOT NULL DEFAULT 0, importance REAL NOT NULL, relevance REAL NOT NULL, first_seen TEXT NOT NULL, last_seen TEXT NOT NULL, stability REAL NOT NULL DEFAULT 0, difficulty REAL NOT NULL DEFAULT 0, due TEXT NOT NULL, last_review TEXT, reviews INTEGER NOT NULL DEFAULT 0);
        CREATE TABLE IF NOT EXISTS encounters(id INTEGER PRIMARY KEY, item_id INTEGER NOT NULL REFERENCES vocabulary(id) ON DELETE CASCADE, quote TEXT NOT NULL, natural_english TEXT NOT NULL, source TEXT NOT NULL, application TEXT, timestamp TEXT NOT NULL, source_seconds REAL, UNIQUE(item_id,quote,source,timestamp));
        CREATE INDEX IF NOT EXISTS ix_encounters_item ON encounters(item_id);
        CREATE INDEX IF NOT EXISTS ix_due ON vocabulary(status,due);
        CREATE TABLE IF NOT EXISTS reviews(id INTEGER PRIMARY KEY,item_id INTEGER NOT NULL REFERENCES vocabulary(id) ON DELETE CASCADE,rating INTEGER NOT NULL,timestamp TEXT NOT NULL,stability REAL NOT NULL,difficulty REAL NOT NULL,due TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS transcripts(id INTEGER PRIMARY KEY, text TEXT NOT NULL, source TEXT NOT NULL,timestamp TEXT NOT NULL,application TEXT,source_seconds REAL, state TEXT NOT NULL DEFAULT 'queued',error TEXT);
        CREATE TABLE IF NOT EXISTS analysis_cache(hash TEXT PRIMARY KEY,json TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS sessions(id INTEGER PRIMARY KEY,started TEXT NOT NULL,seconds REAL NOT NULL,source TEXT NOT NULL);
        CREATE TABLE IF NOT EXISTS metadata(key TEXT PRIMARY KEY,value TEXT NOT NULL);
        """; cmd.ExecuteNonQuery();
        using (var migration = db.BeginTransaction())
        {
            foreach (var (table, column, type) in new[] { ("encounters", "media_json", "TEXT"), ("encounters", "media_search", "TEXT"), ("transcripts", "media_json", "TEXT") })
            {
                bool exists; using (var info = db.CreateCommand()) { info.Transaction = migration; info.CommandText = $"PRAGMA table_info({table})"; using var rows = info.ExecuteReader(); exists = false; while (rows.Read()) if (rows.GetString(1) == column) exists = true; }
                if (!exists) { using var add = db.CreateCommand(); add.Transaction = migration; add.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {type}"; add.ExecuteNonQuery(); }
            }
            using var version = db.CreateCommand(); version.Transaction = migration; version.CommandText = "PRAGMA user_version=2"; version.ExecuteNonQuery(); migration.Commit();
        }
        SeedCore(db);
    }
    private static MediaContext? ReadMedia(SqliteDataReader row, int column) { if (row.IsDBNull(column)) return null; try { return JsonSerializer.Deserialize<MediaContext>(row.GetString(column)); } catch (JsonException) { return null; } }
    private SqliteConnection Open() { var db = new SqliteConnection(connectionString); db.Open(); using var cmd = db.CreateCommand(); cmd.CommandText = "PRAGMA foreign_keys=ON; PRAGMA busy_timeout=5000;"; cmd.ExecuteNonQuery(); return db; }
    private static SqliteCommand Command(SqliteConnection db, string sql, params (string, object?)[] args) { var c = db.CreateCommand(); c.CommandText = sql; foreach (var (k, v) in args) c.Parameters.AddWithValue(k, v ?? DBNull.Value); return c; }
    public static string Normalize(string s) => Regex.Replace(s.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Replace('’', '\''), @"[^\p{L}\p{N}']+", " ").Trim();
    public int SaveAnalysis(Analysis a, Transcript t, UserSettings? settings = null)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); int count = 0;
        foreach (var f in a.Items)
        {
            // Preserve explicit learning/history; filter only unsolicited new capture findings.
            if (settings is not null && LearningFilter.IsAutomatic(t.Source) && !LearningFilter.Accept(f, settings))
            {
                using var existing = Command(db, "SELECT status FROM vocabulary WHERE normalized=$n", ("$n", Normalize(f.Target))); existing.Transaction = tx;
                var previous = existing.ExecuteScalar();
                if (previous is null || Convert.ToInt32(previous) is 0 or 5) continue;
            }
            using var c = Command(db, """
            INSERT INTO vocabulary(normalized,target,translation,type,cefr,topic,importance,relevance,first_seen,last_seen,due) VALUES($n,$t,$tr,$ty,$c,$topic,$i,$r,$time,$time,$time)
            ON CONFLICT(normalized) DO UPDATE SET last_seen=MAX(last_seen,excluded.last_seen),relevance=MAX(relevance,excluded.relevance)
            RETURNING id;
            """, ("$n", Normalize(f.Target)), ("$t", f.Target), ("$tr", f.Translation), ("$ty", f.Type), ("$c", f.Cefr), ("$topic", f.Topic), ("$i", f.Importance), ("$r", f.PersonalRelevance), ("$time", t.Time.ToUniversalTime().ToString("O")));
            c.Transaction = tx; var id = (long)c.ExecuteScalar()!;
            using var e = Command(db, "INSERT OR IGNORE INTO encounters(item_id,quote,natural_english,source,application,timestamp,source_seconds,media_json,media_search) VALUES($id,$q,$en,$s,$app,$time,$sec,$media,$mediaSearch)", ("$id", id), ("$q", t.Text), ("$en", a.NaturalEnglish), ("$s", t.Source), ("$app", t.Application), ("$time", t.Time.ToUniversalTime().ToString("O")), ("$sec", t.SourceSeconds), ("$media", t.Media is null ? null : JsonSerializer.Serialize(t.Media)), ("$mediaSearch", t.Media is null ? null : Normalize(t.Media.Label))); e.Transaction = tx; count += e.ExecuteNonQuery();
        }
        tx.Commit(); return count;
    }
    public List<Vocabulary> List(string search = "", ItemStatus? status = null, bool dueOnly = false, string? topic = null, int limit = 500, int offset = 0, bool personalOnly = false, string? minimumLevel = null, string? mixKind = null, IEnumerable<long>? exclude = null, string? libraryState = null)
    {
        var excluded = string.Join(',', (exclude ?? []).Distinct().Select(id => id.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        var exclusion = excluded.Length == 0 ? "" : $" AND v.id NOT IN ({excluded})";
        using var db = Open(); using var c = Command(db, """
        SELECT v.id,v.target,v.translation,v.type,v.cefr,v.topic,v.status,v.importance,v.relevance,
        (SELECT COUNT(*) FROM encounters WHERE item_id=v.id),v.first_seen,v.last_seen,
        COALESCE(e.quote,''),COALESCE(e.natural_english,''),COALESCE(e.source,''),v.stability,v.difficulty,v.due,v.last_review,v.reviews,e.media_json,e.source_seconds
        FROM vocabulary v LEFT JOIN encounters e ON e.id=(SELECT id FROM encounters WHERE item_id=v.id ORDER BY CASE WHEN $search<>'' AND instr(COALESCE(media_search,''),$search)>0 THEN 0 ELSE 1 END,timestamp DESC LIMIT 1)
        WHERE ($status IS NULL OR v.status=$status) AND ($topic IS NULL OR v.topic=$topic)
        AND ($personal=0 OR EXISTS(SELECT 1 FROM encounters WHERE item_id=v.id))
        AND ($due=0 OR (v.status IN (1,3) AND v.due <= $now))
        AND ($minimum=0 OR v.status IN (1,3) OR CASE v.cefr WHEN 'A1' THEN 1 WHEN 'A2' THEN 2 WHEN 'B1' THEN 3 WHEN 'B2' THEN 4 WHEN 'C1' THEN 5 WHEN 'C2' THEN 6 ELSE 0 END >= $minimum)
        AND ($library IS NULL OR ($library='active' AND v.status IN (0,1,3,5)) OR ($library='known' AND v.status=2) OR ($library='hidden' AND v.status=4))
        AND ($mix IS NULL OR ($mix='new' AND v.status IN (0,5)) OR ($mix='repeat' AND v.status IN (1,3)))
        AND ($search='' OR instr(v.normalized,$search)>0 OR instr(lower(v.translation),$search)>0 OR EXISTS(SELECT 1 FROM encounters WHERE item_id=v.id AND (instr(lower(quote),$search)>0 OR instr(COALESCE(media_search,''),$search)>0)))
        /*mix-exclude*/ ORDER BY CASE WHEN $mix='repeat' THEN v.due ELSE '' END ASC,
        CASE WHEN $mix='new' THEN EXISTS(SELECT 1 FROM encounters WHERE item_id=v.id) ELSE 0 END DESC,v.importance*.3+v.relevance*.4+MIN(.3,(SELECT COUNT(*) FROM encounters WHERE item_id=v.id)*.03) DESC,v.last_seen DESC LIMIT $limit OFFSET $offset;
        """.Replace("/*mix-exclude*/", exclusion), ("$minimum", minimumLevel is null ? 0 : LearningFilter.Level(minimumLevel)), ("$mix", mixKind), ("$library", libraryState), ("$status", status.HasValue ? (int)status.Value : null), ("$topic", topic), ("$due", dueOnly ? 1 : 0), ("$now", DateTimeOffset.UtcNow.ToString("O")), ("$search", Normalize(search)), ("$limit", limit), ("$offset", offset), ("$personal", personalOnly ? 1 : 0));
        using var r = c.ExecuteReader(); var list = new List<Vocabulary>();
        while (r.Read()) list.Add(new(r.GetInt64(0), r.GetString(1), r.GetString(2), r.GetString(3), r.GetString(4), r.GetString(5), (ItemStatus)r.GetInt32(6), r.GetDouble(7), r.GetDouble(8), r.GetInt32(9), DateTimeOffset.Parse(r.GetString(10)), DateTimeOffset.Parse(r.GetString(11)), r.GetString(12), r.GetString(13), r.GetString(14), r.GetDouble(15), r.GetDouble(16), DateTimeOffset.Parse(r.GetString(17)), r.IsDBNull(18) ? null : DateTimeOffset.Parse(r.GetString(18)), r.GetInt32(19), ReadMedia(r, 20), r.IsDBNull(21) ? null : r.GetDouble(21)));
        return list;
    }
    public List<Encounter> Encounters(long id) { using var db = Open(); using var c = Command(db, "SELECT * FROM encounters WHERE item_id=$id ORDER BY timestamp DESC LIMIT 200", ("$id", id)); using var r = c.ExecuteReader(); var l = new List<Encounter>(); while (r.Read()) l.Add(new(r.GetInt64(0), r.GetInt64(1), r.GetString(2), r.GetString(3), r.GetString(4), r.IsDBNull(5) ? null : r.GetString(5), DateTimeOffset.Parse(r.GetString(6)), r.IsDBNull(7) ? null : r.GetDouble(7), ReadMedia(r, 8))); return l; }
    public void SetStatus(long id, ItemStatus status, bool important = false) { using var db = Open(); using var c = Command(db, "UPDATE vocabulary SET status=$s,relevance=CASE WHEN $i=1 THEN 1 ELSE relevance END WHERE id=$id", ("$s", (int)status), ("$id", id), ("$i", important ? 1 : 0)); c.ExecuteNonQuery(); }
    public Dictionary<long, ItemStatus> SetStatuses(IEnumerable<long> ids, ItemStatus status)
    {
        using var db = Open(); using var tx = db.BeginTransaction(); var old = new Dictionary<long, ItemStatus>();
        foreach (var id in ids.Distinct())
        {
            using var read = Command(db, "SELECT status FROM vocabulary WHERE id=$id", ("$id", id)); read.Transaction = tx; var value = read.ExecuteScalar(); if (value is null) continue;
            old[id] = (ItemStatus)Convert.ToInt32(value);
            using var update = Command(db, "UPDATE vocabulary SET status=$s WHERE id=$id", ("$id", id), ("$s", (int)status)); update.Transaction = tx; update.ExecuteNonQuery();
        }
        tx.Commit(); return old;
    }
    public void RestoreStatuses(IReadOnlyDictionary<long, ItemStatus> states)
    {
        using var db = Open(); using var tx = db.BeginTransaction();
        foreach (var (id, status) in states) { using var c = Command(db, "UPDATE vocabulary SET status=$s WHERE id=$id", ("$s", (int)status), ("$id", id)); c.Transaction = tx; c.ExecuteNonQuery(); } tx.Commit();
    }
    public int BasicNewCount(UserSettings settings) => List(limit: int.MaxValue, mixKind: "new").Count(v => !LearningFilter.Accept(v, settings));
    public Dictionary<long, ItemStatus> HideBasicNew(UserSettings settings) => SetStatuses(List(limit: int.MaxValue, mixKind: "new").Where(v => !LearningFilter.Accept(v, settings)).Select(v => v.Id), ItemStatus.Suspended);
    public void Review(Vocabulary item, Rating rating, double retention)
    {
        var now = DateTimeOffset.UtcNow; var n = Srs.Schedule(item.Stability, item.Difficulty, item.LastReview, rating, now, retention);
        using var db = Open(); using var tx = db.BeginTransaction();
        using var c = Command(db, "UPDATE vocabulary SET stability=$s,difficulty=$d,due=$due,last_review=$now,reviews=reviews+1,status=$status WHERE id=$id", ("$s", n.Stability), ("$d", n.Difficulty), ("$due", n.Due.ToString("O")), ("$now", now.ToString("O")), ("$status", n.Stability > 100 ? 3 : 1), ("$id", item.Id)); c.Transaction = tx; c.ExecuteNonQuery();
        using var r = Command(db, "INSERT INTO reviews(item_id,rating,timestamp,stability,difficulty,due) VALUES($id,$g,$now,$s,$d,$due)", ("$id", item.Id), ("$g", (int)rating), ("$now", now.ToString("O")), ("$s", n.Stability), ("$d", n.Difficulty), ("$due", n.Due.ToString("O"))); r.Transaction = tx; r.ExecuteNonQuery(); tx.Commit();
    }
    public long Enqueue(Transcript t) { using var db = Open(); using var c = Command(db, "INSERT INTO transcripts(text,source,timestamp,application,source_seconds,media_json) VALUES($t,$s,$time,$a,$sec,$media) RETURNING id", ("$t", t.Text), ("$s", t.Source), ("$time", t.Time.ToUniversalTime().ToString("O")), ("$a", t.Application), ("$sec", t.SourceSeconds), ("$media", t.Media is null ? null : JsonSerializer.Serialize(t.Media))); return (long)c.ExecuteScalar()!; }
    public (long Id, Transcript Transcript)? NextQueued() { using var db = Open(); using var c = Command(db, "SELECT id,text,source,timestamp,application,source_seconds,media_json FROM transcripts WHERE state='queued' ORDER BY id LIMIT 1"); using var r = c.ExecuteReader(); return r.Read() ? (r.GetInt64(0), new(r.GetString(1), r.GetString(2), DateTimeOffset.Parse(r.GetString(3)), r.IsDBNull(4) ? null : r.GetString(4), r.IsDBNull(5) ? null : r.GetDouble(5), ReadMedia(r, 6))) : null; }
    public void Finish(long id, string? error = null) { using var db = Open(); using var c = Command(db, "UPDATE transcripts SET state=$s,error=$e WHERE id=$id", ("$s", error is null ? "done" : "needs_review"), ("$e", error), ("$id", id)); c.ExecuteNonQuery(); }
    public void RetryFailed() { using var db = Open(); using var c = Command(db, "UPDATE transcripts SET state='queued',error=NULL WHERE state='needs_review'"); c.ExecuteNonQuery(); }
    public int QueueCount { get { using var db = Open(); using var c = Command(db, "SELECT COUNT(*) FROM transcripts WHERE state='queued'"); return Convert.ToInt32(c.ExecuteScalar()); } }
    public List<(string Text, string State, string? Error)> RecentTranscripts() { using var db = Open(); using var c = Command(db, "SELECT text,state,error FROM transcripts ORDER BY id DESC LIMIT 100"); using var r = c.ExecuteReader(); var l = new List<(string, string, string?)>(); while (r.Read()) l.Add((r.GetString(0), r.GetString(1), r.IsDBNull(2) ? null : r.GetString(2))); return l; }
    public Analysis? Cached(string key) { using var db = Open(); using var c = Command(db, "SELECT json FROM analysis_cache WHERE hash=$h", ("$h", key)); var s = c.ExecuteScalar() as string; return s is null ? null : JsonSerializer.Deserialize<Analysis>(s); }
    public void Cache(string key, Analysis a) { using var db = Open(); using var c = Command(db, "INSERT OR REPLACE INTO analysis_cache VALUES($h,$j)", ("$h", key), ("$j", JsonSerializer.Serialize(a))); c.ExecuteNonQuery(); }
    public void Session(DateTimeOffset start, double seconds, CaptureSource source) { using var db = Open(); using var c = Command(db, "INSERT INTO sessions(started,seconds,source) VALUES($s,$d,$src)", ("$s", start.ToString("O")), ("$d", seconds), ("$src", source.ToString())); c.ExecuteNonQuery(); }
    public Statistics Stats()
    {
        using var db = Open(); long Scalar(string sql) { using var c = Command(db, sql); return Convert.ToInt64(c.ExecuteScalar()); }
        int total = (int)Scalar("SELECT COUNT(*) FROM vocabulary"), reviews = (int)Scalar("SELECT COUNT(*) FROM reviews");
        var dates = new HashSet<DateOnly>(); using (var c = Command(db, "SELECT DISTINCT substr(timestamp,1,10) FROM reviews")) using (var r = c.ExecuteReader()) while (r.Read()) dates.Add(DateOnly.Parse(r.GetString(0))); int streak = 0; var day = DateOnly.FromDateTime(DateTime.UtcNow); if (!dates.Contains(day)) day = day.AddDays(-1); while (dates.Contains(day)) { streak++; day = day.AddDays(-1); }
        long heard = Scalar("SELECT COUNT(*) FROM encounters WHERE source='pc_audio' AND timestamp>=datetime('now','-7 days')"), known = Scalar("SELECT COUNT(*) FROM encounters e JOIN vocabulary v ON v.id=e.item_id WHERE e.source='pc_audio' AND v.status IN (2,3) AND e.timestamp>=datetime('now','-7 days')");
        using var sec = Command(db, "SELECT COALESCE(SUM(seconds),0) FROM sessions");
        return new(total, (int)Scalar("SELECT COUNT(*) FROM vocabulary WHERE status=0"), (int)Scalar($"SELECT COUNT(*) FROM vocabulary WHERE status IN (1,3) AND due<='{DateTimeOffset.UtcNow:O}'"), (int)Scalar("SELECT COUNT(*) FROM vocabulary WHERE status=1"), (int)Scalar("SELECT COUNT(*) FROM vocabulary WHERE status=3"), reviews, reviews == 0 ? 0 : (double)Scalar("SELECT COUNT(*) FROM reviews WHERE rating>1") / reviews, streak, Convert.ToDouble(sec.ExecuteScalar()), (int)Scalar("SELECT COUNT(*) FROM encounters"), heard == 0 ? null : (double)known / heard);
    }
    public void Export(string destination, UserSettings? settings = null)
    {
        using var db = Open(); var tables = new Dictionary<string, List<Dictionary<string, object?>>>();
        foreach (var table in new[] { "vocabulary", "encounters", "reviews", "transcripts", "sessions" }) { using var c = Command(db, $"SELECT * FROM {table}"); using var r = c.ExecuteReader(); var rows = new List<Dictionary<string, object?>>(); while (r.Read()) { var row = new Dictionary<string, object?>(); for (int i = 0; i < r.FieldCount; i++) row[r.GetName(i)] = r.IsDBNull(i) ? null : r.GetValue(i); rows.Add(row); } tables[table] = rows; }
        var result = tables.ToDictionary(x => x.Key, x => (object?)x.Value); result["schemaVersion"] = 2; if (settings is not null) result["settings"] = settings;
        File.WriteAllText(destination, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
    }
    public void Backup(string destination) { using var src = Open(); using var dst = new SqliteConnection(new SqliteConnectionStringBuilder { DataSource = destination, Pooling = false }.ToString()); dst.Open(); src.BackupDatabase(dst); }
    public void DeleteAll() { using var db = Open(); using var c = Command(db, "DELETE FROM vocabulary;DELETE FROM transcripts;DELETE FROM sessions;DELETE FROM analysis_cache;"); c.ExecuteNonQuery(); }
    private static void SeedCore(SqliteConnection db)
    {
        using var exists = Command(db, "SELECT value FROM metadata WHERE key='core_seed_v1'"); if (exists.ExecuteScalar() is not null) return;
        // Original short translations, no copied dictionary entries or invented real-life encounters.
        string[] seed = ["take care of|заботиться о|B1|Conversation", "pay attention|обращать внимание|A2|Conversation", "make a decision|принять решение|B1|Conversation", "make a mistake|допустить ошибку|A2|Conversation", "be responsible for|отвечать за|B1|Work", "I don't mind|я не против|A2|Conversation", "It depends|это зависит от обстоятельств|A2|Conversation", "That makes sense|это логично|B1|Conversation", "Fair enough|справедливо / вполне разумно|B1|Conversation", "What are you up to?|чем занимаешься?|B1|Conversation", "go live|начать прямой эфир|B1|Streaming", "edit a video|монтировать видео|A2|Streaming", "upload content|загружать контент|A2|Streaming", "fix a bug|исправить ошибку в программе|B1|Development", "build an app|создать приложение|A2|Development", "make a request|отправить запрос|B1|Development", "meet a deadline|уложиться в срок|B1|Work", "apply for a job|подать заявку на работу|B1|Work", "work remotely|работать удалённо|B1|Work", "book a room|забронировать номер|A2|Travel", "check in|зарегистрироваться / заселиться|A2|Travel", "carry-on luggage|ручная кладь|B1|Travel", "slice the bread|нарезать хлеб|A2|Food", "stir the sauce|перемешать соус|B1|Food", "boil water|кипятить воду|A1|Food", "wash the dishes|мыть посуду|A1|Home", "take out the rubbish|вынести мусор|A2|Home", "turns out|оказывается|B1|Movies", "takes place|происходит / разворачивается|B1|Movies", "plot twist|неожиданный поворот сюжета|B2|Movies", "complete a quest|выполнить задание|A2|Games", "deal damage|наносить урон|B1|Games", "level up|повысить уровень|A2|Games", "keep in touch|поддерживать связь|B1|Conversation", "give it a try|попробовать|A2|Conversation", "run out of|израсходовать / остаться без|B1|Conversation"];
        using var tx = db.BeginTransaction(); foreach (var row in seed) { var p = row.Split('|'); using var c = Command(db, "INSERT OR IGNORE INTO vocabulary(normalized,target,translation,type,cefr,topic,status,importance,relevance,first_seen,last_seen,due) VALUES($n,$t,$tr,'core phrase',$c,$topic,5,0.2,0.1,$time,$time,$time)", ("$n", Normalize(p[0])), ("$t", p[0]), ("$tr", p[1]), ("$c", p[2]), ("$topic", p[3]), ("$time", DateTimeOffset.UtcNow.ToString("O"))); c.Transaction = tx; c.ExecuteNonQuery(); }
        using var mark = Command(db, "INSERT INTO metadata VALUES('core_seed_v1','installed')"); mark.Transaction = tx; mark.ExecuteNonQuery(); tx.Commit();
    }
}
