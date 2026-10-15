using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

static class DetectionChecks
{
    static void Expect(bool value,string message) { if(!value) throw new Exception(message); }
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            var active=NativeThreadCatalog.ActiveIds();
            Expect(active!=null && active.Count>=2,"Need two active chats for native metadata checks");
            var ids=active.Take(2).ToArray();
            bool ambiguous;
            var catalog=new List<SessionEntry> {new SessionEntry {id=ids[0],title="unique-fixture"}};
            Expect(WindowSession.ResolveName("unique-fixture",catalog,out ambiguous)==ids[0] && !ambiguous,"Unique title did not resolve");
            var stale=new List<SessionEntry> {new SessionEntry {id=ids[0],title="stale-fixture"},new SessionEntry {id=Guid.Empty.ToString(),title="stale-fixture"}};
            Expect(WindowSession.ResolveName("stale-fixture",stale,out ambiguous)==ids[0] && !ambiguous,"Inactive namesake was not excluded");
            var duplicate=new List<SessionEntry> {
                new SessionEntry { id=ids[0],title="duplicate-fixture" },
                new SessionEntry { id=ids[1],title="duplicate-fixture" }
            };
            Expect(WindowSession.ResolveName("duplicate-fixture",duplicate,out ambiguous)==null && ambiguous,"Two live namesakes must not be guessed");
            Expect(WindowSession.ResolveName("unknown-fixture",catalog,out ambiguous)==null && !ambiguous,"Unknown pages must not reuse a previous chat");
            File.WriteAllText(args[0],"4 session-selection checks passed; native metadata opened read-only.");
        }
        catch(Exception ex) { File.WriteAllText(args[0],ex.ToString()); Environment.ExitCode=1; }
    }
}
