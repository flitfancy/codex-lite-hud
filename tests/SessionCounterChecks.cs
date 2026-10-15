using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

static class SessionCounterChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static string Record(string type,object payload){return Program.Json.Serialize(new{timestamp=DateTimeOffset.UtcNow.ToString("o"),type=type,payload=payload});}
    static string Event(string type,string id){return Record("event_msg",new{type=type,turn_id=id});}
    static string Compact(string window,string response){return Record("compacted",new{message="summary",replacement_history=new[]{new{type="event_msg",payload=new{type="task_started",turn_id="nested"}}},window_id=window,compaction_response_id=response});}
    static Dictionary<string,object> Payload(string kind,string id){return new Dictionary<string,object>{{"type",kind},{"turn_id",id}};}
    static void Main(string[] args)
    {
        try
        {
            var counters=new SessionCounters();Require(counters.Turns==null && counters.Compactions==0,"Missing events must remain unknown turns and zero observed compactions");
            counters.Event(Payload("task_started","a"));counters.Event(Payload("task_started","a"));counters.Event(Payload("task_complete","a"));counters.Event(Payload("task_complete","a"));
            counters.Event(Payload("task_started","b"));counters.Event(Payload("turn_aborted","b"));
            Require(counters.Turns==2 && counters.Completed==1 && counters.Aborted==1 && counters.Active==0,"Turn IDs and terminal events must deduplicate");
            counters.Compacted(Compact("window-1","response-1"));counters.Compacted(Compact("window-retry","response-1"));
            Require(counters.Compactions==1 && counters.Turns==2,"Compaction IDs deduplicate; embedded history is not a turn");
            counters.Reset();Require(counters.Turns==null && counters.Compactions==0,"Reset must clear all counters");

            string id="00000000-0000-7000-8000-000000000004";
            string file=Path.Combine(Program.CodexHome,"sessions","counter-"+id+".jsonl");
            File.WriteAllText(file,Event("task_started","one")+"\n"+Event("task_started","one")+"\n"+Event("task_complete","one")+"\n",new UTF8Encoding(false));
            var first=SessionReader.Read(id);Require(first.turns==1 && first.completedTurns==1 && first.activeTurns==0,"Initial persisted turns differ");
            string partial=Event("task_started","two");File.AppendAllText(file,partial);
            Require(SessionReader.Read(id).turns==1,"Incomplete line must not count early");
            File.AppendAllText(file,"\n"+Record("response_item",new{role="user",content="in-turn steering"})+"\n"+Record("response_item",new{type="function_call"})+"\n"+Compact("window-1","response-1")+"\n"+Compact("window-retry","response-1")+"\n");
            var second=SessionReader.Read(id);Require(second.turns==2 && second.completedTurns==1 && second.activeTurns==1 && second.compactions==1,"Incremental, steering/tool exclusion or compaction history count differs");
            Require(SessionReader.Read(id).turns==2 && SessionReader.Read(id).compactions==1,"Unchanged reads must not recount");
            File.WriteAllText(file,Event("task_started","new")+"\n",new UTF8Encoding(false));
            var reset=SessionReader.Read(id);Require(reset.turns==1 && reset.compactions==0 && reset.completedTurns==0,"Truncation must reset persisted counters");
            var left=new Data{turns=1};var right=new Data{turns=2};Require(!left.SameDisplay(right),"Turn change must trigger display update");
            right.turns=1;right.compactions=1;Require(!left.SameDisplay(right),"Compaction change must trigger display update");
            File.WriteAllText(args[0],"PASS turn/completion/abort IDs, unknown turns, compaction deduplication, embedded-history exclusion, incremental append, partial lines, steering/tool exclusion, repeated reads, truncation and display changes.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
