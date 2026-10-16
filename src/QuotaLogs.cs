using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;

// Account quota is shared across chats. Read only the tail of changed logs,
// independently of the conversation currently displayed by the HUD.
static class QuotaLogs
{
    static readonly object Gate=new object();
    static readonly HashSet<string> pending=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    static readonly List<FileSystemWatcher> watchers=new List<FileSystemWatcher>();
    static readonly Dictionary<string,Tail> tails=new Dictionary<string,Tail>(StringComparer.OrdinalIgnoreCase);
    // Refresh owns Gate across all tail reads, so their 128 cursors can share
    // one I/O buffer while keeping their partial records independent.
    static readonly byte[] buffer=new byte[8192];
    static readonly byte[] tokenMarker=Encoding.ASCII.GetBytes("\"token_count\"");
    sealed class Tail
    {
        internal long seen;
        // Account discovery reads only the last 256 KiB of a newly seen file.
        // Unrelated tool output is rejected after its first KiB; quota records
        // retain the existing 64 KiB line limit and partial-line continuation.
        internal readonly IncrementalLogReader reader=new IncrementalLogReader(262144,65536,true,ContainsToken,buffer);
    }
    static bool ContainsToken(byte[] bytes,int count)
    {
        for(int i=0;i<=count-tokenMarker.Length;i++)
        {
            if(bytes[i]!=tokenMarker[0])continue;
            int at=1;while(at<tokenMarker.Length && bytes[i+at]==tokenMarker[at])at++;
            if(at==tokenMarker.Length)return true;
        }
        return false;
    }
    static DateTime lastScan=DateTime.MinValue;
    static bool started;
    static int pendingCount;
    // The UI timer must never wait for filesystem work held under Gate.
    internal static bool Changed {get{return Volatile.Read(ref pendingCount)>0;}}

    static void ChangedFile(object sender,FileSystemEventArgs e)
    {
        lock(Gate){if(pending.Count<128)pending.Add(e.FullPath);else lastScan=DateTime.MinValue;Volatile.Write(ref pendingCount,pending.Count);}
    }
    internal static void Refresh()
    {
        lock(Gate)
        {
            bool cold=!started;
            if(!started)
            {
                started=true;
                foreach(string name in new[]{"sessions","archived_sessions"})
                {
                    string root=Path.Combine(Program.CodexHome,name);
                    if(!Directory.Exists(root))continue;
                    try
                    {
                        var watcher=new FileSystemWatcher(root,"*.jsonl"){IncludeSubdirectories=true,NotifyFilter=NotifyFilters.LastWrite|NotifyFilters.Size|NotifyFilters.FileName};
                        watcher.Changed+=ChangedFile;watcher.Created+=ChangedFile;watcher.Renamed+=delegate(object sender,RenamedEventArgs e){ChangedFile(sender,e);};
                        watcher.Error+=delegate{lock(Gate)lastScan=DateTime.MinValue;};
                        watcher.EnableRaisingEvents=true;watchers.Add(watcher);
                    }
                    catch { }
                }
            }
            // Periodic metadata fallback also recovers dropped filesystem events.
            if(DateTime.UtcNow-lastScan>TimeSpan.FromSeconds(30))
            {
                lastScan=DateTime.UtcNow;
                var recent=new List<FileInfo>();
                foreach(string name in new[]{"sessions","archived_sessions"})
                {
                    string root=Path.Combine(Program.CodexHome,name);
                    AddFiles(root,recent);
                    // An old chat can have the newest quota. On startup inspect
                    // file metadata across dated folders once, never their bodies.
                    if(cold)
                    {
                        try{if(Directory.Exists(root))recent.AddRange(new DirectoryInfo(root).EnumerateFiles("*.jsonl",SearchOption.AllDirectories).OrderByDescending(x=>x.LastWriteTimeUtc).Take(16));}catch{ }
                    }
                    for(int day=-1;day<7;day++)AddFiles(Path.Combine(root,DateTime.Now.AddDays(-day).ToString("yyyy\\MM\\dd")),recent);
                }
                // Recursive startup discovery and recent dated folders overlap.
                // Deduplicate before the limit so all sixteen slots are files.
                foreach(var file in recent.GroupBy(x=>x.FullName,StringComparer.OrdinalIgnoreCase).Select(x=>x.First()).OrderByDescending(x=>x.LastWriteTimeUtc).Take(16))pending.Add(file.FullName);
            }
            var paths=pending.ToArray();pending.Clear();Volatile.Write(ref pendingCount,0);
            foreach(var path in paths)ReadTail(path);
        }
    }
    static void AddFiles(string folder,List<FileInfo> files)
    {
        try{if(Directory.Exists(folder))files.AddRange(new DirectoryInfo(folder).GetFiles("*.jsonl"));}catch{ }
    }
    static void ReadTail(string path)
    {
        try
        {
            if(!File.Exists(path))return;
            Tail tail;
            if(!tails.TryGetValue(path,out tail))
            {
                if(tails.Count>=128)tails.Remove(tails.OrderBy(x=>x.Value.seen).First().Key);
                tail=new Tail();tails[path]=tail;
            }
            tail.seen=DateTime.UtcNow.Ticks;
            tail.reader.Read(path,IgnoreReset,ParseLine);
        }
        catch { }
    }
    static void IgnoreReset() { }
    static void ParseLine(string line)
    {
        try
        {
            var root=SessionReader.Map(Program.Json.DeserializeObject(line));
            if(SessionReader.Text(SessionReader.At(root,"type"))!="event_msg")return;
            var payload=SessionReader.Map(SessionReader.At(root,"payload"));
            if(SessionReader.Text(SessionReader.At(payload,"type"))!="token_count")return;
            DateTimeOffset at;if(!DateTimeOffset.TryParse(SessionReader.Text(SessionReader.At(root,"timestamp")),out at))return;
            var snapshot=QuotaReader.FromSession(SessionReader.Map(SessionReader.At(payload,"rate_limits")),at);
            if(snapshot!=null){snapshot.source="本机账号记录（跨会话）";QuotaReader.Current(snapshot);}
        }
        catch { }
    }
}
