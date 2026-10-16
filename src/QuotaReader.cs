using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

sealed class QuotaWindow
{
    public string name,resetAt;
    public double remaining;
    public long windowMinutes;
}
sealed class QuotaSnapshot
{
    public string readAt,source,planType;
    public QuotaWindow[] windows;
}
static class QuotaReader
{
    static readonly object Gate=new object();
    static QuotaSnapshot cached;
    static DateTimeOffset cachedAt,expiresAt=DateTimeOffset.MaxValue;

    // Rollouts use snake_case; protocol snapshots use camelCase. Explicit null
    // in the native field stays absent rather than reviving an alias value.
    static object Field(Dictionary<string,object> value,string native,string protocol)
    {
        object result;
        return value!=null && value.TryGetValue(native,out result) ? result : SessionReader.At(value,protocol);
    }
    static bool OptionalInteger(object value,out long number)
    {
        number=0;
        return value==null || Int64.TryParse(SessionReader.Text(value),NumberStyles.Integer,CultureInfo.InvariantCulture,out number);
    }
    internal static QuotaSnapshot FromSession(Dictionary<string,object> limits,DateTimeOffset at)
    {
        if(limits==null)return null;
        string id=SessionReader.Text(Field(limits,"limit_id","limitId"));
        if(id!="" && id!="codex")return null;
        var windows=new List<QuotaWindow>();
        foreach(string key in new[]{"primary","secondary"})
        {
            var value=SessionReader.Map(SessionReader.At(limits,key));
            var used=Field(value,"used_percent","usedPercent");
            if(used==null)continue;
            double number;
            if(!Double.TryParse(SessionReader.Text(used),NumberStyles.Float,CultureInfo.InvariantCulture,out number) || Double.IsNaN(number) || Double.IsInfinity(number))continue;
            long minutes,reset;
            // A malformed optional value invalidates this window, never the
            // other window or the conversation's independent token readings.
            if(!OptionalInteger(Field(value,"window_minutes","windowDurationMins"),out minutes) || minutes<0)continue;
            if(!OptionalInteger(Field(value,"resets_at","resetsAt"),out reset) || reset<0 || reset>253402300799L)continue;
            string name=minutes>=1440 ? (minutes/1440d).ToString("0.#",CultureInfo.InvariantCulture)+" 天" : minutes>=60 ? (minutes/60d).ToString("0.#",CultureInfo.InvariantCulture)+" 小时" : minutes>0 ? minutes+" 分钟" : "额度窗口";
            windows.Add(new QuotaWindow { name=name,windowMinutes=minutes,remaining=Math.Max(0,Math.Min(100,100-number)),resetAt=reset>0 ? new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(reset).ToString("o") : null });
        }
        // Preserve the reported plan for diagnostics; it never selects a
        // guessed quota, duration or supported-plan allowlist.
        return windows.Count==0 ? null : new QuotaSnapshot { windows=windows.ToArray(),readAt=at.ToString("o"),source="本机会话记录",planType=Field(limits,"plan_type","planType") as string };
    }
    static DateTimeOffset Timestamp(QuotaSnapshot snapshot)
    {
        DateTimeOffset at;
        return snapshot!=null && DateTimeOffset.TryParse(snapshot.readAt,out at) ? at : DateTimeOffset.MinValue;
    }
    static bool Active(QuotaWindow window)
    {
        DateTimeOffset reset;
        return !DateTimeOffset.TryParse(window.resetAt,out reset) || reset>DateTimeOffset.Now;
    }
    internal static QuotaSnapshot Current(QuotaSnapshot session=null)
    {
        lock(Gate)
        {
            if(session!=null && !Object.ReferenceEquals(session,cached))
            {
                var at=Timestamp(session);
                if(at>cachedAt){cached=session;cachedAt=at;expiresAt=NextReset(session);}
            }
            if(cached==null)return null;
            if(DateTimeOffset.Now<expiresAt)return cached;
            var valid=cached.windows.Where(Active).ToArray();
            cached=valid.Length==0 ? null : new QuotaSnapshot { windows=valid,readAt=cached.readAt,source=cached.source,planType=cached.planType };
            expiresAt=NextReset(cached);
            return cached;
        }
    }
    static DateTimeOffset NextReset(QuotaSnapshot snapshot)
    {
        var next=DateTimeOffset.MaxValue;
        if(snapshot!=null)foreach(var window in snapshot.windows)
        {
            DateTimeOffset reset;
            if(DateTimeOffset.TryParse(window.resetAt,out reset) && reset<next)next=reset;
        }
        return next;
    }
    internal static void Apply(Data data) { data.accountQuota=Current(data.accountQuota); }
}
