using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Exercise production readers against generated logs. Do not use a real Codex
// home: scripts/test.ps1 supplies an isolated home and data directory.
static class SessionRecoveryChecks
{
    [StructLayout(LayoutKind.Sequential)] struct IoCounts { public ulong reads,writes,other,readBytes,writeBytes,otherBytes; }
    [DllImport("kernel32.dll")] static extern bool GetProcessIoCounters(IntPtr process,out IoCounts counts);
    static readonly UTF8Encoding Utf8=new UTF8Encoding(false);
    static readonly DateTimeOffset Start=DateTimeOffset.UtcNow.AddMinutes(10);
    static readonly List<string> report=new List<string>(),files=new List<string>();
    static string activeFolder,archiveFolder;
    static int passed,failed;

    static void Require(bool condition,string message) { if(!condition)throw new Exception(message); }
    static void Check(string name,Action action)
    {
        try { action();passed++;report.Add("PASS "+name); }
        catch(Exception ex) { failed++;report.Add("FAIL "+name+": "+ex.GetBaseException().Message); }
    }
    static string Record(string kind,object payload,int second)
    {
        return Program.Json.Serialize(new {timestamp=Start.AddSeconds(second).ToString("o"),type=kind,payload=payload})+"\n";
    }
    static object Usage(long input,long output) { return new {input_tokens=input,output_tokens=output,total_tokens=input+output,cached_input_tokens=0}; }
    static string Token(object last,object total,int second)
    {
        return Record("event_msg",new {type="token_count",info=new {model_context_window=100000,last_token_usage=last,total_token_usage=total}},second);
    }
    static string Legacy(string id,long input,long output,int second)
    {
        return Record("token_usage_record",new {thread_id=id,thread_token_usage=Usage(input*10,output*10),usage=Usage(input,output)},second);
    }
    static string Compact(int second) { return Record("compacted",new {message="anonymous fixture compaction"},second); }
    static string ChangedUsage(string text)
    {
        return text.Replace("\"input_tokens\":111,","\"input_tokens\":999,")
            .Replace("\"input_tokens\":1111,","\"input_tokens\":9999,")
            .Replace("\"output_tokens\":11,","\"output_tokens\":99,");
    }
    static string NewLog(out string id,string content)
    {
        id=Guid.NewGuid().ToString();string path=Path.Combine(activeFolder,"fixture-"+id+".jsonl");
        File.WriteAllText(path,content,Utf8);files.Add(path);return path;
    }
    static void Append(string path,string text) { File.AppendAllText(path,text,Utf8); }
    static void Move(string source,string target) { File.Move(source,target);files.Add(target); }
    static void Last(Data data,long input,long output)
    {
        Require(data.lastInput==input && data.lastOutput==output,"expected last="+input+"/"+output+", actual="+data.lastInput+"/"+data.lastOutput);
    }

