// Query-latency benchmark: SQLite vs PostgreSQL for the Session Post-Mortem read path.
//
// Loads the real Copilot corpus into an equivalent schema on both engines, then times the
// query shapes the three surfaces actually issue. Ingest time is not the subject; the
// question is whether querying is prompt.
//
// Usage: dotnet run -- <copilot-session-state-dir> <pg-connstring> <scale>

using System.Diagnostics;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Npgsql;

var sourceDir = args[0];
var pgConn = args[1];
var scale = int.Parse(args[2]);          // 1 = real corpus; N = corpus duplicated N times
var sqlitePath = Path.Combine(AppContext.BaseDirectory, $"bench-{scale}x.db");

Console.WriteLine($"scale {scale}x  sqlite {sqlitePath}");

// ---------- read the corpus into memory-efficient records ----------

static IEnumerable<Ev> ReadCorpus(string dir, int scale)
{
    var files = Directory.GetFiles(dir, "events.jsonl", SearchOption.AllDirectories);
    for (var copy = 0; copy < scale; copy++)
    {
        foreach (var file in files)
        {
            var baseSession = Path.GetFileName(Path.GetDirectoryName(file))!;
            var session = copy == 0 ? baseSession : $"{baseSession}-c{copy}";
            var seq = 0;
            foreach (var line in File.ReadLines(file))
            {
                if (line.Length == 0) continue;
                seq++;
                string type = "<none>", ts = "";
                string? toolCallId = null, toolName = null, agentId = null, path = null;
                int? success = null;
                try
                {
                    using var doc = JsonDocument.Parse(line);
                    var root = doc.RootElement;
                    if (root.TryGetProperty("type", out var t)) type = t.GetString() ?? type;
                    if (root.TryGetProperty("timestamp", out var s)) ts = s.GetString() ?? "";
                    if (root.TryGetProperty("agentId", out var a)) agentId = a.GetString();
                    if (root.TryGetProperty("data", out var d) && d.ValueKind == JsonValueKind.Object)
                    {
                        if (d.TryGetProperty("toolCallId", out var tc)) toolCallId = tc.GetString();
                        if (d.TryGetProperty("toolName", out var tn)) toolName = tn.GetString();
                        if (d.TryGetProperty("success", out var sc) &&
                            (sc.ValueKind == JsonValueKind.True || sc.ValueKind == JsonValueKind.False))
                            success = sc.GetBoolean() ? 1 : 0;
                        if (d.TryGetProperty("arguments", out var ar) && ar.ValueKind == JsonValueKind.Object
                            && ar.TryGetProperty("path", out var p)) path = p.GetString();
                    }
                }
                catch (JsonException) { }
                yield return new Ev(session, seq, type, ts, line, toolCallId, toolName, success, agentId, path);
            }
        }
    }
}

// ---------- schema, identical in shape on both engines ----------

const string SqliteSchema = """
CREATE TABLE raw_event(
  id INTEGER PRIMARY KEY, session_id TEXT NOT NULL, seq INTEGER NOT NULL,
  event_type TEXT NOT NULL, ts TEXT NOT NULL, payload TEXT NOT NULL);
CREATE TABLE tool_call(
  id INTEGER PRIMARY KEY, session_id TEXT NOT NULL, tool_call_id TEXT,
  tool_name TEXT NOT NULL, ts TEXT NOT NULL, success INTEGER, agent_id TEXT, path TEXT);
CREATE TABLE finding(id INTEGER PRIMARY KEY, class TEXT, provenance TEXT, sessions_affected INTEGER);
CREATE TABLE finding_session(finding_id INTEGER, session_id TEXT);
""";

const string PgSchema = """
CREATE TABLE raw_event(
  id BIGSERIAL PRIMARY KEY, session_id TEXT NOT NULL, seq INTEGER NOT NULL,
  event_type TEXT NOT NULL, ts TEXT NOT NULL, payload TEXT NOT NULL);
CREATE TABLE tool_call(
  id BIGSERIAL PRIMARY KEY, session_id TEXT NOT NULL, tool_call_id TEXT,
  tool_name TEXT NOT NULL, ts TEXT NOT NULL, success INTEGER, agent_id TEXT, path TEXT);
CREATE TABLE finding(id INT PRIMARY KEY, class TEXT, provenance TEXT, sessions_affected INT);
CREATE TABLE finding_session(finding_id INT, session_id TEXT);
""";

