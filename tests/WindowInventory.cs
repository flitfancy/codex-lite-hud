using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Web.Script.Serialization;

static class WindowInventory
{
    delegate bool EnumProc(IntPtr window,IntPtr state);
    [DllImport("user32.dll")]static extern bool EnumWindows(EnumProc proc,IntPtr state);
    [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
    [DllImport("user32.dll")]static extern IntPtr GetWindowLongPtrW(IntPtr window,int index);
    [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")]static extern IntPtr GetWindow(IntPtr window,uint relation);
    [DllImport("user32.dll")]static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rect rect);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowText(IntPtr window,StringBuilder text,int max);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder text,int max);
    struct Rect {public int left,top,right,bottom;}
    static void Main(string[] args)
    {
        var ids=new HashSet<uint>();foreach(var p in Process.GetProcessesByName("ChatGPT"))using(p){if(p.MainModule.FileName.IndexOf("OpenAI.Codex_",StringComparison.OrdinalIgnoreCase)>=0)ids.Add((uint)p.Id);}
        var result=new List<object>();
        EnumWindows(delegate(IntPtr w,IntPtr unused)
        {
            uint pid;GetWindowThreadProcessId(w,out pid);if(!ids.Contains(pid))return true;
            Rect r;GetWindowRect(w,out r);var title=new StringBuilder(256);GetWindowText(w,title,256);var cls=new StringBuilder(256);GetClassName(w,cls,256);
            result.Add(new{window=w.ToInt64(),pid=pid,title=title.ToString(),windowClass=cls.ToString(),visible=IsWindowVisible(w),owner=GetWindow(w,4).ToInt64(),dpi=GetDpiForWindow(w),rect=r,style=GetWindowLongPtrW(w,-16).ToInt64().ToString("X"),exStyle=GetWindowLongPtrW(w,-20).ToInt64().ToString("X")});return true;
        },IntPtr.Zero);
        File.WriteAllText(args[0],new JavaScriptSerializer().Serialize(result));
    }
}
