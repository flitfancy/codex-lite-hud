using System;
using System.Collections.Generic;
using System.IO;

static class SessionTimingChecks
{
    static void Require(bool value,string message){if(!value)throw new Exception(message);}
    static long Unix(DateTimeOffset at){return (long)(at-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalSeconds;}
    static Dictionary<string,object> Event(string kind,string id){return new Dictionary<string,object>{{"type",kind},{"turn_id",id}};}
    static void Main(string[] args)
    {
        try
        {
            var t=new DateTimeOffset(2026,10,15,0,0,0,TimeSpan.Zero);var clock=new SessionTiming();
            Require(clock.Running(t)==null && clock.Span(t)==null,"Absent timing must be unknown");
            clock.Observe(t);var start=Event("task_started","a");start["started_at"]=Unix(t.AddSeconds(1));clock.Event(start,t.AddSeconds(3));
            Require(clock.Running(t.AddSeconds(11))==10000,"Unix start time must precede persistence delay");
            var end=Event("task_complete","a");end["duration_ms"]=9000L;end["completed_at"]=Unix(t.AddSeconds(20));clock.Event(end,t.AddSeconds(21));clock.Event(end,t.AddSeconds(22));
            Require(clock.Running(t.AddSeconds(50))==9000 && clock.Span(t.AddSeconds(50))==50000,"Completed duration must freeze, deduplicate and exclude idle gaps");
            var second=Event("task_started","b");second["started_at"]=Unix(t.AddSeconds(25))*1000L;clock.Event(second,t.AddSeconds(26));clock.Event(second,t.AddSeconds(27));
            Require(clock.Running(t.AddSeconds(30))==14000,"Unix milliseconds and duplicate starts differ");
            clock.Event(Event("turn_aborted","b"),t.AddSeconds(32));
            Require(clock.Running(t.AddSeconds(100))==16000,"Aborted turn must stop at recorded end");
            clock.Reset();Require(clock.Running(t)==null && clock.Span(t)==null,"Reset did not clear timing");
            clock.Observe(t.AddSeconds(2));Require(clock.Span(t)==0,"Future clock must not yield negative span");
            var left=new Data{runningMs=1000,spanMs=2000};var right=new Data{runningMs=2000,spanMs=3000};
            Require(left.SameDisplay(right),"Live clock tick must not repaint all unchanged readings");
            File.WriteAllText(args[0],"PASS missing data, Unix seconds/milliseconds, persistence lag, duration priority, duplicate events, live turns, idle gaps, aborts, reset and independent clock repaint.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
