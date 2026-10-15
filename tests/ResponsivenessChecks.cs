using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

static class ResponsivenessChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [STAThread]
    static void Main(string[] args)
    {
        var issues=new List<string>();
        try
        {
            var gate=typeof(QuotaLogs).GetField("Gate",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            using(var entered=new ManualResetEvent(false))
            {
                var worker=new Thread(delegate(){lock(gate){entered.Set();Thread.Sleep(160);}});worker.IsBackground=true;worker.Start();entered.WaitOne();
                var clock=Stopwatch.StartNew();bool changed=QuotaLogs.Changed;clock.Stop();worker.Join();
                if(clock.Elapsed.TotalMilliseconds>=80)issues.Add("UI quota notification check waited for background I/O lock: "+clock.Elapsed.TotalMilliseconds.ToString("0.0")+" ms");
            }
            using(var target=new Form{FormBorderStyle=FormBorderStyle.None,StartPosition=FormStartPosition.Manual,Left=-22000,Top=-22000,Width=1440,Height=1600})
            using(var hud=new Hud(true,target.Handle))
            {
                int actual=Native.VisibleBorder(target.Handle);
                WindowSession.LatestSidebar=new SidebarFrame{window=target.Handle.ToInt64(),width=73,leftOffset=actual+1,dpi=(int)Native.GetDpiForWindow(target.Handle),topOffset=61,bottomOffset=5,navigationBottomOffset=505,footerReserved=65};
                typeof(Hud).GetField("bounds",Private).SetValue(hud,new Native.RECT{Left=40,Top=80,Right=1480,Bottom=1680});
                typeof(Hud).GetMethod("UpdateDock",Private).Invoke(hud,null);
                int left=(int)typeof(Hud).GetField("dockLeft",Private).GetValue(hud);
                if(left!=40+actual)issues.Add("Cached sidebar inset overrode current physical border: "+left+" vs "+(40+actual));
            }
            if(issues.Count>0)throw new Exception(String.Join("\n",issues.ToArray()));
            File.WriteAllText(args[0],"PASS nonblocking UI notification check during background I/O, current physical border overrides stale sidebar cache. Own hidden target only.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
