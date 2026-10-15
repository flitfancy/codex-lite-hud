using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Web.Script.Serialization;
using System.Windows.Forms;

// Opt-in, read-only native pixel benchmark. Uses an explicit existing HUD
// rectangle; never shows this fixture or moves/activates the target window.
static class ThemeBench
{
    const BindingFlags Members=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;
    [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")]static extern int GetGuiResources(IntPtr process,int kind);
    static FieldInfo Field(Type type,string name){return type.GetField(name,Members);}
    [STAThread]
    static void Main(string[] args)
    {
        SetProcessDpiAwarenessContext(new IntPtr(-4));Application.EnableVisualStyles();
        var assembly=Assembly.LoadFrom(Path.GetFullPath(args[0]));var type=assembly.GetType("Hud",true);
        var target=new IntPtr(Int64.Parse(args[2]));var location=new Point(Int32.Parse(args[3]),Int32.Parse(args[4]));var size=new Size(Int32.Parse(args[5]),Int32.Parse(args[6]));
        int iterations=Int32.Parse(args[7]);
        using(var hud=(Form)type.GetConstructor(Members,null,new[]{typeof(bool),typeof(IntPtr)},null).Invoke(new object[]{true,target}))
        {
            hud.Location=location;hud.ClientSize=size;
            Field(type,"dockLeft").SetValue(hud,location.X);Field(type,"dockTop").SetValue(hud,location.Y-32);
            Field(type,"positionLocked").SetValue(hud,true);
            var sample=type.GetMethod("SampleTheme",Members);var last=Field(type,"lastTheme");
            Field(type,"backgroundLeft").SetValue(hud,Color.Magenta);
            var sampleArgs=sample.GetParameters().Length==0?new object[0]:new object[]{location.Y};
            Action run=delegate{last.SetValue(hud,DateTime.MinValue);sample.Invoke(hud,sampleArgs);};
            for(int i=0;i<5;i++)run();
            if(((Color)Field(type,"backgroundLeft").GetValue(hud)).ToArgb()==Color.Magenta.ToArgb())throw new Exception("Native sample unavailable: target is covered or moved");
            var process=Process.GetCurrentProcess();process.Refresh();int gdi=GetGuiResources(process.Handle,0),gc=GC.CollectionCount(0);
            var cpu=process.TotalProcessorTime;long privateBefore=process.PrivateMemorySize64;
            var times=new double[iterations];var total=Stopwatch.StartNew();
            for(int i=0;i<iterations;i++){var watch=Stopwatch.StartNew();run();times[i]=watch.Elapsed.TotalMilliseconds;}
            total.Stop();process.Refresh();Array.Sort(times);
            var result=new {assembly=Path.GetFullPath(args[0]),width=size.Width,height=size.Height,iterations=iterations,elapsedMs=total.Elapsed.TotalMilliseconds,meanMs=total.Elapsed.TotalMilliseconds/iterations,medianMs=times[iterations/2],p95Ms=times[(int)Math.Ceiling(iterations*.95)-1],cpuMs=(process.TotalProcessorTime-cpu).TotalMilliseconds,gen0=GC.CollectionCount(0)-gc,privateBefore=privateBefore,privateAfter=process.PrivateMemorySize64,gdiBefore=gdi,gdiAfter=GetGuiResources(process.Handle,0)};
            string json=new JavaScriptSerializer().Serialize(result);File.WriteAllText(args[1],json);Console.WriteLine(json);
        }
    }
}