    static void Main(string[] args)
    {
        if(String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_HOME")) || String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CODEX_LITE_HUD_DATA_HOME")))
            throw new InvalidOperationException("SessionRecoveryChecks requires generated CODEX_HOME and HUD data directories");
        string suffix="reader-recovery-"+Guid.NewGuid().ToString("N");
        activeFolder=Path.Combine(Program.CodexHome,"sessions",suffix);
        archiveFolder=Path.Combine(Program.CodexHome,"archived_sessions",suffix);
        Directory.CreateDirectory(activeFolder);Directory.CreateDirectory(archiveFolder);
        try
        {
            Check("token_count-only last request and cumulative usage",delegate {
                string id;NewLog(out id,Token(Usage(123,45),Usage(1234,450),0));var data=SessionReader.Read(id);
                Last(data,123,45);Require(data.input==1234 && data.output==450 && data.contextUsed==168,"independent total/context usage differs");
            });
            Check("last-only event preserves cumulative usage",delegate {
                string id;string path=NewLog(out id,Token(Usage(123,45),Usage(1234,450),0));SessionReader.Read(id);
                Append(path,Token(Usage(150,50),null,1));var data=SessionReader.Read(id);
                Last(data,150,50);Require(data.input==1234 && data.output==450 && data.contextUsed==200,"last-only event changed cumulative totals");
            });
            Check("total-only event preserves last request",delegate {
                string id;string path=NewLog(out id,Legacy("",123,45,0));Last(SessionReader.Read(id),123,45);
                Append(path,Token(null,Usage(9999,999),1));var data=SessionReader.Read(id);
                Last(data,123,45);Require(data.input==9999 && data.output==999,"total-only event did not update cumulative totals");
            });
            Check("context-only last usage preserves request counts",delegate {
                string id;string path=NewLog(out id,Legacy("",123,45,0));SessionReader.Read(id);
                Append(path,Token(new {total_tokens=999},null,1));var data=SessionReader.Read(id);
                Last(data,123,45);Require(data.contextUsed==999,"context-only last usage was lost");
            });
            Check("newer token_count supersedes legacy last usage",delegate {
                string id;string path=NewLog(out id,Legacy("",100,10,0));SessionReader.Read(id);
                Append(path,Token(Usage(200,20),Usage(2000,200),2));Last(SessionReader.Read(id),200,20);
            });
            Check("older legacy event cannot regress last usage",delegate {
                string id;string path=NewLog(out id,Legacy("",300,30,3));SessionReader.Read(id);
                Append(path,Legacy("",100,10,1));var data=SessionReader.Read(id);
                Last(data,300,30);Require(data.input==3000 && data.contextUsed==330,"older legacy event regressed totals/context");
            });
            Check("older token_count cannot regress last usage",delegate {
                string id;string path=NewLog(out id,Token(Usage(300,30),Usage(3000,300),3));SessionReader.Read(id);
                Append(path,Token(Usage(100,10),Usage(1000,100),1));Last(SessionReader.Read(id),300,30);
            });
            Check("equal-time legacy then token_count preserves request timing",delegate {
                string id;string path=NewLog(out id,Record("response_item",new {role="user"},0)+Legacy("",123,45,2)+Token(Usage(123,45),null,2));
                var data=SessionReader.Read(id);Last(data,123,45);Require(data.requestMs==2000,"duplicate format changed request timing");
            });
            Check("equal-time token_count then legacy fills request timing",delegate {
                string id;NewLog(out id,Record("response_item",new {role="user"},0)+Token(Usage(123,45),null,2)+Legacy("",123,45,2));
                var data=SessionReader.Read(id);Last(data,123,45);Require(data.requestMs==2000,"legacy timing was not retained");
            });
            Check("new token_count request does not reuse old request duration",delegate {
                string id;string path=NewLog(out id,Record("response_item",new {role="user"},0)+Legacy("",123,45,2));SessionReader.Read(id);
                Append(path,Token(Usage(200,20),null,4));var data=SessionReader.Read(id);
                Last(data,200,20);Require(data.requestMs==0 && !data.requestToks.HasValue,"new request reused another request's duration");
            });
            Check("append and unchanged reads do not recount records",delegate {
                string id;string path=NewLog(out id,Compact(0)+Token(Usage(100,10),Usage(1000,100),1));
                Require(SessionReader.Read(id).compactions==1,"initial compaction missing");
                Append(path,Compact(2));Require(SessionReader.Read(id).compactions==2,"appended compaction missing");
                Require(SessionReader.Read(id).compactions==2,"unchanged read recounted compactions");
            });
            Check("archive movement continues the same session",delegate {
                string id;string path=NewLog(out id,Compact(0));SessionReader.Read(id);
                string archived=Path.Combine(archiveFolder,Path.GetFileName(path));Move(path,archived);Append(archived,Compact(1));
                Require(SessionReader.Read(id).compactions==2,"archive movement lost or duplicated history");
            });
            Check("archive round trip preserves a partial line",delegate {
                string id;string path=NewLog(out id,Compact(0));SessionReader.Read(id);string line=Token(Usage(222,22),null,1);
                Append(path,line.Substring(0,line.Length/2));SessionReader.Read(id);
                string archived=Path.Combine(archiveFolder,Path.GetFileName(path));Move(path,archived);SessionReader.Read(id);
                Move(archived,path);Append(path,line.Substring(line.Length/2));var data=SessionReader.Read(id);
                Last(data,222,22);Require(data.compactions==1,"archive round trip duplicated a completed record");
            });
            Check("same-length overwrite resets every session field",delegate {
                string id;string before=Record("turn_context",new {model="old-model"},0)+Token(Usage(111,11),Usage(1111,111),1);
                string path=NewLog(out id,before);SessionReader.Read(id);
                string after=Token(Usage(999,99),Usage(9999,999),2);after=after.PadRight(before.Length-1,' ')+"\n";
                Require(Utf8.GetByteCount(before)==Utf8.GetByteCount(after),"fixture lengths differ");
                File.WriteAllText(path,after,Utf8);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddSeconds(3));var data=SessionReader.Read(id);
                Require(data.input==9999 && String.IsNullOrEmpty(data.model),"same-length overwrite retained stale totals/model");Last(data,999,99);
            });
            Check("larger in-place rewrite resets old cursor",delegate {
                string id;string path=NewLog(out id,Compact(0)+Token(Usage(111,11),Usage(1111,111),1));SessionReader.Read(id);
                File.WriteAllText(path,Token(Usage(99999,99999),Usage(99999,99999),2)+new string(' ',1000)+"\n",Utf8);
                var data=SessionReader.Read(id);Require(data.compactions==0 && data.input==99999,"larger rewrite retained old history");Last(data,99999,99999);
            });
            Check("short truncation resets totals counters and model",delegate {
                string id;string path=NewLog(out id,Record("turn_context",new {model="old-model"},0)+Compact(1)+Token(Usage(111,11),Usage(1111,111),2));SessionReader.Read(id);
                File.WriteAllText(path,Token(Usage(9,1),Usage(9,1),3),Utf8);var data=SessionReader.Read(id);
                Require(data.compactions==0 && data.input==9 && String.IsNullOrEmpty(data.model),"truncation retained an old field");Last(data,9,1);
            });
            Check("replacement identity detects unchanged metadata and checkpoints",delegate {
                string id;string prefix=Record("response_item",new {type="function_call_output",output=new string('p',1024)},0);
                string suffixLine=Record("response_item",new {type="function_call_output",output=new string('s',1024)},2);
                string before=prefix+Token(Usage(111,11),Usage(1111,111),1)+suffixLine;string path=NewLog(out id,before);SessionReader.Read(id);
                var stamp=new FileInfo(path);DateTime created=stamp.CreationTimeUtc,written=stamp.LastWriteTimeUtc;
                string replacement=path+".replacement";files.Add(replacement);File.WriteAllText(replacement,ChangedUsage(before),Utf8);
                File.Delete(path);File.Move(replacement,path);File.SetCreationTimeUtc(path,created);File.SetLastWriteTimeUtc(path,written);
                var data=SessionReader.Read(id);Require(data.input==9999,"replacement retained old totals");Last(data,999,99);
            });
            Check("head checkpoint detects growing rewrite with unchanged old tail",delegate {
                string id;string before=Token(Usage(111,11),Usage(1111,111),0)+Record("response_item",new {type="function_call_output",output=new string('s',1024)},1);
                string path=NewLog(out id,before);SessionReader.Read(id);
                File.WriteAllText(path,ChangedUsage(before)+Compact(2),Utf8);var data=SessionReader.Read(id);
                Require(data.input==9999,"growing rewrite retained old totals despite a changed head");Last(data,999,99);
            });
            Check("tail checkpoint detects growing rewrite with unchanged head",delegate {
                string id;string before=Record("response_item",new {type="function_call_output",output=new string('p',1024)},0)+Token(Usage(111,11),Usage(1111,111),1);
                string path=NewLog(out id,before);SessionReader.Read(id);
                File.WriteAllText(path,ChangedUsage(before)+Compact(2),Utf8);var data=SessionReader.Read(id);
                Require(data.input==9999,"growing rewrite retained old totals despite a changed tail");Last(data,999,99);
            });
            Check("UTF-8 split waits for a complete JSONL record",delegate {
                string id;string path=NewLog(out id,Compact(0));SessionReader.Read(id);
                byte[] line=Utf8.GetBytes(Record("turn_context",new {model="模型测试"},1));byte[] marker=Utf8.GetBytes("模");int split=0;
                for(int i=0;i<line.Length;i++)if(line[i]==marker[0]){split=i+1;break;}
                Require(split>0,"fixture did not contain UTF-8 text");
                using(var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite))stream.Write(line,0,split);
                Require(String.IsNullOrEmpty(SessionReader.Read(id).model),"incomplete record was parsed early");
                using(var stream=new FileStream(path,FileMode.Append,FileAccess.Write,FileShare.ReadWrite))stream.Write(line,split,line.Length-split);
                Require(SessionReader.Read(id).model=="模型测试","UTF-8 continuation was corrupted");
            });
            Check("rewrite discards the old unfinished record",delegate {
                string id;string path=NewLog(out id,Compact(0)+Token(Usage(111,11),null,1).TrimEnd('\n'));SessionReader.Read(id);
                File.WriteAllText(path,Token(Usage(9999,99),Usage(9999,99),2),Utf8);var data=SessionReader.Read(id);
                Require(data.input==9999 && data.compactions==0,"replacement reused an old unfinished record");Last(data,9999,99);
            });
            Check("overlong complete line does not hide the following record",delegate {
                string id;string path=NewLog(out id,new string('x',33)+"\nkept\n");
                var reader=new IncrementalLogReader(0,32,false,null);var lines=new List<string>();bool rejected=false;
                try {reader.Read(path,delegate {},lines.Add);}catch(InvalidDataException){rejected=true;}
                Require(rejected,"overlong record was not reported");reader.Read(path,delegate {},lines.Add);
                Require(lines.Count==1 && lines[0]=="kept","overlong completed line hid the following record");
            });
            Check("overlong continuation skips only to its own newline",delegate {
                string id;string path=NewLog(out id,new string('x',64)+"\nkept\n");
                var reader=new IncrementalLogReader(0,32,false,null,new byte[16]);var lines=new List<string>();bool rejected=false;
                try {reader.Read(path,delegate {},lines.Add);}catch(InvalidDataException){rejected=true;}
                Require(rejected,"overlong record was not reported");reader.Read(path,delegate {},lines.Add);
                Require(lines.Count==1 && lines[0]=="kept","oversized continuation was split or hid the following record");
            });
            Check("session appends stay within an incremental I/O budget",delegate {
                string id;string padding=Record("response_item",new {type="function_call_output",output=new string('x',1024)},0);
                var content=new StringBuilder();for(int i=0;i<2048;i++)content.Append(padding);content.Append(Compact(1));
                string path=NewLog(out id,content.ToString());Require(SessionReader.Read(id).compactions==1,"warm-up record missing");
                using(var process=Process.GetCurrentProcess())
                {
                    IoCounts before,after;Require(GetProcessIoCounters(process.Handle,out before),"I/O counters unavailable");
                    for(int i=0;i<64;i++){Append(path,Compact(i+2));Require(SessionReader.Read(id).compactions==i+2,"append count differs");}
                    Require(GetProcessIoCounters(process.Handle,out after),"I/O counters unavailable");
                    ulong bytes=after.readBytes-before.readBytes;Require(bytes<1048576,"64 small appends read "+bytes+" bytes, budget <1 MiB");
                    report.Add("INFO 64 session appends: "+bytes+" read bytes after a >2 MiB warm-up log");
                }
            });
            Check("cold-start quota reads sixteen distinct recent logs",delegate {
                string folder=Path.Combine(Program.CodexHome,"sessions",DateTime.Now.ToString("yyyy\\MM\\dd"));Directory.CreateDirectory(folder);
                for(int i=0;i<16;i++)
                {
                    string path=Path.Combine(folder,suffix+"-quota-"+i+".jsonl");files.Add(path);
                    string line=i==15?Record("event_msg",new {type="token_count",rate_limits=new {limit_id="codex",primary=new {used_percent=41,window_minutes=300}}},100):Record("response_item",new {type="function_call_output",output="tool result"},0);
                    File.WriteAllText(path,line,Utf8);File.SetLastWriteTimeUtc(path,DateTime.UtcNow.AddHours(1).AddSeconds(-i));
                }
                QuotaLogs.Refresh();var quota=QuotaReader.Current();
                Require(quota!=null && quota.windows.Length==1 && quota.windows[0].remaining==59,"sixteenth distinct recent log's quota was omitted");
            });
        }
        finally
        {
            // Delete only explicitly created files; fixture directories can stay
            // in the isolated home for diagnostics and are never user folders.
            foreach(string path in files)try {if(File.Exists(path))File.Delete(path);} catch { }
            report.Add("Session recovery: "+passed+" passed, "+failed+" failed, 0 skipped.");
            File.WriteAllLines(args[0],report);Environment.ExitCode=failed==0?0:1;
        }
    }
}
