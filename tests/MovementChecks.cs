using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using System.Windows.Forms;

// Exercises the real Hud follow path against our own invisible test window.
// It never moves, activates or inspects the Codex application's UI.
static class MovementChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Target : Form
    {
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        internal Target(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(-22000,-22000,1440,1256);Opacity=0;}
    }
    [STAThread]
    static void Main(string[] args)
    {
        Native.SetProcessDpiAwarenessContext(new IntPtr(-4));
        Application.EnableVisualStyles();
        using(var target=new Target())
        {
            target.Shown+=async delegate
            {
                try
                {
                    WindowSession.LatestSidebar=new SidebarFrame {window=target.Handle.ToInt64(),width=73,dpi=(int)Native.GetDpiForWindow(target.Handle),topOffset=61,bottomOffset=5,navigationBottomOffset=505,footerReserved=65};
                    using(var hud=new Hud(true,target.Handle))
                    {
                        typeof(Hud).GetField("lastRead",Private).SetValue(hud,DateTime.UtcNow.AddDays(1));
                        typeof(Hud).GetField("lastTheme",Private).SetValue(hud,DateTime.UtcNow.AddDays(1));
                        typeof(Hud).GetField("lastStatus",Private).SetValue(hud,DateTime.UtcNow.AddDays(1));
                        hud.Show();
                        await Task.Delay(150);
                        var samples=new List<double>();
                        for(int i=0;i<48;i++)
                        {
                            int targetX=-21900+i*8,expected=targetX+Native.VisibleBorder(target.Handle);
                            var completion=new TaskCompletionSource<double>();
                            var clock=new Stopwatch();
                            EventHandler moved=delegate {if(hud.Left==expected)completion.TrySetResult(clock.Elapsed.TotalMilliseconds);};
                            hud.LocationChanged+=moved;
                            clock.Start();
                            Native.SetWindowPos(target.Handle,IntPtr.Zero,targetX,-22000,1440,1256,0x0010|0x0004);
                            var finished=await Task.WhenAny(completion.Task,Task.Delay(180));
                            hud.LocationChanged-=moved;
                            if(finished!=completion.Task)throw new Exception("HUD did not reach test target position within 180 ms; expected="+expected+", actual="+hud.Left+", target="+target.Left);
                            if(i>=4)samples.Add(await completion.Task);
                            await Task.Delay(3+i%7);
                        }
                        samples.Sort();
                        double median=samples[samples.Count/2],p95=samples[(int)Math.Ceiling(samples.Count*.95)-1];
                        var events=(WindowTracking)typeof(Hud).GetField("tracking",Private).GetValue(hud);
                        if(events==null || !events.LocationEventsActive || events.PositionEvents<44)throw new Exception("Native location events did not drive the HUD");
                        if(!events.MovingEventsActive || !events.VisibilityEventsActive || !events.ForegroundEventsActive)throw new Exception("Native event hook diagnostics did not report all installed hooks");
                        typeof(Hud).GetField("targetMoving",Private).SetValue(hud,true);
                        typeof(Hud).GetMethod("RecoverMotion",Private).Invoke(hud,null);
                        if((bool)typeof(Hud).GetField("targetMoving",Private).GetValue(hud))throw new Exception("Lost move-end state did not recover");
                        typeof(Hud).GetField("codex",Private).SetValue(hud,new IntPtr(-1));
                        typeof(Hud).GetMethod("AttachTracking",Private).Invoke(hud,null);
                        var fallback=(System.Windows.Forms.Timer)typeof(Hud).GetField("follow",Private).GetValue(hud);
                        if(fallback.Interval!=40)throw new Exception("Missing event hook did not restore fast polling fallback");
                        if(events.LocationEventsActive || events.MovingEventsActive || events.VisibilityEventsActive || !events.ForegroundEventsActive)throw new Exception("Invalid target hook diagnostics did not distinguish foreground monitoring");
                        typeof(Hud).GetField("codex",Private).SetValue(hud,target.Handle);
                        typeof(Hud).GetMethod("AttachTracking",Private).Invoke(hud,null);
                        if(fallback.Interval!=200 || !events.LocationEventsActive)throw new Exception("Event hooks did not rebind to target");
                        long before=events.PositionEvents;
                        hud.Dispose();
                        if(events.LocationEventsActive || events.MovingEventsActive || events.VisibilityEventsActive || events.ForegroundEventsActive)throw new Exception("Disposed event hook diagnostics retained active hooks");
                        Native.SetWindowPos(target.Handle,IntPtr.Zero,-21000,-22000,1440,1256,0x0010|0x0004);
                        await Task.Delay(30);
                        if(events.PositionEvents!=before)throw new Exception("Location hook still active after HUD disposal");
                        double limit=args.Length>1 ? Double.Parse(args[1],System.Globalization.CultureInfo.InvariantCulture) : 18;
                        bool pass=p95<limit;
                        File.WriteAllText(args[0],String.Format(System.Globalization.CultureInfo.InvariantCulture,"{0}: native move-to-HUD latency; n={1}, median={2:0.00} ms, p95={3:0.00} ms, max={4:0.00} ms. Threshold p95 < {5} ms. Own test window only. PASS event delivery, stale move-state recovery, hook failure fallback, rebind and disposal.",pass?"PASS":"FAIL",samples.Count,median,p95,samples.Last(),limit));
                        Environment.ExitCode=pass?0:1;
                    }
                }
                catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
                finally {target.Close();}
            };
            Application.Run(target);
        }
    }
}
