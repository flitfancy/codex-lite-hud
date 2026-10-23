using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class QuotaDisplayChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Case
    {
        internal string name,json,first,second;
        internal Case(string n,string j,string a,string b){name=n;json=j;first=a;second=b;}
    }
    static void Require(bool ok,string message){if(!ok)throw new Exception(message);}
    [STAThread]
    static void Main(string[] args)
    {
        var report=new List<string>();int passed=0,failed=0;var now=DateTimeOffset.UtcNow;
        string reset=now.AddDays(2).AddHours(5).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string shortReset=now.AddHours(2).AddMinutes(4).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        string freeReset=now.AddMinutes(12).AddSeconds(20).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cases=new[]{
            new Case("Missing quota occupies a single unknown reading","null","额度 —",""),
            new Case("Plus shows both actual reset countdowns","{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":40,\"window_minutes\":300,\"resets_at\":"+shortReset+"},\"secondary\":{\"used_percent\":75,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","60%/2.1时","25%/2.3天"),
            new Case("Duration order is independent of primary or secondary","{\"primary\":{\"used_percent\":60,\"window_minutes\":10080,\"resets_at\":"+reset+"},\"secondary\":{\"used_percent\":20,\"window_minutes\":300,\"resets_at\":"+shortReset+"}}","80%/2.1时","40%/2.3天"),
            new Case("Equal percentages retain two distinct windows","{\"primary\":{\"used_percent\":40,\"window_minutes\":300,\"resets_at\":"+shortReset+"},\"secondary\":{\"used_percent\":40,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","60%/2.1时","60%/2.3天"),
            new Case("Free shows actual reset time without a spare row","{\"plan_type\":\"free\",\"primary\":{\"used_percent\":25,\"window_minutes\":15,\"resets_at\":"+freeReset+"}}","75%/13分",""),
            new Case("Weekly-only Pro uses a single row","{\"plan_type\":\"prolite\",\"primary\":{\"used_percent\":36,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","64%/2.3天",""),
            new Case("Expired short window releases its row","{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":99,\"window_minutes\":300,\"resets_at\":1},\"secondary\":{\"used_percent\":35,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","65%/2.3天",""),
            new Case("Known reset works without window duration","{\"primary\":{\"used_percent\":20,\"resets_at\":"+reset+"}}","80%/2.3天",""),
            new Case("Missing reset is not inferred from window duration","{\"primary\":{\"used_percent\":20,\"window_minutes\":10080}}","80%/—","")
        };
        for(int i=0;i<cases.Length;i++)
        {
            var item=cases[i];
            try
            {
                var snapshot=QuotaReader.FromSession(SessionReader.Map(Program.Json.DeserializeObject(item.json)),now.AddMinutes(1).AddSeconds(i));
                using(var hud=new Hud(true,IntPtr.Zero))
                {
                    var data=new Data{sessionTitle="HUD",accountQuota=snapshot};
                    typeof(Hud).GetField("data",Private).SetValue(hud,data);
                    typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                    var first=(Label)typeof(Hud).GetField("remaining",Private).GetValue(hud);
                    var secondField=typeof(Hud).GetField("remainingSecondary",Private);
                    Require(secondField!=null,"HUD has no second quota reading");
                    var second=(Label)secondField.GetValue(hud);
                    Require(first.Text==item.first && second.Text==item.second,"Expected "+item.first+" / "+item.second+", actual "+first.Text+" / "+second.Text);
                    var settings=(DisplaySettings)typeof(Hud).GetField("display",Private).GetValue(hud);
                    Require(settings.Height("remaining")== (item.second==""?54:78),"Single-window accounts reserve a spare quota row");
                    if(item.second!="")Require(first.Bottom<=second.Top && second.Bottom<=hud.ClientSize.Height,"Quota lines overlap or leave the viewport");
                    if(i==1)
                    {
                        // Snapshot changes can arrive without a full session repaint.
                        snapshot.windows[0].remaining=5;
                        typeof(Hud).GetMethod("RenderRemaining",Private).Invoke(hud,null);
                        Require(first.Text=="5%/2.1时" && first.ForeColor!=second.ForeColor,"Low quota color or independent background update missing");
                        int doubleHeight=hud.Height;
                        var both=snapshot.windows;snapshot.windows=new[]{both[1]};
                        typeof(Hud).GetMethod("RenderRemaining",Private).Invoke(hud,null);
                        Require(hud.Height==doubleHeight-24 && first.Text=="25%/2.3天" && second.Text=="","Two-to-one quota update did not release the row and HUD height");
                        snapshot.windows=both;typeof(Hud).GetMethod("RenderRemaining",Private).Invoke(hud,null);
                        Require(hud.Height==doubleHeight,"One-to-two quota update did not restore the second row");
                        typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,164);
                        typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                        Require(first.Text=="5%/2.1h" && second.Text=="25%/2.3d","Compact mode dropped a window or its reset countdown");
                        Require(first.Bottom<=second.Top,"Compact quota readings overlap");
                    }
                }
                report.Add("PASS "+item.name);passed++;
            }
            catch(Exception ex){report.Add("FAIL "+item.name+": "+ex.GetBaseException().Message);failed++;}
        }
        try
        {
            var at=new DateTimeOffset(2026,10,22,12,0,0,TimeSpan.FromHours(8));
            var window=new QuotaWindow {windowMinutes=10080,remaining=7,resetAt=at.AddMinutes(61).ToUniversalTime().ToString("o")};
            Require(Hud.QuotaReading(window,at,false)=="7%/1.1时","UTC reset was not compared against the local instant");
            Require(Hud.QuotaReading(window,at.AddMinutes(2),false)=="7%/59分","Unchanged snapshot did not count down across the hour boundary");
            Require(Hud.QuotaReading(window,at.AddMinutes(60).AddSeconds(30),false)=="7%/<1分","Sub-minute reset was rounded to zero");
            Require(Hud.QuotaReading(window,at.AddMinutes(61),false)=="待更新","Expired snapshot still displayed its old percentage");
            report.Add("PASS actual reset instant, unchanged-snapshot countdown, sub-minute and expiry boundaries");passed++;
        }
        catch(Exception ex){report.Add("FAIL reset countdown boundaries: "+ex.GetBaseException().Message);failed++;}
        bool automatic=SessionReader.IsAutomatic();string selected="";
        try{selected=SessionReader.SelectedId();}catch(FileNotFoundException){}
        try
        {
            SessionReader.Select("00000000-0000-7000-8000-000000000001");
            QuotaReader.Current(new QuotaSnapshot {readAt=now.AddMinutes(2).ToString("o"),windows=new[]{new QuotaWindow {windowMinutes=10080,remaining=7,resetAt=DateTimeOffset.Now.AddMinutes(22.5).ToString("o")}}});
            var steady=Reader.ReadCurrent(IntPtr.Zero,false).GetAwaiter().GetResult();
            using(var hud=new Hud(true,IntPtr.Zero))
            {
                typeof(Hud).GetField("data",Private).SetValue(hud,steady);
                typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                var label=(Label)typeof(Hud).GetField("remaining",Private).GetValue(hud);
                label.Text="7%/24分";
                typeof(Hud).GetMethod("RefreshData",Private).Invoke(hud,null);
                var deadline=DateTime.UtcNow.AddSeconds(3);
                while((bool)typeof(Hud).GetField("reading",Private).GetValue(hud) && DateTime.UtcNow<deadline){Application.DoEvents();System.Threading.Thread.Sleep(5);}
                var refreshed=(Data)typeof(Hud).GetField("data",Private).GetValue(hud);
                Require(!(bool)typeof(Hud).GetField("reading",Private).GetValue(hud),"Regular refresh did not finish");
                Require(steady.SameDisplay(refreshed) && Object.ReferenceEquals(steady.accountQuota,refreshed.accountQuota),"Fixture unexpectedly changed the snapshot or session readings");
                Require(label.Text=="7%/23分","Regular unchanged-data refresh left the reset countdown frozen: "+label.Text);
            }
            report.Add("PASS regular unchanged-session refresh updates the countdown without a new quota snapshot");passed++;
        }
        catch(Exception ex){report.Add("FAIL unchanged-session countdown refresh: "+ex.GetBaseException().Message);failed++;}
        finally{if(!String.IsNullOrEmpty(selected))SessionReader.Select(selected);SessionReader.Automatic(automatic);}
        report.Add("Quota display: "+passed+" passed, "+failed+" failed, 0 skipped. Generated snapshots and hidden HUD widgets.");
        File.WriteAllLines(args[0],report);Environment.ExitCode=failed==0?0:1;
    }
}
