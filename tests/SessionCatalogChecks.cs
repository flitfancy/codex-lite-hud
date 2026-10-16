using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

// Real SQLite + rollout fixtures at the same catalog/selection seam used by
// the UIA resolver. No real Codex configuration or windows are touched.
static class SessionCatalogChecks
{
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] path,out IntPtr db,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_exec(IntPtr db,byte[] sql,IntPtr callback,IntPtr state,IntPtr error);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr db);
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Sql(IntPtr db,string sql){if(sqlite3_exec(db,Encoding.UTF8.GetBytes(sql+"\0"),IntPtr.Zero,IntPtr.Zero,IntPtr.Zero)!=0)throw new Exception("Fixture SQL failed");}
    [STAThread]
    static void Main(string[] args)
    {
        IntPtr db=IntPtr.Zero;
        string id="00000000-0000-7000-8000-000000000003",other="00000000-0000-7000-8000-000000000004";
        string path=Path.Combine(Program.CodexHome,"sessions","native-"+id+".jsonl");
        try
        {
            string database=AppPaths.FindStateDatabase(Program.CodexHome);
            Require(sqlite3_open_v2(Encoding.UTF8.GetBytes(database+"\0"),out db,6,IntPtr.Zero)==0,"Fixture database open failed");
            var legacy=SessionReader.Recent(Int32.MaxValue);
            Require(legacy.Any(x=>x.id=="00000000-0000-7000-8000-000000000001" && x.title=="查看 flitrealize 架构与测试文件") && NativeThreadCatalog.ActiveIds().Count==2,"Legacy ID-only database did not retain index titles");
            Sql(db,"ALTER TABLE threads ADD COLUMN title TEXT;ALTER TABLE threads ADD COLUMN name TEXT;ALTER TABLE threads ADD COLUMN updated_at INTEGER;PRAGMA journal_mode=WAL;");
            string title="用其他的格式发给我 这个看不了";
            Sql(db,"INSERT INTO threads(id,archived,title,updated_at) VALUES('"+id+"',0,'"+title+"',100);");
            File.WriteAllText(path,Program.Json.Serialize(new{timestamp=DateTimeOffset.UtcNow.ToString("o"),type="event_msg",payload=new{type="token_count",info=new{total_token_usage=new{input_tokens=1234,output_tokens=56}}}})+"\n");
            bool ambiguous;
            var catalog=SessionReader.Recent(Int32.MaxValue);
            Require(WindowSession.ResolveName(title,catalog,out ambiguous)==id && !ambiguous,"Selected local chat missing from session_index.jsonl stays unrecognized");
            var data=SessionReader.Read(id);
            Require(data.sessionTitle==title && data.input==1234 && data.output==56,"Resolved database-only chat lost title or real rollout readings");
            Require(SessionReader.Recent(1).Count==1 && SessionReader.Recent(-1).Count==0,"Merged catalog lost bounds");
            var changed=SessionReader.Recent(Int32.MaxValue);changed[0].title="mutated";changed.Clear();
            Require(SessionReader.Recent(Int32.MaxValue).Any(x=>x.id==id && x.title==title),"Returned catalog mutated cached metadata");
            string renamed="重命名后的会话 · 中文 UTF-8";
            Sql(db,"UPDATE threads SET name='"+renamed+"',updated_at=101 WHERE id='"+id+"';");
            catalog=SessionReader.Recent(Int32.MaxValue);
            Require(WindowSession.ResolveName(renamed,catalog,out ambiguous)==id && !ambiguous,"WAL rename was not picked up or UTF-8 title was corrupted");
            Require(SessionReader.Read(id).sessionTitle==renamed,"Displayed title ignored the current database name");
            Sql(db,"INSERT INTO threads(id,archived,title,name,updated_at) VALUES('"+other+"',0,'fixture','"+renamed+"',102);");
            catalog=SessionReader.Recent(Int32.MaxValue);
            Require(WindowSession.ResolveName(renamed,catalog,out ambiguous)==null && ambiguous,"Two live database namesakes were guessed");
            Sql(db,"UPDATE threads SET archived=1 WHERE id='"+other+"';");
            Require(WindowSession.ResolveName(renamed,SessionReader.Recent(Int32.MaxValue),out ambiguous)==id && !ambiguous,"Archived namesake prevented reliable selection");
            Require(WindowSession.ResolveName("unknown-fixture",SessionReader.Recent(Int32.MaxValue),out ambiguous)==null && !ambiguous,"Unknown page reused a previous chat");
            File.WriteAllText(args[0],"PASS legacy ID-only schema fallback, database-only local chat, UTF-8 title, real rollout readings, merged bounds, defensive copies, live WAL rename, live duplicate guard, archived namesake and unknown-page guard.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
        finally
        {
            if(db!=IntPtr.Zero){Sql(db,"DELETE FROM threads WHERE id IN ('"+id+"','"+other+"');");sqlite3_close(db);}
            File.Delete(path);
        }
    }
}
