using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class VisibilityChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    sealed class Target:Form
    {
        protected override bool ShowWithoutActivation {get{return true;}}
        protected override CreateParams CreateParams {get{var p=base.CreateParams;p.ExStyle|=0x08000080;return p;}}
        internal Target(){FormBorderStyle=FormBorderStyle.None;ShowInTaskbar=false;StartPosition=FormStartPosition.Manual;Bounds=new Rectangle(-22000,-22000,1440,1256);Opacity=0;}
    }
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            Native.SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();
            uint foregroundPid;Native.GetWindowThreadProcessId(Native.GetForegroundWindow(),out foregroundPid);
            var known=(Dictionary<uint,bool>)typeof(Hud).GetField("KnownProcesses",BindingFlags.Static|BindingFlags.NonPublic).GetValue(null);
            known[foregroundPid]=false; // Treat the untouched real foreground as another app.
            using(var target=new Target())
            {
                target.Show();
                using(var hud=new Hud(false,target.Handle))
                {
                    WindowSession.LatestSidebar=new SidebarFrame {window=target.Handle.ToInt64(),width=73,dpi=(int)Native.GetDpiForWindow(target.Handle),topOffset=61,bottomOffset=5,navigationBottomOffset=505,footerReserved=65};
                    var follow=typeof(Hud).GetMethod("Follow",Private);
                    typeof(Hud).GetField("lastDiscovery",Private).SetValue(hud,DateTime.UtcNow.AddDays(1));
                    follow.Invoke(hud,null);
                    if(!hud.Visible || hud.TopMost)throw new Exception("HUD should stay with visible target when a different app is foreground");
                    if(Native.GetWindow(target.Handle,3)!=hud.Handle)throw new Exception("HUD should remain immediately above its target in normal Z order");
                    target.WindowState=FormWindowState.Minimized;follow.Invoke(hud,null);
                    if(hud.Visible)throw new Exception("Minimized target must hide HUD");
                    target.WindowState=FormWindowState.Normal;follow.Invoke(hud,null);
                    if(!hud.Visible)throw new Exception("Restored target must restore HUD");
                    target.Hide();follow.Invoke(hud,null);
                    if(hud.Visible)throw new Exception("Hidden target must hide HUD");
                    target.Show();follow.Invoke(hud,null);
                    if(!hud.Visible)throw new Exception("Visible target must restore HUD");
                    target.Close();follow.Invoke(hud,null);
                    if(hud.Visible || hud.IsDisposed)throw new Exception("Closed target must hide HUD while retaining its process for rediscovery");
                }
            }
            File.WriteAllText(args[0],"PASS background visibility, normal Z order, minimize/restore, hide/show, target close and future rediscovery lifetime. Own offscreen fixture only.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