const string Indexes = """
CREATE INDEX ix_raw_session_seq ON raw_event(session_id, seq);
CREATE INDEX ix_raw_type ON raw_event(event_type);
CREATE INDEX ix_tc_session ON tool_call(session_id);
CREATE INDEX ix_tc_name ON tool_call(tool_name);
CREATE INDEX ix_tc_session_path ON tool_call(session_id, path);
CREATE INDEX ix_fs_finding ON finding_session(finding_id);
CREATE INDEX ix_tc_name_success ON tool_call(tool_name, success);
CREATE INDEX ix_tc_session_name ON tool_call(session_id, tool_name);
""";

// ---------- load ----------

Console.WriteLine("loading sqlite...");
if (File.Exists(sqlitePath)) File.Delete(sqlitePath);
var sw = Stopwatch.StartNew();
long rawRows = 0, toolRows = 0;
var sessions = new List<string>();
var sessionCounts = new Dictionary<string, int>();

using (var db = new SqliteConnection($"Data Source={sqlitePath}"))
{
    db.Open();
    Exec(db, "PRAGMA journal_mode=WAL; PRAGMA synchronous=OFF;");
    Exec(db, SqliteSchema);
    using var tx = db.BeginTransaction();
    var ins = db.CreateCommand();
    ins.CommandText = "INSERT INTO raw_event(session_id,seq,event_type,ts,payload) VALUES($s,$q,$t,$ts,$p)";
    var ps = Add(ins, "$s"); var pq = Add(ins, "$q"); var pt = Add(ins, "$t");
    var pts = Add(ins, "$ts"); var pp = Add(ins, "$p");

    var tins = db.CreateCommand();
    tins.CommandText = "INSERT INTO tool_call(session_id,tool_call_id,tool_name,ts,success,agent_id,path) " +
                       "VALUES($s,$c,$n,$ts,$ok,$a,$pa)";
    var ts_ = Add(tins, "$s"); var tc_ = Add(tins, "$c"); var tn_ = Add(tins, "$n");
    var tts_ = Add(tins, "$ts"); var tok_ = Add(tins, "$ok"); var ta_ = Add(tins, "$a");
    var tpa_ = Add(tins, "$pa");

    foreach (var e in ReadCorpus(sourceDir, scale))
    {
        ps.Value = e.Session; pq.Value = e.Seq; pt.Value = e.Type; pts.Value = e.Ts; pp.Value = e.Payload;
        ins.ExecuteNonQuery(); rawRows++;
        sessionCounts[e.Session] = sessionCounts.GetValueOrDefault(e.Session) + 1;

        if (e.Type == "tool.execution_start" || e.Type == "tool.execution_complete")
        {
            ts_.Value = e.Session; tc_.Value = (object?)e.ToolCallId ?? DBNull.Value;
            tn_.Value = e.ToolName ?? "<none>"; tts_.Value = e.Ts;
            tok_.Value = (object?)e.Success ?? DBNull.Value;
            ta_.Value = (object?)e.AgentId ?? DBNull.Value;
            tpa_.Value = (object?)e.Path ?? DBNull.Value;
            tins.ExecuteNonQuery(); toolRows++;
        }
    }
    // a small precomputed FINDINGS layer, as the architecture specifies
    Exec(db, """
      INSERT INTO finding(id,class,provenance,sessions_affected)
      SELECT ROW_NUMBER() OVER (ORDER BY COUNT(DISTINCT session_id) DESC), 'waste','derived',
             COUNT(DISTINCT session_id) FROM tool_call GROUP BY tool_name;
      INSERT INTO finding_session(finding_id, session_id)
      SELECT f.id, tc.session_id FROM finding f
      JOIN (SELECT tool_name, session_id, ROW_NUMBER() OVER (PARTITION BY tool_name ORDER BY session_id) rn
            FROM (SELECT DISTINCT tool_name, session_id FROM tool_call)) tc
        ON tc.rn = 1 AND f.id = f.id WHERE f.id <= 40;
    """);
    Exec(db, Indexes);
    tx.Commit();
    Exec(db, "ANALYZE;");
    sessions = sessionCounts.OrderByDescending(k => k.Value).Select(k => k.Key).ToList();
}
Console.WriteLine($"sqlite loaded {rawRows:N0} raw, {toolRows:N0} tool_call in {sw.Elapsed.TotalSeconds:N1}s " +
                  $"({new FileInfo(sqlitePath).Length / 1048576.0:N0} MiB)");

