using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

sealed class SessionEntry
{
    public string id, title;
}

// Keep file reads and JSON parsing out of the one-second refresh when only
// metadata has been checked. Never cache a missing file or a changing snapshot.
sealed class MetadataFileCache<T>
{
    readonly object gate = new object();
    readonly string path;
    readonly Func<string, T> parse;
    bool hasValue;
    FileStamp stamp;
    T value;

    internal MetadataFileCache(string path, Func<string, T> parse)
    {
        this.path = path; this.parse = parse;
    }

    internal bool TryGet(out T result)
    {
        lock (gate)
        {
            var before = FileStamp.Read(path);
            if (!before.Exists) { hasValue = false; result = default(T); return false; }
            if (hasValue && stamp.Equals(before)) { result = value; return true; }
            string text;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream, Encoding.UTF8)) text = reader.ReadToEnd();
            T parsed = parse(text);
            var after = FileStamp.Read(path);
            hasValue = before.Equals(after);
            if (hasValue) { stamp = after; value = parsed; }
            result = parsed;
            return true;
        }
    }

    internal void Write(string text)
    {
        lock (gate)
        {
            // Invalidate first so even a partially failed write is re-read.
            hasValue = false;
            File.WriteAllText(path, text);
        }
    }

    struct FileStamp : IEquatable<FileStamp>
    {
        internal bool Exists;
        long length, modified, created;
        internal static FileStamp Read(string path)
        {
            var info = new FileInfo(path);
            if (!info.Exists) return new FileStamp();
            return new FileStamp { Exists = true, length = info.Length, modified = info.LastWriteTimeUtc.Ticks, created = info.CreationTimeUtc.Ticks };
        }
        public bool Equals(FileStamp other)
        {
            return Exists == other.Exists && length == other.length && modified == other.modified && created == other.created;
        }
    }
}

sealed class SessionCatalog
{
    readonly List<SessionEntry> rows;
    readonly Dictionary<string, string> titles;

    SessionCatalog(List<SessionEntry> rows, Dictionary<string, string> titles) { this.rows = rows; this.titles = titles; }

    internal static SessionCatalog Parse(string text)
    {
        var rows = new List<SessionEntry>();
        var titles = new Dictionary<string, string>();
        var lines = text.Split('\n');
        for (int i = lines.Length - 1; i >= 0; i--)
        {
            try
            {
                var row = SessionReader.Map(Program.Json.DeserializeObject(lines[i]));
                var id = SessionReader.Text(SessionReader.At(row, "id")); Guid parsed;
                if (Guid.TryParse(id, out parsed) && !titles.ContainsKey(id))
                {
                    string title = SessionReader.Text(SessionReader.At(row, "thread_name"));
                    titles.Add(id, title);
                    rows.Add(new SessionEntry { id = id, title = title });
                }
            }
            catch { }
        }
        return new SessionCatalog(rows, titles);
    }

    internal List<SessionEntry> Recent(int maximum)
    {
        int count = Math.Max(0, Math.Min(maximum, rows.Count));
        var result = new List<SessionEntry>(count);
        for (int i = 0; i < count; i++) result.Add(new SessionEntry { id = rows[i].id, title = rows[i].title });
        return result;
    }

    internal string Title(string id) { string title; return titles.TryGetValue(id, out title) ? title : null; }
}

