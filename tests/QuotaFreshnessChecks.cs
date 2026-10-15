using System;
using System.Diagnostics;
using System.IO;

static class QuotaFreshnessChecks
{
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            string selected="00000000-0000-7000-8000-000000000001";
            string oldFolder=Path.Combine(Program.CodexHome,"sessions","2000","01","01");Directory.CreateDirectory(oldFolder);
            string oldChat=Path.Combine(oldFolder,"fixture-old-chat.jsonl");
            File.WriteAllText(oldChat,Program.Json.Serialize(new {timestamp=DateTimeOffset.UtcNow.AddMilliseconds(500).ToString("o"),type="event_msg",payload=new {type="token_count",rate_limits=new {limit_id="codex",primary=new {used_percent=80,window_minutes=10080}}}})+"\n");
            var initial=Reader.Read(selected).GetAwaiter().GetResult();
            if(QuotaReader.Limiting(initial.accountQuota).remaining!=20)throw new Exception("Startup omitted a recently changed chat in an old date folder");
            string path=Path.Combine(Program.CodexHome,"sessions","fixture-background.jsonl");
            var watch=Stopwatch.StartNew();
            File.WriteAllText(path,Program.Json.Serialize(new {timestamp=DateTimeOffset.UtcNow.AddSeconds(1).ToString("o"),type="event_msg",payload=new {type="token_count",rate_limits=new {limit_id="codex",primary=new {used_percent=89,window_minutes=10080}}}})+"\n");
            QuotaWindow window=null;
            var deadline=DateTime.UtcNow.AddSeconds(2);
            do
            {
                System.Threading.Thread.Sleep(50);
                window=QuotaReader.Limiting(Reader.Read(selected).GetAwaiter().GetResult().accountQuota);
            }while((window==null || window.remaining!=11) && DateTime.UtcNow<deadline);
            if(window==null || window.remaining!=11)throw new Exception("Selected session hides newer account quota from another session; expected 11%, actual "+(window==null?"missing":window.remaining.ToString()));
            File.Delete(path);
            File.Delete(oldChat);
            File.WriteAllText(args[0],"PASS startup quota in an old date folder; background session quota reaches selected session without switching chats in "+watch.Elapsed.TotalMilliseconds.ToString("0.0")+" ms (50 ms polling fixture).");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