Console.WriteLine("loading postgres...");
sw.Restart();
using (var pg = new NpgsqlConnection(pgConn))
{
    pg.Open();
    ExecPg(pg, "DROP TABLE IF EXISTS raw_event, tool_call, finding, finding_session CASCADE;");
    ExecPg(pg, PgSchema);
    using (var w = pg.BeginBinaryImport(
        "COPY raw_event(session_id,seq,event_type,ts,payload) FROM STDIN (FORMAT BINARY)"))
    {
        foreach (var e in ReadCorpus(sourceDir, scale))
        {
            w.StartRow(); w.Write(e.Session); w.Write(e.Seq); w.Write(e.Type); w.Write(e.Ts); w.Write(e.Payload);
        }
        w.Complete();
    }
    using (var w = pg.BeginBinaryImport(
        "COPY tool_call(session_id,tool_call_id,tool_name,ts,success,agent_id,path) FROM STDIN (FORMAT BINARY)"))
    {
        foreach (var e in ReadCorpus(sourceDir, scale))
        {
            if (e.Type != "tool.execution_start" && e.Type != "tool.execution_complete") continue;
            w.StartRow(); w.Write(e.Session);
            if (e.ToolCallId is null) w.WriteNull(); else w.Write(e.ToolCallId);
            w.Write(e.ToolName ?? "<none>"); w.Write(e.Ts);
            if (e.Success is null) w.WriteNull(); else w.Write(e.Success.Value);
            if (e.AgentId is null) w.WriteNull(); else w.Write(e.AgentId);
            if (e.Path is null) w.WriteNull(); else w.Write(e.Path);
        }
        w.Complete();
    }
    ExecPg(pg, """
      INSERT INTO finding(id,class,provenance,sessions_affected)
      SELECT ROW_NUMBER() OVER (ORDER BY COUNT(DISTINCT session_id) DESC)::int,'waste','derived',
             COUNT(DISTINCT session_id)::int FROM tool_call GROUP BY tool_name;
      INSERT INTO finding_session(finding_id, session_id)
      SELECT f.id, s.session_id FROM finding f
      CROSS JOIN LATERAL (SELECT DISTINCT session_id FROM tool_call LIMIT 12) s WHERE f.id <= 40;
    """);
    ExecPg(pg, Indexes);
    ExecPg(pg, "VACUUM ANALYZE;");
    var size = ScalarPg(pg, "SELECT pg_total_relation_size('raw_event')+pg_total_relation_size('tool_call')");
    Console.WriteLine($"postgres loaded in {sw.Elapsed.TotalSeconds:N1}s " +
                      $"({Convert.ToInt64(size) / 1048576.0:N0} MiB)");
}

// ---------- the query shapes ----------