static class SessionReader
{
    static readonly string Home = Program.CodexHome;
    static readonly string Selection = Path.Combine(Program.DataFolder, "session.json");
    static readonly MetadataFileCache<Dictionary<string, object>> Choice = new MetadataFileCache<Dictionary<string, object>>(Selection, text => Map(Program.Json.DeserializeObject(text)));
    static readonly MetadataFileCache<SessionCatalog> Catalog = new MetadataFileCache<SessionCatalog>(Path.Combine(Home, "session_index.jsonl"), SessionCatalog.Parse);
    static readonly Dictionary<string, SessionState> States = new Dictionary<string, SessionState>();
    static readonly Regex Timestamp = new Regex("\\\"timestamp\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"", RegexOptions.Compiled);
    static readonly Regex RecordType=new Regex("\\\"type\\\"\\s*:\\s*\\\"([^\\\"]+)\\\"",RegexOptions.Compiled);

    internal static Dictionary<string, object> Map(object o) { return o as Dictionary<string, object>; }
    internal static object At(Dictionary<string, object> o, string key) { object v; return o != null && o.TryGetValue(key, out v) ? v : null; }
    internal static long Count(object o) { return o == null ? 0 : Convert.ToInt64(o, CultureInfo.InvariantCulture); }
    internal static string Text(object o) { return Convert.ToString(o, CultureInfo.InvariantCulture); }

    internal static string SelectedId()
    {
        Dictionary<string, object> value;
        if (!Choice.TryGet(out value)) throw new FileNotFoundException("请右键选择要查看的会话", Selection);
        var id = Text(At(value, "id")); Guid parsed;
        if (!Guid.TryParse(id, out parsed)) throw new InvalidDataException("请右键选择要查看的会话");
        return id;
    }

    internal static void Select(string id)
    {
        Guid parsed;
        if (!Guid.TryParse(id, out parsed)) throw new InvalidDataException("无效的会话 ID");
        Choice.Write(Program.Json.Serialize(new { id = id, automatic = false }));
    }

    internal static bool IsAutomatic()
    {
        try
        {
            Dictionary<string, object> value;
            if (!Choice.TryGet(out value)) return true;
            return !Object.Equals(At(value,"automatic"),false);
        }
        catch { return true; }
    }

    internal static void Automatic(bool enabled)
    {
        string id = ""; try { id = SelectedId(); } catch { }
        Choice.Write(Program.Json.Serialize(new { id = id, automatic = enabled }));
    }

    internal static List<SessionEntry> Recent(int maximum = 30)
    {
        SessionCatalog catalog;
        var indexed=Catalog.TryGet(out catalog)?catalog.Recent(Int32.MaxValue):new List<SessionEntry>();
        var indexedTitles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in indexed)if(!indexedTitles.ContainsKey(row.id))indexedTitles.Add(row.id,row.title);
        var result=new List<SessionEntry>();var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in NativeThreadCatalog.Recent())
        {
            string fallback;string title=row.title;
            if(String.IsNullOrEmpty(title) && indexedTitles.TryGetValue(row.id,out fallback))title=fallback;
            if(seen.Add(row.id))result.Add(new SessionEntry{id=row.id,title=title});
        }
        foreach(var row in indexed)if(seen.Add(row.id))result.Add(row);
        return result.Take(Math.Max(0,maximum)).ToList();
    }

    internal static Data Read(string overrideId = null)
    {
        string id = overrideId ?? SelectedId();
        SessionState state;
        if (!States.TryGetValue(id, out state))
        {
            if (States.Count > 5) States.Clear();
            state = new SessionState { Id = id, Path = FindRollout(id) };
            States[id] = state;
        }
        state.Update();
        SessionCatalog catalog;
        string title=NativeThreadCatalog.Title(id);
        if(String.IsNullOrEmpty(title))title=Catalog.TryGet(out catalog)?catalog.Title(id):null;
        return new Data
        {
            sessionId = id,
            sessionTitle = String.IsNullOrEmpty(title) ? id.Substring(0, 8) : title,
            model = state.Model,
            input = state.Input, output = state.Output, cache = state.Cache,
            lastInput = state.LastInput, lastOutput = state.LastOutput,
            contextUsed = state.ContextUsed, contextLimit = state.ContextLimit,
            contextPercent = state.ContextLimit > 0 ? (double?)Math.Round(100d * state.ContextUsed / state.ContextLimit, 1) : null,
            requestToks = state.RequestMs >= 100 && state.LastOutput > 0 ? (double?)Math.Round(1000d * state.LastOutput / state.RequestMs, 1) : null,
            requestMs = state.RequestMs,
            turns=state.Counters.Turns,completedTurns=state.Counters.Completed,abortedTurns=state.Counters.Aborted,activeTurns=state.Counters.Active,compactions=state.Counters.Compactions,
            runningMs=state.Timing.Running(DateTimeOffset.UtcNow),spanMs=state.Timing.Span(DateTimeOffset.UtcNow),
            accountQuota = state.Quota,
            usageAt = state.UsageAt == DateTimeOffset.MinValue ? null : state.UsageAt.ToLocalTime().ToString("o"),
            readAt = DateTimeOffset.Now.ToString("o")
        };
    }

    static string FindRollout(string id)
    {
        Guid parsed;
        if (!Guid.TryParse(id, out parsed)) throw new InvalidDataException("无效的会话 ID");
        string sessions = Path.Combine(Home, "sessions");
        try
        {
            var time = new DateTime(1970, 1, 1).AddMilliseconds(Convert.ToInt64(id.Replace("-", "").Substring(0, 12), 16));
            foreach (var day in new[] { time.Date, time.Date.AddDays(1), time.Date.AddDays(-1) })
            {
                var folder = Path.Combine(sessions, day.ToString("yyyy"), day.ToString("MM"), day.ToString("dd"));
                if (!Directory.Exists(folder)) continue;
                var hit = Directory.GetFiles(folder, "*" + id + "*.jsonl").FirstOrDefault();
                if (hit != null) return hit;
            }
        }
        catch { }
        foreach (var root in new[] { sessions, Path.Combine(Home, "archived_sessions") })
        {
            if (!Directory.Exists(root)) continue;
            var hit = Directory.EnumerateFiles(root, "*" + id + "*.jsonl", SearchOption.AllDirectories).FirstOrDefault();
            if (hit != null) return hit;
        }
        throw new FileNotFoundException("这段会话没有可读取的本机记录");
    }

    sealed class SessionState
    {
        internal string Id, Path, Model;
        internal long Input, Output, Cache, LastInput, LastOutput, ContextUsed, ContextLimit, RequestMs;
        internal DateTimeOffset UsageAt, ContextAt, LastUsageAt, CallStart;
        internal QuotaSnapshot Quota;
        internal readonly SessionCounters Counters=new SessionCounters();
        internal readonly SessionTiming Timing=new SessionTiming();
        readonly IncrementalLogReader reader=new IncrementalLogReader(0,16777216,false,null);

        internal void Update()
        {
            try { reader.Read(Path,Reset,ProcessLine); }
            catch(FileNotFoundException) { Relocate(); }
            catch(DirectoryNotFoundException) { Relocate(); }
        }
        void Relocate()
        {
            // Archiving changes the path, not the session. The shared reader
            // preserves its partial line when the reopened file has the same
            // identity, and resets statistics when it is a replacement.
            Path=FindRollout(Id);reader.Read(Path,Reset,ProcessLine);
        }
        void Reset()
        {
            Model=null;
            Input=Output=Cache=LastInput=LastOutput=ContextUsed=ContextLimit=RequestMs=0;
            UsageAt=ContextAt=LastUsageAt=CallStart=DateTimeOffset.MinValue;
            Quota=null;Counters.Reset();Timing.Reset();
        }
        void SetLast(Dictionary<string,object> usage,DateTimeOffset at,bool hasRequestTiming)
        {
            if(usage==null || at<LastUsageAt)return;
            object input=At(usage,"input_tokens"),output=At(usage,"output_tokens");
            // Some token_count records carry only total_tokens. They can update
            // context without erasing a complete request from the other format.
            if(input==null && output==null)return;
            if(hasRequestTiming)
            {
                if(CallStart!=DateTimeOffset.MinValue && at>CallStart)RequestMs=(long)(at-CallStart).TotalMilliseconds;
                else if(at>LastUsageAt)RequestMs=0;
                CallStart=at;
            }
            else if(at>LastUsageAt)RequestMs=0;
            LastInput=Count(input);LastOutput=Count(output);LastUsageAt=at;
        }

        void ProcessLine(string line)
        {
            string head = line.Substring(0, Math.Min(line.Length, 700));
            var match = Timestamp.Match(head);
            DateTimeOffset at;
            if (!match.Success || !DateTimeOffset.TryParse(match.Groups[1].Value, out at)) return;
            Timing.Observe(at);
            string recordType=RecordType.Match(head).Groups[1].Value;
            if(recordType=="compacted"){Counters.Compacted(line);return;}
            if (recordType=="response_item")
            {
                if (head.Contains("\"custom_tool_call_output\"") || Regex.IsMatch(head, "\\\"role\\\"\\s*:\\s*\\\"user\\\"")) CallStart = at;
                return;
            }
            bool turnEvent=recordType=="event_msg" && (head.Contains("\"task_started\"") || head.Contains("\"task_complete\"") || head.Contains("\"turn_aborted\""));
            if (!turnEvent && !head.Contains("\"token_usage_record\"") && !head.Contains("\"token_count\"") && !head.Contains("\"turn_context\"")) return;
            Dictionary<string, object> root;
            try { root = Map(Program.Json.DeserializeObject(line)); } catch { return; }
            string type = Text(At(root, "type"));
            var p = Map(At(root, "payload"));
            if(turnEvent){Counters.Event(p);Timing.Event(p,at);return;}
            if (type == "turn_context") { Model = Text(At(p, "model")); return; }
            if (type == "token_usage_record")
            {
                if (Text(At(p, "thread_id")) != "" && Text(At(p, "thread_id")) != Id) return;
                var total = Map(At(p, "thread_token_usage"));
                if (total != null && at >= UsageAt) { SetTotal(total); UsageAt = at; }
                var last = Map(At(p, "usage"));
                if (last != null)
                {
                    SetLast(last,at,true);
                    if (at >= ContextAt) { ContextUsed = Count(At(last, "total_tokens")); ContextAt = at; }
                }
            }
            else if (type == "event_msg" && Text(At(p, "type")) == "token_count")
            {
                var snapshot=QuotaReader.FromSession(Map(At(p,"rate_limits")),at);
                if(snapshot!=null)Quota=snapshot;
                var info = Map(At(p, "info"));
                if (info == null) return;
                var limit = At(info, "model_context_window");
                if (limit != null) ContextLimit = Count(limit);
                var last = Map(At(info, "last_token_usage"));
                SetLast(last,at,false);
                if (last != null && at >= ContextAt) { ContextUsed = Count(At(last, "total_tokens")); ContextAt = at; }
                var total = Map(At(info, "total_token_usage"));
                if (total != null && at >= UsageAt) { SetTotal(total); UsageAt = at; }
            }
        }

        void SetTotal(Dictionary<string, object> total)
        {
            Input = Count(At(total, "input_tokens")); Output = Count(At(total, "output_tokens")); Cache = Count(At(total, "cached_input_tokens"));
        }
    }
}
