using System;
using System.Collections.Generic;
using System.Globalization;

sealed class SessionTiming
{
    sealed class Turn {internal DateTimeOffset start,end;internal bool terminal;internal long? duration;}
    readonly Dictionary<string,Turn> turns=new Dictionary<string,Turn>();
    internal DateTimeOffset FirstAt;
    internal void Observe(DateTimeOffset at){if(FirstAt==DateTimeOffset.MinValue || at<FirstAt)FirstAt=at;}
    static DateTimeOffset At(object value,DateTimeOffset fallback)
    {
        string text=SessionReader.Text(value);long unix;
        if(Int64.TryParse(text,NumberStyles.Integer,CultureInfo.InvariantCulture,out unix))
        {
            try{var epoch=new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero);return Math.Abs((double)unix)>=1000000000000d?epoch.AddMilliseconds(unix):epoch.AddSeconds(unix);}catch(ArgumentOutOfRangeException){return fallback;}
        }
        DateTimeOffset parsed;return DateTimeOffset.TryParse(text,out parsed)?parsed:fallback;
    }
    internal void Event(Dictionary<string,object> payload,DateTimeOffset at)
    {
        string kind=SessionReader.Text(SessionReader.At(payload,"type")),id=SessionReader.Text(SessionReader.At(payload,"turn_id"));
        if(id=="" || (kind!="task_started" && kind!="task_complete" && kind!="turn_aborted"))return;
        Turn turn;if(!turns.TryGetValue(id,out turn)){turn=new Turn();turns.Add(id,turn);}
        if(kind=="task_started")
        {
            var start=At(SessionReader.At(payload,"started_at"),at);
            if(turn.start==DateTimeOffset.MinValue || start<turn.start)turn.start=start;
            return;
        }
        turn.terminal=true;turn.end=At(SessionReader.At(payload,"completed_at"),at);
        var explicitStart=SessionReader.At(payload,"started_at");
        if(turn.start==DateTimeOffset.MinValue && explicitStart!=null)turn.start=At(explicitStart,DateTimeOffset.MinValue);
        double milliseconds;
        if(Double.TryParse(SessionReader.Text(SessionReader.At(payload,"duration_ms")),NumberStyles.Float,CultureInfo.InvariantCulture,out milliseconds) && !Double.IsNaN(milliseconds) && !Double.IsInfinity(milliseconds) && milliseconds>=0 && milliseconds<=Int64.MaxValue)
            turn.duration=(long)Math.Round(milliseconds);
    }
    internal long? Running(DateTimeOffset now)
    {
        long total=0;bool known=false;
        foreach(var turn in turns.Values)
        {
            if(turn.duration.HasValue){total+=turn.duration.Value;known=true;}
            else if(turn.start!=DateTimeOffset.MinValue){total+=(long)Math.Max(0,((turn.terminal?turn.end:now)-turn.start).TotalMilliseconds);known=true;}
        }
        return known?(long?)total:null;
    }
    internal long? Span(DateTimeOffset now){return FirstAt==DateTimeOffset.MinValue?(long?)null:(long)Math.Max(0,(now-FirstAt).TotalMilliseconds);}
    internal void Reset(){turns.Clear();FirstAt=DateTimeOffset.MinValue;}
}
