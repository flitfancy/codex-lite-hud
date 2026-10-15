using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

// Drives the same queued-change/Refresh path as FileSystemWatcher, without
// relying on callback timing. Reads are OS process I/O bytes, including cache.
static class QuotaIoChecks
{
    [StructLayout(LayoutKind.Sequential)] struct Counts {public ulong reads,writes,other,readBytes,writeBytes,otherBytes;}
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process,out Counts counts);
    static string Quota(int used,DateTimeOffset at){return Program.Json.Serialize(new{timestamp=at.ToString("o"),type="event_msg",payload=new{type="token_count",rate_limits=new{limit_id="codex",primary=new{used_percent=used,window_minutes=10080}}}})+"\n";}
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            string folder=Path.Combine(Program.CodexHome,"sessions"),path=Path.Combine(folder,"quota-io.jsonl");Directory.CreateDirectory(folder);
            string padding="{\"timestamp\":\"2026-01-01T00:00:00Z\",\"type\":\"response_item\",\"payload\":{\"type\":\"custom_tool_call_output\",\"output\":\""+new string('x',200)+"\"}}\n";
            using(var writer=new StreamWriter(path,false,new UTF8Encoding(false))){for(int i=0;i<4096;i++)writer.Write(padding);writer.Write(Program.Json.Serialize(new{timestamp=DateTimeOffset.UtcNow.ToString("o"),type="event_msg",payload=new{type="token_count",rate_limits=new{limit_id="codex",primary=new{used_percent=20,window_minutes=10080}}}})+"\n");}
            QuotaLogs.Refresh();
            var notify=typeof(QuotaLogs).GetMethod("ChangedFile",BindingFlags.NonPublic|BindingFlags.Static);
            var change=new FileSystemEventArgs(WatcherChangeTypes.Changed,folder,Path.GetFileName(path));
            notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();
            var process=Process.GetCurrentProcess();Counts before,after;GetProcessIoCounters(process.Handle,out before);
            var clock=new Stopwatch();int gc=GC.CollectionCount(0);
            for(int i=0;i<64;i++){File.AppendAllText(path,padding,new UTF8Encoding(false));clock.Start();notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();clock.Stop();}
            clock.Stop();GetProcessIoCounters(process.Handle,out after);
            ulong read=after.readBytes-before.readBytes;
            string result="64 small log appends: "+read+" read bytes, quota processing "+clock.Elapsed.TotalMilliseconds.ToString("0.0")+" ms (excludes fixture writes), gen0 collections="+(GC.CollectionCount(0)-gc)+". Budget < 1 MiB after warm-up.";
            if(read>=1048576)throw new Exception("Repeated quota reads exceed incremental I/O budget: "+result);
            var at=DateTimeOffset.UtcNow.AddMinutes(1);string line=Quota(30,at);
            File.AppendAllText(path,line.Substring(0,line.Length/2),new UTF8Encoding(false));notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();
            if(QuotaReader.Limiting(QuotaReader.Current()).remaining!=80)throw new Exception("Incomplete line changed quota");
            File.AppendAllText(path,line.Substring(line.Length/2),new UTF8Encoding(false));notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();
            if(QuotaReader.Limiting(QuotaReader.Current()).remaining!=70)throw new Exception("Completed partial line was lost");
            File.WriteAllText(path,Quota(40,at.AddSeconds(1)),new UTF8Encoding(false));notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();
            if(QuotaReader.Limiting(QuotaReader.Current()).remaining!=60)throw new Exception("Truncated log did not reset cursor");
            File.WriteAllText(path,Quota(50,at.AddSeconds(2)),new UTF8Encoding(false));File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(2));notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();
            if(QuotaReader.Limiting(QuotaReader.Current()).remaining!=50)throw new Exception("Same-size rewrite did not reset cursor");
            File.AppendAllText(path,new string('x',300000)+"\n"+Quota(60,at.AddSeconds(3)),new UTF8Encoding(false));notify.Invoke(null,new object[]{null,change});QuotaLogs.Refresh();
            if(QuotaReader.Limiting(QuotaReader.Current()).remaining!=40)throw new Exception("Long unrelated line hid following quota");
            File.Delete(path);
            File.WriteAllText(args[0],"PASS "+result+" PASS partial line, truncation, same-size rewrite and huge unrelated line.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
