using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

sealed class QuotaWindow
{
    public string name,resetAt;
    public double remaining;
}
sealed class QuotaSnapshot
{
    public string readAt,source;
    public QuotaWindow[] windows;
}
static class QuotaReader
{
    static readonly object Gate=new object();
    static QuotaSnapshot cached;
    static DateTimeOffset cachedAt,expiresAt=DateTimeOffset.MaxValue;

    internal static QuotaSnapshot FromSession(Dictionary<string,object> limits,DateTimeOffset at)
    {
        if(limits==null)return null;
        string id=SessionReader.Text(SessionReader.At(limits,"limit_id"));
        if(id!="" && id!="codex")return null;
        var windows=new List<QuotaWindow>();
        foreach(string key in new[]{"primary","secondary"})
        {
            var value=SessionReader.Map(SessionReader.At(limits,key));
            var used=SessionReader.At(value,"used_percent");
            if(used==null)continue;
            double number;
            if(!Double.TryParse(SessionReader.Text(used),NumberStyles.Float,CultureInfo.InvariantCulture,out number) || Double.IsNaN(number) || Double.IsInfinity(number))continue;
            long minutes=SessionReader.Count(SessionReader.At(value,"window_minutes"));
            long reset=SessionReader.Count(SessionReader.At(value,"resets_at"));
            string name=minutes>=1440 ? (minutes/1440d).ToString("0.#",CultureInfo.InvariantCulture)+" 天" : minutes>=60 ? (minutes/60d).ToString("0.#",CultureInfo.InvariantCulture)+" 小时" : minutes>0 ? minutes+" 分钟" : "额度窗口";
            windows.Add(new QuotaWindow { name=name,remaining=Math.Max(0,Math.Min(100,100-number)),resetAt=reset>0 ? new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(reset).ToString("o") : null });
        }
        return windows.Count==0 ? null : new QuotaSnapshot { windows=windows.ToArray(),readAt=at.ToString("o"),source="本机会话记录" };
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
            cached=valid.Length==0 ? null : new QuotaSnapshot { windows=valid,readAt=cached.readAt,source=cached.source };
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
    internal static QuotaWindow Limiting(QuotaSnapshot snapshot)
    {
        QuotaWindow limiting=null;
        if(snapshot!=null)foreach(var window in snapshot.windows)if(limiting==null || window.remaining<limiting.remaining)limiting=window;
        return limiting;
    }
    internal static void Apply(Data data) { data.accountQuota=Current(data.accountQuota); }
}
