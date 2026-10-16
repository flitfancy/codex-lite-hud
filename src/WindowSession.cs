using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Linq;
using System.Text.RegularExpressions;

sealed class SidebarFrame
{
    public long window;
    public int leftOffset,topOffset,bottomOffset,width,rootWidth,rootHeight,dpi,seedWidth,navigationBottomOffset,footerReserved;
}

// Native UIA properties are read directly: older managed UIA assemblies
// don't register Chromium's AriaProperties and LegacyIAccessible properties.
static class WindowSession
{
    internal static string LastDebug;
    internal static SidebarFrame LatestSidebar;
    [ComImport, Guid("30CBE57D-D9D0-452A-AB13-7AC5AC4825EE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Automation
    {
        void S0(); void S1(); void GetRootElement(out Element e);
        void ElementFromHandle(IntPtr hwnd, out Element e);
        void S4(); void S5(); void S6(); void S7(); void S8(); void S9();
        void S10(); void S11(); void S12(); void GetRawViewWalker(out TreeWalker walker); void S14(); void S15(); void S16(); void S17();
        void CreateTrueCondition(out IntPtr condition); void S19();
        void CreatePropertyCondition(int property, [MarshalAs(UnmanagedType.Struct)] object value, out IntPtr condition);
        void CreatePropertyConditionEx(int property, [MarshalAs(UnmanagedType.Struct)] object value, int flags, out IntPtr condition);
    }
    [ComImport, Guid("D22108AA-8AC5-49A5-837B-37BBB3D7591E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Element
    {
        void S0(); void S1(); void S2();
        void FindAll(int scope, IntPtr condition, out Elements elements);
        void S4(); void S5(); void S6();
        void GetCurrentPropertyValue(int id, [MarshalAs(UnmanagedType.Struct)] out object value);
    }
    [ComImport, Guid("14314595-B4BC-4055-95F2-58F2E42C9855"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface Elements
    {
        void GetLength(out int count);
        void GetElement(int index, out Element element);
    }
    [ComImport,Guid("4042C624-389C-4AFC-A630-9DF854A541FC"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    internal interface TreeWalker { void GetParentElement(Element element,out Element parent); }
    internal static object Property(Element e, int id)
    {
        try { object value; e.GetCurrentPropertyValue(id, out value); return value; } catch { return null; }
    }
    internal static string Text(Element e, int id) { return Convert.ToString(Property(e,id)); }

    static SidebarFrame ReadSidebar(Automation automation,Element seed,double[] rootRect,IntPtr hwnd,double[] seedRect)
    {
        int dpi=(int)Native.GetDpiForWindow(hwnd);
        var old=LatestSidebar;
        if(old!=null && old.window==hwnd.ToInt64() && old.rootWidth==(int)Math.Round(rootRect[2]) && old.rootHeight==(int)Math.Round(rootRect[3]) && old.dpi==dpi && old.seedWidth==(int)Math.Round(seedRect[2])) return old;
        double scale=Math.Max(1,dpi/96d);
        SidebarFrame best=null;
        double bestHeight=0;
        TreeWalker walker=null; Element current=null;
        try
        {
            automation.GetRawViewWalker(out walker);
            walker.GetParentElement(seed,out current);
            for(int depth=0;current!=null && depth<12;depth++)
            {
                var r=Property(current,30001) as double[];
                if(r!=null && r.Length>=4)
                {
                    if(r[2]>=45*scale && r[2]<=200*scale && r[3]>=rootRect[3]*.75 && r[0]<=rootRect[0]+20*scale && r[3]>bestHeight)
                    {
                        bestHeight=r[3];
                        // Window movement can occur between UIA property calls.
                        // Dock to the native window edge, never to a difference
                        // between two separately sampled screen coordinates.
                        int bottomInset=Native.IsZoomed(hwnd) ? 0 : (int)Math.Round(5*scale);
                        int header=(int)Math.Round(rootRect[3]-r[3])-bottomInset;
                        if(header<0 || header>160*scale)header=(int)Math.Round(61*scale);
                        var alignedRoot=new[]{r[0],r[1]-header,rootRect[2],rootRect[3]};
                        int footer;
                        int navigation=NavigationBottom(automation,current,alignedRoot,r,seedRect,scale,out footer);
                        if(navigation<header || navigation>rootRect[3]-footer)navigation=header+(int)Math.Round(80*scale);
                        best=new SidebarFrame { window=hwnd.ToInt64(),leftOffset=Native.VisibleBorder(hwnd),topOffset=header,bottomOffset=bottomInset,width=(int)Math.Round(r[2]),rootWidth=(int)Math.Round(rootRect[2]),rootHeight=(int)Math.Round(rootRect[3]),dpi=dpi,seedWidth=(int)Math.Round(seedRect[2]),navigationBottomOffset=navigation,footerReserved=footer };
                    }
                    if(r[2]>rootRect[2]*.7 && r[3]>rootRect[3]*.7) break;
                }
                Element parent; walker.GetParentElement(current,out parent);
                Marshal.ReleaseComObject(current); current=parent;
            }
        }
        finally { if(current!=null)Marshal.ReleaseComObject(current); if(walker!=null)Marshal.ReleaseComObject(walker); }
        return best;
    }

    static int NavigationBottom(Automation automation,Element rail,double[] root,double[] rect,double[] seed,double scale,out int footer)
    {
        int bottom=(int)Math.Round(seed[1]+seed[3]-root[1]);
        footer=(int)Math.Round(64*scale);
        var ranges=new List<double[]>();
        IntPtr condition=IntPtr.Zero;Elements buttons=null;
        try
        {
            automation.CreatePropertyCondition(30003,50000,out condition);
            rail.FindAll(4,condition,out buttons);
            int count;buttons.GetLength(out count);
            for(int i=0;i<Math.Min(count,80);i++)
            {
                Element e;buttons.GetElement(i,out e);
                try
                {
                    var r=Property(e,30001) as double[];
                    if(r!=null && !Object.Equals(Property(e,30022),true) && r[0]>=rect[0]-1 && r[0]+r[2]<=rect[0]+rect[2]+1 && r[2]>12*scale && r[3]>12*scale)
                        ranges.Add(new[]{r[1],r[1]+r[3]});
                }
                finally{Marshal.ReleaseComObject(e);}
            }
        }
        catch { }
        finally{if(buttons!=null)Marshal.ReleaseComObject(buttons);if(condition!=IntPtr.Zero)Marshal.Release(condition);}
        var merged=new List<double[]>();
        foreach(var range in ranges.OrderBy(x=>x[0]))
        {
            if(merged.Count>0 && range[0]<=merged[merged.Count-1][1]+2*scale)merged[merged.Count-1][1]=Math.Max(merged[merged.Count-1][1],range[1]);
            else merged.Add((double[])range.Clone());
        }
        double gap=24*scale;int split=-1;
        for(int i=0;i<merged.Count-1;i++)if(merged[i+1][0]-merged[i][1]>gap){gap=merged[i+1][0]-merged[i][1];split=i;}
        if(split>=0)
        {
            bottom=(int)Math.Round(merged[split][1]-root[1]);
            footer=Math.Max(footer,(int)Math.Round(rect[1]+rect[3]-merged[split+1][0]+8*scale));
        }
        return bottom;
    }

    static void CaptureSidebar(Automation automation,Element root,double[] rootRect,IntPtr hwnd)
    {
        if(rootRect==null)return;
        var previous=LatestSidebar;
        if(previous!=null && previous.window==hwnd.ToInt64() && previous.rootWidth==(int)Math.Round(rootRect[2]) && previous.rootHeight==(int)Math.Round(rootRect[3]) && previous.dpi==(int)Native.GetDpiForWindow(hwnd))return;
        IntPtr condition=IntPtr.Zero; Elements nodes=null;
        try
        {
            automation.CreatePropertyCondition(30005,"首页",out condition);
            root.FindAll(4,condition,out nodes);
            int count;nodes.GetLength(out count);
            double scale=Native.GetDpiForWindow(hwnd)/96d;
            for(int i=0;i<Math.Min(count,10);i++)
            {
                Element seed;nodes.GetElement(i,out seed);
                try
                {
                    var r=Property(seed,30001) as double[];
                    if(r!=null && r[0]<rootRect[0]+45*scale && r[2]<90*scale)
                    {
                        var found=ReadSidebar(automation,seed,rootRect,hwnd,r);
                        if(found!=null){LatestSidebar=found;break;}
                    }
                }
                finally{Marshal.ReleaseComObject(seed);}
            }
        }
        finally{if(nodes!=null)Marshal.ReleaseComObject(nodes);if(condition!=IntPtr.Zero)Marshal.Release(condition);}
    }

    internal static string ResolveName(string name,List<SessionEntry> catalog,out bool ambiguous)
    {
        ambiguous=false;
        var named=catalog.Where(x=>!String.IsNullOrEmpty(x.title) && x.title.Trim().Equals(name,StringComparison.Ordinal)).ToArray();
        if(named.Length==1) return named[0].id;
        if(named.Length>1)
        {
            var active=NativeThreadCatalog.ActiveIds();
            var live=active==null ? named : named.Where(x=>active.Contains(x.id)).ToArray();
            if(live.Length==1) return live[0].id;
            ambiguous=true;
        }
        return null;
    }

    internal static string Resolve(IntPtr hwnd, List<SessionEntry> catalog, bool diagnostic=false)
    {
        if (hwnd == IntPtr.Zero) throw new InvalidOperationException("等待 Codex 窗口");
        var matches = new HashSet<string>();
        var observations = diagnostic ? new List<object>() : null;
        bool ambiguous = false;
        var automation = (Automation)Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("FF48DBA4-60EF-4201-AA87-54103EEF594E")));
        Element root = null; Elements elements = null; IntPtr condition = IntPtr.Zero;
        try
        {
            automation.ElementFromHandle(hwnd, out root);
            // Provider-side substring filtering reads only current-page nodes,
            // rather than walking chat messages or checking every UI element.
            automation.CreatePropertyConditionEx(30102, "current=page", 2, out condition);
            root.FindAll(4, condition, out elements);
            int count; elements.GetLength(out count);
            var rootRect = Property(root,30001) as double[];
            CaptureSidebar(automation,root,rootRect,hwnd);
            double scale = Native.GetDpiForWindow(hwnd) / 96d;
            if(diagnostic)observations.Add(new { count=count, root=rootRect, scale=scale });
            for (int i = 0; i < Math.Min(count, 40); i++)
            {
                Element e; elements.GetElement(i, out e);
                try
                {
                    if (Object.Equals(Property(e,30022),true)) continue;
                    var rect = Property(e,30001) as double[];
                    string currentName=diagnostic ? Text(e,30005) : null;
                    if(diagnostic)observations.Add(new { name=currentName, rect=rect, aria=Text(e,30102) });
                    if(rootRect!=null && rect!=null && rect[0]<rootRect[0]+45*scale && rect[2]<90*scale)
                    {
                        var sidebar=ReadSidebar(automation,e,rootRect,hwnd,rect);
                        if(sidebar!=null)LatestSidebar=sidebar;
                    }
                    // The product rail (Home, etc.) also has a current-page
                    // marker. A task row is wider and to the rail's right.
                    if (rootRect != null && rect != null && (rect[0] < rootRect[0] + 45 * scale || rect[2] < 75 * scale)) continue;
                    string value = Text(e,30011) + " " + Text(e,30093) + " " + Text(e,30013);
                    var idMatch = Regex.Match(value, "[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}");
                    if (idMatch.Success && catalog.Any(x => x.id.Equals(idMatch.Value,StringComparison.OrdinalIgnoreCase))) { matches.Add(idMatch.Value); continue; }
                    string name = (currentName ?? Text(e,30005)).Trim();
                    bool duplicate;
                    string resolved=ResolveName(name,catalog,out duplicate);
                    if(resolved!=null) matches.Add(resolved);
                    ambiguous|=duplicate;
                }
                finally { Marshal.ReleaseComObject(e); }
            }
        }
        finally
        {
            if(diagnostic)LastDebug = Program.Json.Serialize(observations);
            if(elements != null) Marshal.ReleaseComObject(elements);
            if(condition != IntPtr.Zero) Marshal.Release(condition);
            if(root != null) Marshal.ReleaseComObject(root);
            Marshal.ReleaseComObject(automation);
        }
        if (matches.Count == 1 && !ambiguous) return matches.First();
        if (ambiguous || matches.Count > 1) throw new InvalidOperationException("会话名称重复，请右键固定一个会话");
        throw new InvalidOperationException("打开侧边栏中的本机会话后显示");
    }
}
