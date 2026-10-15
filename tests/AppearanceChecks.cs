using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

static class AppearanceChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    static Color Mix(Color a,Color b,double t){return Color.FromArgb((int)Math.Round(a.R+(b.R-a.R)*t),(int)Math.Round(a.G+(b.G-a.G)*t),(int)Math.Round(a.B+(b.B-a.B)*t));}
    static int Difference(Color a,Color b){return Math.Abs(a.R-b.R)+Math.Abs(a.G-b.G)+Math.Abs(a.B-b.B);}
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            using(var hud=new Hud(true,IntPtr.Zero))
            {
                WindowSession.LatestSidebar=new SidebarFrame{window=0,width=73,leftOffset=1,dpi=96,topOffset=61,bottomOffset=5,navigationBottomOffset=505,footerReserved=65};
                typeof(Hud).GetField("bounds",Private).SetValue(hud,new Native.RECT{Left=40,Top=80,Right=1480,Bottom=1680});
                typeof(Hud).GetMethod("UpdateDock",Private).Invoke(hud,null);
                int left=(int)typeof(Hud).GetField("dockLeft",Private).GetValue(hud);
                if(left!=41 || hud.Width!=72)throw new Exception("HUD paints over native frame pixel: left="+left+", width="+hud.Width+"; expected 41 / 72");
            }
            File.WriteAllText(args[0],"PASS native frame inset excluded from HUD bounds.");
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
        try
        {
            using(var hud=new Hud(true,IntPtr.Zero))
            {
                hud.ClientSize=new Size(72,200);
                var top=new Color[72];var bottom=new Color[72];var left=new Color[200];var right=new Color[200];
                for(int x=0;x<72;x++)
                {
                    double edge=Math.Max(0,(x-48)/23d);
                    top[x]=Mix(Color.FromArgb(247,240,249),Color.FromArgb(235,229,237),edge);
                    bottom[x]=Mix(Color.FromArgb(240,243,249),Color.FromArgb(229,233,237),edge);
                }
                for(int y=0;y<200;y++){double t=y/199d;left[y]=Mix(top[0],bottom[0],t);right[y]=Mix(top[71],bottom[71],t);}
                typeof(Hud).GetField("backgroundLeft",Private).SetValue(hud,top[0]);typeof(Hud).GetField("backgroundRight",Private).SetValue(hud,top[71]);
                // Optional reflection also runs against the previous renderer,
                // whose two-color fill cannot reproduce this sidebar surface.
                var apply=typeof(Hud).GetMethod("ApplyBackdrop",Private);if(apply!=null)apply.Invoke(hud,new object[]{top,bottom,left,right});
                using(var image=new Bitmap(72,200))
                using(var graphics=Graphics.FromImage(image))
                {
                    typeof(Hud).GetMethod("OnPaintBackground",Private).Invoke(hud,new object[]{new PaintEventArgs(graphics,new Rectangle(0,0,72,200))});
                    foreach(int x in new[]{1,20,48,60,70})foreach(int y in new[]{1,70,140,198})
                    {
                        var expected=Mix(top[x],bottom[x],y/199d);var actual=image.GetPixel(x,y);
                        if(Difference(actual,expected)>3)throw new Exception("Sidebar color seam at "+x+","+y+": "+actual+" vs "+expected);
                    }
                }
            }
            File.AppendAllText(args[0]," PASS non-linear horizontal and vertical sidebar colors.");
        }
        catch(Exception ex){File.AppendAllText(args[0],"\n"+ex);Environment.ExitCode=1;}
    }
}
