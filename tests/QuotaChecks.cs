using System;
using System.IO;
using System.Web.Script.Serialization;

static class QuotaChecks
{
    static void Require(bool value,string text) { if(!value)throw new Exception(text); }
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            var parser=new JavaScriptSerializer();
            var now=DateTimeOffset.Now;
            long reset=(long)(now.AddHours(5)-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalSeconds;
            var native=QuotaReader.FromSession(SessionReader.Map(parser.DeserializeObject("{\"limit_id\":\"codex\",\"primary\":{\"used_percent\":40,\"window_minutes\":300,\"resets_at\":"+reset+"},\"secondary\":{\"used_percent\":75,\"window_minutes\":10080}}")),now);
            Require(native!=null && native.windows.Length==2,"Native primary and secondary windows must both parse");
            Require(native.windows[0].name=="5 小时" && native.windows[0].remaining==60 && native.windows[1].name=="7 天" && native.windows[1].remaining==25,"Native window duration or remaining percent is incorrect");
            DateTimeOffset parsedReset;
            Require(DateTimeOffset.TryParse(native.windows[0].resetAt,out parsedReset) && Math.Abs((parsedReset-now.AddHours(5)).TotalSeconds)<1,"Native reset timestamp is incorrect");
            var missing=QuotaReader.FromSession(SessionReader.Map(parser.DeserializeObject("{\"limit_id\":\"codex\",\"primary\":{\"used_percent\":10,\"window_minutes\":300},\"secondary\":null}")),now.AddMinutes(-1));
            Require(missing!=null && missing.windows.Length==1 && missing.windows[0].remaining==90,"Missing secondary window must not become zero");
            var current=QuotaReader.Current(native);
            Require(current.windows.Length==2 && current.windows[0].remaining==60 && current.windows[1].remaining==25,"Current quota collapsed one of the recorded windows");
            Require(Object.ReferenceEquals(QuotaReader.Current(missing),current),"Older snapshot overwrote newer quota");
            var newer=QuotaReader.FromSession(SessionReader.Map(parser.DeserializeObject("{\"primary\":{\"used_percent\":80,\"window_minutes\":300}}")),now.AddMinutes(1));
            Require(QuotaReader.Current(newer).windows[0].remaining==20,"Newer native snapshot did not replace older quota");
            Require(QuotaReader.FromSession(SessionReader.Map(parser.DeserializeObject("{\"limit_id\":\"other-provider\",\"primary\":{\"used_percent\":90}}")),now)==null,"Non-Codex limit must not be accepted");
            var expired=new QuotaSnapshot { readAt=now.AddMinutes(2).ToString("o"),source="test",windows=new[]{new QuotaWindow {name="test",remaining=2,resetAt=now.AddSeconds(-1).ToString("o")} } };
            Require(QuotaReader.Current(expired)==null,"Expired quota must not be shown as current");
            File.WriteAllText(args[0],"PASS native primary and secondary quota, missing window, independent windows, reset timestamp, snapshot freshness, limit identity, reset expiry.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
