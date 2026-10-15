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
    static readonly byte[] buffer=new byte[8192];
    static readonly byte[] tokenMarker=Encoding.ASCII.GetBytes("\"token_count\"");
    sealed class Tail
    {
        internal long position,created,written,seen;
        internal byte[] line=new byte[1024];
        internal int count;
        internal bool skipping;
        internal void Append(byte[] bytes,int start,int length)
        {
            if(skipping)return;
            // Ordinary tool outputs can be megabytes long. Their first KiB
            // identifies them, so never retain the rest in the quota reader.
            int prefix=Math.Min(length,Math.Max(0,1024-count));
            if(prefix>0){Buffer.BlockCopy(bytes,start,line,count,prefix);count+=prefix;start+=prefix;length-=prefix;}
            if(count>=1024 && !ContainsToken(line,count)){skipping=true;count=0;return;}
            if(count+length>65536){skipping=true;count=0;return;}
            if(line.Length<count+length)Array.Resize(ref line,Math.Min(65536,Math.Max(line.Length*2,count+length)));
            if(length>0){Buffer.BlockCopy(bytes,start,line,count,length);count+=length;}
        }
        internal void Complete()
        {
            if(!skipping && count>0 && ContainsToken(line,count))ParseLine(Encoding.UTF8.GetString(line,0,count));
            count=0;skipping=false;
        }
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
                foreach(var file in recent.OrderByDescending(x=>x.LastWriteTimeUtc).Take(16))pending.Add(file.FullName);
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
            var file=new FileInfo(path);if(!file.Exists)return;
            Tail tail;
            long length=file.Length,created=file.CreationTimeUtc.Ticks,written=file.LastWriteTimeUtc.Ticks;
            if(!tails.TryGetValue(path,out tail) || tail.created!=created || length<tail.position || (length==tail.position && tail.written!=written))
            {
                if(tails.Count>=128 && !tails.ContainsKey(path))tails.Remove(tails.OrderBy(x=>x.Value.seen).First().Key);
                long start=Math.Max(0,length-262144);
                tail=new Tail{position=start,created=created,skipping=start>0};tails[path]=tail;
            }
            tail.seen=DateTime.UtcNow.Ticks;
            if(length==tail.position)return;
            using(var stream=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))
            {
                stream.Position=tail.position;
                while(tail.position<length)
                {
                    int read=stream.Read(buffer,0,(int)Math.Min(buffer.Length,length-tail.position));if(read==0)break;
                    int start=0;
                    while(start<read)
                    {
                        int end=Array.IndexOf(buffer,(byte)10,start,read-start);
                        tail.Append(buffer,start,(end<0?read:end)-start);
                        if(end<0)break;
                        tail.Complete();start=end+1;
                    }
                    tail.position+=read;
                }
                tail.written=written;
            }
        }
        catch { }
    }
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
