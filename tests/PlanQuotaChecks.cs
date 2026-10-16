using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

// Generated local records, not captures from live Free/Plus accounts. Exercise
// both session parsing and the background tail reader through the HUD label.
static class PlanQuotaChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Case
    {
        internal string name,json,plan;
        internal double? remaining;
        internal int windows;
        internal Case(string n,string j,string p,double? r,int w){name=n;json=j;plan=p;remaining=r;windows=w;}
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    static void Snapshot(QuotaSnapshot snapshot,Case item,string path)
    {
        if(!item.remaining.HasValue){Require(snapshot==null,path+": missing quota must stay unknown");return;}
        Require(snapshot!=null,path+": quota was dropped");
        Require(snapshot.windows.Length==item.windows,path+": wrong window count");
        Require(Array.Exists(snapshot.windows,x=>x.remaining==item.remaining.Value),path+": expected remaining percent missing");
        var fields=SessionReader.Map(Program.Json.DeserializeObject(Program.Json.Serialize(snapshot)));
        Require(SessionReader.Text(SessionReader.At(fields,"planType"))==(item.plan??""),path+": plan metadata was lost or guessed");
    }
    static void Label(Data data,double? expected)
    {
        using(var hud=new Hud(true,IntPtr.Zero))
        {
            typeof(Hud).GetField("data",Private).SetValue(hud,data);
            typeof(Hud).GetMethod("RenderRemaining",Private).Invoke(hud,null);
            var label=(Label)typeof(Hud).GetField("remaining",Private).GetValue(hud);
            var second=(Label)typeof(Hud).GetField("remainingSecondary",Private).GetValue(hud);
            string text=expected.HasValue?" "+expected.Value.ToString("0.#",System.Globalization.CultureInfo.InvariantCulture)+"%":" —";
            Require(expected.HasValue?(label.Text.EndsWith(text) || second.Text.EndsWith(text)):(label.Text.EndsWith(text) && second.Text==""),"HUD quota label: expected "+text+", actual "+label.Text+" / "+second.Text);
        }
    }
    [STAThread]
    static void Main(string[] args)
    {
        var report=new List<string>();int passed=0,failed=0;
        var now=DateTimeOffset.UtcNow;
        long reset=now.AddDays(1).ToUnixTimeSeconds();
        string expiry=reset.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cases=new[]{
            new Case("Free without a quota record","null",null,null,0),
            new Case("Free credits are not a subscription percentage","{\"plan_type\":\"free\",\"credits\":{\"has_credits\":true,\"balance\":\"20\"}}",null,null,0),
            new Case("Plus native dual windows","{\"limit_id\":\"codex\",\"plan_type\":\"plus\",\"primary\":{\"used_percent\":40,\"window_minutes\":300,\"resets_at\":"+expiry+"},\"secondary\":{\"used_percent\":75,\"window_minutes\":10080,\"resets_at\":"+expiry+"}}","plus",25,2),
            new Case("Free native single window","{\"plan_type\":\"free\",\"primary\":{\"used_percent\":25,\"window_minutes\":15,\"resets_at\":"+expiry+"},\"secondary\":null}","free",75,1),
            new Case("Free secondary only","{\"limit_id\":\"codex\",\"plan_type\":\"free\",\"primary\":null,\"secondary\":{\"used_percent\":80,\"window_minutes\":1440,\"resets_at\":"+expiry+"}}","free",20,1),
            new Case("Pro variant from native record shape","{\"limit_id\":\"codex\",\"plan_type\":\"prolite\",\"primary\":{\"used_percent\":36,\"window_minutes\":10080,\"resets_at\":"+expiry+"},\"secondary\":null}","prolite",64,1),
            new Case("Plus protocol field names","{\"limitId\":\"codex\",\"planType\":\"plus\",\"primary\":{\"usedPercent\":25,\"windowDurationMins\":15,\"resetsAt\":"+expiry+"},\"secondary\":null}","plus",75,1),
            new Case("Free protocol secondary only","{\"limitId\":\"codex\",\"planType\":\"free\",\"secondary\":{\"usedPercent\":83,\"windowDurationMins\":60,\"resetsAt\":"+expiry+"}}","free",17,1),
            new Case("Plus malformed reset preserves valid secondary","{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":10,\"resets_at\":9223372036854775807},\"secondary\":{\"used_percent\":45,\"window_minutes\":10080,\"resets_at\":"+expiry+"}}","plus",55,1),
            new Case("Plus malformed duration preserves valid secondary","{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":10,\"window_minutes\":\"invalid\"},\"secondary\":{\"used_percent\":50,\"window_minutes\":10080,\"resets_at\":"+expiry+"}}","plus",50,1),
            new Case("Free malformed reset remains unknown","{\"plan_type\":\"free\",\"primary\":{\"used_percent\":0,\"resets_at\":\"invalid\"}}",null,null,0),
            new Case("Free exhausted quota is zero remaining","{\"plan_type\":\"free\",\"primary\":{\"used_percent\":100,\"resets_at\":"+expiry+"}}","free",0,1),
            new Case("Free unused quota is fully remaining","{\"plan_type\":\"free\",\"primary\":{\"used_percent\":0,\"resets_at\":"+expiry+"}}","free",100,1),
            new Case("Unknown plan keeps recorded quota","{\"plan_type\":\"future-plan\",\"primary\":{\"used_percent\":12,\"resets_at\":"+expiry+"}}","future-plan",88,1),
            new Case("Legacy record has no invented plan","{\"primary\":{\"used_percent\":20,\"resets_at\":"+expiry+"}}",null,80,1),
            new Case("Other protocol bucket is not ordinary Codex quota","{\"limitId\":\"other-provider\",\"planType\":\"free\",\"primary\":{\"used_percent\":99}}",null,null,0)
        };
        string folder=Path.Combine(Program.CodexHome,"sessions","plan-quota-fixtures");Directory.CreateDirectory(folder);
        for(int i=0;i<cases.Length;i++)
        {
            var item=cases[i];string id=Guid.NewGuid().ToString();string path=Path.Combine(folder,"fixture-"+id+".jsonl");
            try
            {
                string at=now.AddMinutes(10).AddSeconds(i).ToString("o");
                string line="{\"timestamp\":\""+at+"\",\"type\":\"event_msg\",\"payload\":{\"type\":\"token_count\",\"info\":{\"total_token_usage\":{\"input_tokens\":1234,\"output_tokens\":56}},\"rate_limits\":"+item.json+"}}\n";
                File.WriteAllText(path,line);
                var data=SessionReader.Read(id);
                Require(data.input==1234 && data.output==56,"Quota fields must not prevent token statistics");
                Snapshot(data.accountQuota,item,"session");
                if(item.remaining.HasValue)
                {
                    var notify=typeof(QuotaLogs).GetMethod("ChangedFile",BindingFlags.NonPublic|BindingFlags.Static);
                    notify.Invoke(null,new object[]{null,new FileSystemEventArgs(WatcherChangeTypes.Changed,folder,Path.GetFileName(path))});
                    QuotaLogs.Refresh();
                    Snapshot(QuotaReader.Current(),item,"background");
                    QuotaReader.Apply(data);Label(data,item.remaining);
                }
                else if(i<2)Label(data,null);
                report.Add("PASS "+item.name);passed++;
            }
            catch(Exception ex){report.Add("FAIL "+item.name+": "+ex.GetBaseException().Message);failed++;}
            finally{File.Delete(path);}
        }
        try
        {
            var snapshot=QuotaReader.FromSession(SessionReader.Map(Program.Json.DeserializeObject("{\"plan_type\":\"free\",\"primary\":{\"used_percent\":99,\"resets_at\":1},\"secondary\":{\"used_percent\":35,\"resets_at\":"+expiry+"}}")),now.AddHours(1));
            var active=QuotaReader.Current(snapshot);
            Snapshot(active,new Case("expiry",null,"free",65,1),"expiry filtering");
            report.Add("PASS expiry preserves plan metadata and valid window");passed++;
        }
        catch(Exception ex){report.Add("FAIL expiry: "+ex.GetBaseException().Message);failed++;}
        report.Add("Plan quota compatibility: "+passed+" passed, "+failed+" failed, 0 skipped. Generated fixtures; no live Free/Plus account acceptance.");
        File.WriteAllLines(args[0],report);Environment.ExitCode=failed==0?0:1;
    }
}
