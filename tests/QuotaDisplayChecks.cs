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
        string reset=now.AddDays(1).ToUnixTimeSeconds().ToString(System.Globalization.CultureInfo.InvariantCulture);
        var cases=new[]{
            new Case("Missing quota occupies a single unknown reading","null","额度 —",""),
            new Case("Plus five-hour and weekly quota both shown","{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":40,\"window_minutes\":300,\"resets_at\":"+reset+"},\"secondary\":{\"used_percent\":75,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","5小时 60%","7天 25%"),
            new Case("Duration order is independent of primary or secondary","{\"primary\":{\"used_percent\":60,\"window_minutes\":10080,\"resets_at\":"+reset+"},\"secondary\":{\"used_percent\":20,\"window_minutes\":300,\"resets_at\":"+reset+"}}","5小时 80%","7天 40%"),
            new Case("Equal percentages retain two distinct windows","{\"primary\":{\"used_percent\":40,\"window_minutes\":300,\"resets_at\":"+reset+"},\"secondary\":{\"used_percent\":40,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","5小时 60%","7天 60%"),
            new Case("Free records its actual duration without a spare row","{\"plan_type\":\"free\",\"primary\":{\"used_percent\":25,\"window_minutes\":15,\"resets_at\":"+reset+"}}","15分钟 75%",""),
            new Case("Weekly-only Pro uses a single row","{\"plan_type\":\"prolite\",\"primary\":{\"used_percent\":36,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","7天 64%",""),
            new Case("Expired short window releases its row","{\"plan_type\":\"plus\",\"primary\":{\"used_percent\":99,\"window_minutes\":300,\"resets_at\":1},\"secondary\":{\"used_percent\":35,\"window_minutes\":10080,\"resets_at\":"+reset+"}}","7天 65%",""),
            new Case("Unknown duration does not reserve another row","{\"primary\":{\"used_percent\":20,\"resets_at\":"+reset+"}}","窗口1 80%","")
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
                        Require(first.Text=="5小时 5%" && first.ForeColor!=second.ForeColor,"Low quota color or independent background update missing");
                        int doubleHeight=hud.Height;
                        var both=snapshot.windows;snapshot.windows=new[]{both[1]};
                        typeof(Hud).GetMethod("RenderRemaining",Private).Invoke(hud,null);
                        Require(hud.Height==doubleHeight-24 && first.Text=="7天 25%" && second.Text=="","Two-to-one quota update did not release the row and HUD height");
                        snapshot.windows=both;typeof(Hud).GetMethod("RenderRemaining",Private).Invoke(hud,null);
                        Require(hud.Height==doubleHeight,"One-to-two quota update did not restore the second row");
                        typeof(Hud).GetField("layoutPixels",Private).SetValue(hud,164);
                        typeof(Hud).GetMethod("ApplyLayout",Private).Invoke(hud,null);
                        Require(first.Text=="5h 5%" && second.Text=="7d 25%","Compact mode dropped a window or its duration");
                        Require(first.Bottom<=second.Top,"Compact quota readings overlap");
                    }
                }
                report.Add("PASS "+item.name);passed++;
            }
            catch(Exception ex){report.Add("FAIL "+item.name+": "+ex.GetBaseException().Message);failed++;}
        }
        report.Add("Quota display: "+passed+" passed, "+failed+" failed, 0 skipped. Generated snapshots and hidden HUD widgets.");
        File.WriteAllLines(args[0],report);Environment.ExitCode=failed==0?0:1;
    }
}
