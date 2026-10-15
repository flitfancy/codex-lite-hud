using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

static class FixtureBuilder
{
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_exec(IntPtr db,byte[] sql,IntPtr callback,IntPtr state,IntPtr error);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr db);
    static void Main(string[] args)
    {
        string home=args[0];Directory.CreateDirectory(home);Directory.CreateDirectory(Path.Combine(home,"sessions"));
        var json=new JavaScriptSerializer();var now=DateTimeOffset.UtcNow;
        string one="00000000-0000-7000-8000-000000000001",two="00000000-0000-7000-8000-000000000002";
        File.WriteAllText(Path.Combine(home,"session_index.jsonl"),json.Serialize(new{id=one,thread_name="查看 flitrealize 架构与测试文件"})+"\n"+json.Serialize(new{id=two,thread_name="Another fixture"})+"\n");
        string lines=json.Serialize(new{timestamp=now.AddSeconds(-3).ToString("o"),type="turn_context",payload=new{model="fixture"}})+"\n"
            +json.Serialize(new{timestamp=now.AddSeconds(-2).ToString("o"),type="response_item",payload=new{role="user"}})+"\n"
            +json.Serialize(new{timestamp=now.ToString("o"),type="token_usage_record",payload=new{thread_id=one,thread_token_usage=new{input_tokens=53000000,output_tokens=200000,cached_input_tokens=50000000},usage=new{input_tokens=200000,output_tokens=1200,total_tokens=201200}}})+"\n"
            +json.Serialize(new{timestamp=now.ToString("o"),type="event_msg",payload=new{type="token_count",info=new{model_context_window=828400,last_token_usage=new{total_tokens=201200}},rate_limits=new{limit_id="codex",primary=new{used_percent=74,window_minutes=10080,resets_at=(long)(now.AddDays(7)-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalSeconds}}}})+"\n";
        lines=json.Serialize(new{timestamp=now.AddSeconds(-4).ToString("o"),type="event_msg",payload=new{type="task_started",turn_id="fixture-turn"}})+"\n"+lines
            +json.Serialize(new{timestamp=now.ToString("o"),type="event_msg",payload=new{type="task_complete",turn_id="fixture-turn",duration_ms=4000}})+"\n"
            +json.Serialize(new{timestamp=now.ToString("o"),type="compacted",payload=new{message="fixture summary",window_id="fixture-window",compaction_response_id="fixture-compaction"}})+"\n";
        File.WriteAllText(Path.Combine(home,"sessions","fixture-"+one+".jsonl"),lines,new UTF8Encoding(false));
        IntPtr db;
        if(sqlite3_open_v2(Encoding.UTF8.GetBytes(Path.Combine(home,"state_12.sqlite")+"\0"),out db,6,IntPtr.Zero)!=0)throw new Exception("Fixture database creation failed");
        try
        {
            string sql="CREATE TABLE threads(id TEXT PRIMARY KEY, archived INTEGER NOT NULL);INSERT INTO threads VALUES('"+one+"',0),('"+two+"',0);";
            if(sqlite3_exec(db,Encoding.UTF8.GetBytes(sql+"\0"),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)!=0)throw new Exception("Fixture database setup failed");
        }
        finally{sqlite3_close(db);}
    }
}
