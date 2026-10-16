using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

// Codex may persist current chat titles here without updating session_index.
// Read only identity/title/archive metadata; never read messages or credentials.
static class NativeThreadCatalog
{
    static readonly object Gate=new object();
    static DateTime readAt;
    static string stamp;
    static HashSet<string> active;
    static List<SessionEntry> rows=new List<SessionEntry>();
    static Dictionary<string,string> titles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_open_v2(byte[] filename,out IntPtr database,int flags,IntPtr vfs);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_prepare_v2(IntPtr database,byte[] sql,int bytes,out IntPtr statement,IntPtr tail);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_step(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern IntPtr sqlite3_column_text(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_column_bytes(IntPtr statement,int column);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_finalize(IntPtr statement);
    [DllImport("winsqlite3.dll",CallingConvention=CallingConvention.Cdecl)]static extern int sqlite3_close(IntPtr database);
    static string Text(IntPtr statement,int column)
    {
        IntPtr pointer=sqlite3_column_text(statement,column);int length=sqlite3_column_bytes(statement,column);
        if(pointer==IntPtr.Zero || length==0)return "";
        var bytes=new byte[length];Marshal.Copy(pointer,bytes,0,length);return Encoding.UTF8.GetString(bytes);
    }
    static IntPtr Prepare(IntPtr database,string sql)
    {
        IntPtr statement;byte[] bytes=Encoding.UTF8.GetBytes(sql+"\0");
        if(sqlite3_prepare_v2(database,bytes,bytes.Length,out statement,IntPtr.Zero)!=0)
        {if(statement!=IntPtr.Zero)sqlite3_finalize(statement);throw new InvalidDataException("会话元数据结构不兼容");}
        return statement;
    }
    static string Stamp(string path)
    {
        try{var file=new FileInfo(path);return file.Exists?path+"|"+file.Length+"|"+file.LastWriteTimeUtc.Ticks:"";}catch{return "";}
    }
    static void Load()
    {
        string path;try{path=AppPaths.FindStateDatabase(Program.CodexHome);}catch{path=null;}
        string current=Stamp(path)+(path==null?"":Stamp(path+"-wal"));
        if(current==stamp && DateTime.UtcNow-readAt<TimeSpan.FromSeconds(5))return;
        stamp=current;readAt=DateTime.UtcNow;
        IntPtr database=IntPtr.Zero,statement=IntPtr.Zero;
        try
        {
            if(!File.Exists(path) || sqlite3_open_v2(Encoding.UTF8.GetBytes(path+"\0"),out database,1,IntPtr.Zero)!=0)throw new IOException("没有可读取的会话元数据");
            var columns=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            statement=Prepare(database,"PRAGMA table_info(threads)");int status;
            while((status=sqlite3_step(statement))==100)columns.Add(Text(statement,1));
            if(status!=101 || !columns.Contains("id") || !columns.Contains("archived"))throw new InvalidDataException("会话元数据结构不兼容");
            sqlite3_finalize(statement);statement=IntPtr.Zero;
            string title=columns.Contains("title")?"title":"NULL";
            if(columns.Contains("name"))title="COALESCE(NULLIF(name,''),"+title+")";
            string order=columns.Contains("updated_at_ms")?" ORDER BY updated_at_ms DESC":columns.Contains("updated_at")?" ORDER BY updated_at DESC":"";
            statement=Prepare(database,"SELECT id,"+title+" FROM threads WHERE archived=0"+order);
            var nextActive=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var nextRows=new List<SessionEntry>();var nextTitles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
            while((status=sqlite3_step(statement))==100)
            {
                string id=Text(statement,0),name=Text(statement,1);Guid parsed;
                if(!Guid.TryParse(id,out parsed))continue;
                nextActive.Add(id);nextRows.Add(new SessionEntry{id=id,title=name});nextTitles[id]=name;
            }
            if(status!=101)throw new IOException("会话元数据读取未完成");
            active=nextActive;rows=nextRows;titles=nextTitles;
        }
        catch{active=null;rows=new List<SessionEntry>();titles=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);}
        finally{if(statement!=IntPtr.Zero)sqlite3_finalize(statement);if(database!=IntPtr.Zero)sqlite3_close(database);}
    }
    internal static HashSet<string> ActiveIds()
    {
        lock(Gate){Load();return active==null?null:new HashSet<string>(active,StringComparer.OrdinalIgnoreCase);}
    }
    internal static List<SessionEntry> Recent()
    {
        lock(Gate){Load();var result=new List<SessionEntry>();foreach(var row in rows)result.Add(new SessionEntry{id=row.id,title=row.title});return result;}
    }
    internal static string Title(string id)
    {
        lock(Gate){Load();string title;return titles.TryGetValue(id,out title)?title:null;}
    }
}
