using System;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;

static class MenuResourceChecks
{
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    [DllImport("user32.dll")] static extern int GetGuiResources(IntPtr process,int kind);
    static void Rebuild(Hud hud,ToolStripMenuItem visible,ToolStripMenuItem order)
    {
        typeof(Hud).GetMethod("BuildDisplayMenus",Private).Invoke(hud,new object[]{visible,order});
        var handle=visible.DropDown.Handle;handle=order.DropDown.Handle;
        foreach(ToolStripMenuItem item in order.DropDownItems)handle=item.DropDown.Handle;
    }
    [STAThread]
    static void Main(string[] args)
    {
        try
        {
            using(var hud=new Hud(true,IntPtr.Zero))
            using(var visible=new ToolStripMenuItem())
            using(var order=new ToolStripMenuItem())
            {
                for(int i=0;i<10;i++)Rebuild(hud,visible,order);
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                var process=Process.GetCurrentProcess();int gdi=GetGuiResources(process.Handle,0),user=GetGuiResources(process.Handle,1);
                for(int i=0;i<80;i++)Rebuild(hud,visible,order);
                // Forced collection is confined to this fixture; the live HUD
                // never trims its working set or forces a collection.
                GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();
                int afterGdi=GetGuiResources(process.Handle,0),afterUser=GetGuiResources(process.Handle,1);
                var theme=typeof(Hud).GetField("menuTheme",Private).GetValue(hud);
                int tracked=((ICollection)theme.GetType().GetField("menus",Private).GetValue(theme)).Count;
                if(afterGdi>gdi+4 || afterUser>user+4 || tracked>Metrics.All.Length+5)throw new Exception("Menu native resources or weak-reference registry grew");
                File.WriteAllText(args[0],"PASS 80 menu rebuilds with native handles: GDI "+gdi+" -> "+afterGdi+", USER "+user+" -> "+afterUser+", tracked menus "+tracked+". Hidden fixture; GC only for leak check.");
            }
        }
        catch(Exception ex){File.WriteAllText(args[0],ex.ToString());Environment.ExitCode=1;}
    }
}