var big = sessions[0];
Console.WriteLine($"largest session {big} = {sessionCounts[big]:N0} rows");
var versionSessions = sessions.Take(12).ToList();
var inList = string.Join(",", versionSessions.Select(s => $"'{s.Replace("'", "''")}'"));

var queries = new (string Name, string Sql, bool Param)[]
{
    ("Q1 digest ranking",     "SELECT id,class,provenance,sessions_affected FROM finding ORDER BY sessions_affected DESC LIMIT 20", false),
    ("Q2 recurrence strip",   "SELECT finding_id,session_id FROM finding_session WHERE finding_id <= 10", false),
    ("Q3 masthead counts",    "SELECT COUNT(*), COUNT(DISTINCT session_id) FROM raw_event", false),
    ("Q4 tape (narrow)",      "SELECT seq,event_type,ts FROM raw_event WHERE session_id = @s ORDER BY seq", true),
    ("Q5 tape (with payload)","SELECT seq,event_type,ts,payload FROM raw_event WHERE session_id = @s ORDER BY seq", true),
    ("Q6 raw tab (1 row)",    "SELECT payload FROM raw_event WHERE session_id = @s AND seq = 500", true),
    ("Q7 failure rates",      "SELECT tool_name, COUNT(*) n, SUM(CASE WHEN success=0 THEN 1 ELSE 0 END) f FROM tool_call GROUP BY tool_name ORDER BY n DESC", false),
    ("Q8 repeated reads",     "SELECT session_id, path, COUNT(*) c FROM tool_call WHERE path IS NOT NULL GROUP BY session_id, path HAVING COUNT(*) >= 4 ORDER BY c DESC LIMIT 50", false),
    ("Q9 adherence scope",    $"SELECT tool_name, COUNT(*) n FROM tool_call WHERE session_id IN ({inList}) GROUP BY tool_name ORDER BY n DESC", false),
};

const int Warmup = 3, Runs = 15;
var results = new List<(string Q, double SqliteMs, double PgMs, long Rows, long Bytes)>();

using var sq = new SqliteConnection($"Data Source={sqlitePath};Mode=ReadOnly");
sq.Open();
using var pgq = new NpgsqlConnection(pgConn);
pgq.Open();

foreach (var (name, sql, param) in queries)
{
    var (sms, rows, bytes) = Time(() => RunSqlite(sq, sql, param ? big : null));
    var (pms, _, _) = Time(() => RunPg(pgq, sql, param ? big : null));
    results.Add((name, sms, pms, rows, bytes));
    Console.WriteLine($"{name,-24} sqlite {sms,8:N2} ms   pg {pms,8:N2} ms   {rows,8:N0} rows  {bytes / 1024.0,9:N0} KiB");
}

Console.WriteLine();
Console.WriteLine("| Query | rows | SQLite median | Postgres median | winner |");
Console.WriteLine("|---|---|---|---|---|");
foreach (var r in results)
{
    var w = r.SqliteMs < r.PgMs ? $"SQLite {r.PgMs / r.SqliteMs:N1}x" : $"Postgres {r.SqliteMs / r.PgMs:N1}x";
    Console.WriteLine($"| {r.Q} | {r.Rows:N0} | {r.SqliteMs:N2} ms | {r.PgMs:N2} ms | {w} |");
}

// ---------- helpers ----------

(double, long, long) Time(Func<(long rows, long bytes)> run)
{
    for (var i = 0; i < Warmup; i++) run();
    var times = new List<double>();
    (long rows, long bytes) last = (0, 0);
    for (var i = 0; i < Runs; i++)
    {
        var t = Stopwatch.StartNew();
        last = run();
        times.Add(t.Elapsed.TotalMilliseconds);
    }
    times.Sort();
    return (times[times.Count / 2], last.rows, last.bytes);
}

static (long, long) RunSqlite(SqliteConnection c, string sql, string? session)
{
    using var cmd = c.CreateCommand();
    cmd.CommandText = sql;
    if (session is not null) cmd.Parameters.AddWithValue("@s", session);
    return Drain(cmd.ExecuteReader());
}

static (long, long) RunPg(NpgsqlConnection c, string sql, string? session)
{
    using var cmd = new NpgsqlCommand(sql, c);
    if (session is not null) cmd.Parameters.AddWithValue("@s", session);
    return Drain(cmd.ExecuteReader());
}

static (long, long) Drain(System.Data.Common.DbDataReader r)
{
    long rows = 0, bytes = 0;
    using (r)
        while (r.Read())
        {
            rows++;
            for (var i = 0; i < r.FieldCount; i++)
                if (!r.IsDBNull(i) && r.GetFieldType(i) == typeof(string))
                    bytes += r.GetString(i).Length;
        }
    return (rows, bytes);
}

static void Exec(SqliteConnection c, string sql)
{
    using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery();
}
static void ExecPg(NpgsqlConnection c, string sql)
{
    using var cmd = new NpgsqlCommand(sql, c); cmd.ExecuteNonQuery();
}
static object ScalarPg(NpgsqlConnection c, string sql)
{
    using var cmd = new NpgsqlCommand(sql, c); return cmd.ExecuteScalar()!;
}
static SqliteParameter Add(SqliteCommand c, string n)
{
    var p = c.CreateParameter(); p.ParameterName = n; c.Parameters.Add(p); return p;
}

record Ev(string Session, int Seq, string Type, string Ts, string Payload,
          string? ToolCallId, string? ToolName, int? Success, string? AgentId, string? Path);
