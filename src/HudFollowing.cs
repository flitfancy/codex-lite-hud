using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

// Native position events only adjust geometry; slower discovery and data work
// run from the idle refresh path after motion has settled.

sealed partial class Hud
{
    bool InMotion {get{return targetMoving || DateTime.UtcNow-lastMotion<TimeSpan.FromMilliseconds(140);}}
    void QueueFollow()
    {
        if(followQueued || IsDisposed || !IsHandleCreated)return;
        followQueued=true;
        BeginInvoke(new Action(delegate {followQueued=false;if(!IsDisposed)Follow();}));
    }
    void TargetMoving(bool moving)
    {
        targetMoving=moving;
        if(!moving){lastMotion=DateTime.MinValue;lastRead=DateTime.MinValue;lastTheme=DateTime.MinValue;QueueFollow();}
    }
    void AttachTracking()
    {
        if(tracking==null)return;
        tracking.Attach(codex);
        follow.Interval=tracking.LocationEventsActive ? 200 : 40;
    }
    void RecoverMotion()
    {
        if(!targetMoving)return;
        var state=new Native.GUITHREADINFO {cbSize=(uint)Marshal.SizeOf(typeof(Native.GUITHREADINFO))};
        uint pid;uint thread=Native.GetWindowThreadProcessId(codex,out pid);
        if(thread==0 || !Native.GetGUIThreadInfo(thread,ref state) || (state.flags&2)==0)
        {targetMoving=false;lastMotion=DateTime.MinValue;lastRead=DateTime.MinValue;}
    }
    bool ReadBounds(bool immediate)
    {
        Native.RECT raw;
        if(!Native.GetWindowRect(codex,out raw))return false;
        int w=raw.Right-raw.Left,h=raw.Bottom-raw.Top;
        uint dpi=Native.GetDpiForWindow(codex);
        if(!immediate || geometryTarget!=codex || geometryWidth!=w || geometryHeight!=h || geometryDpi!=dpi)
        {
            Native.RECT visual;
            bool prior=geometryTarget==codex && geometryDpi==dpi;
            if(Native.DwmGetWindowAttribute(codex,9,out visual,Marshal.SizeOf(typeof(Native.RECT)))==0)
            {
                var candidate=new Native.RECT {Left=visual.Left-raw.Left,Top=visual.Top-raw.Top,Right=raw.Right-visual.Right,Bottom=raw.Bottom-visual.Bottom};
                int maximum=(int)Math.Ceiling(32*Math.Max(1,dpi/96d));
                bool valid=candidate.Left>=0 && candidate.Top>=0 && candidate.Right>=0 && candidate.Bottom>=0 && candidate.Left<=maximum && candidate.Top<=maximum && candidate.Right<=maximum && candidate.Bottom<=maximum;
                if(valid)frameInsets=candidate;
                else if(!prior)frameInsets=new Native.RECT();
            }
            else if(!prior)frameInsets=new Native.RECT();
            geometryTarget=codex;geometryWidth=w;geometryHeight=h;geometryDpi=dpi;
        }
        bounds=new Native.RECT {Left=raw.Left+frameInsets.Left,Top=raw.Top+frameInsets.Top,Right=raw.Right-frameInsets.Right,Bottom=raw.Bottom-frameInsets.Bottom};
        return true;
    }
    void MoveToDock()
    {
        int x=dockLeft,y=Math.Max(minimumTop,Math.Min(dockBottom-Height-footerGap,dockBottom-Height-effectiveGap));
        if(!positionLocked)
        {
            x=bounds.Left+Px(freeX);y=bounds.Top+Px(freeY);
            var area=Screen.FromRectangle(new Rectangle(x,y,Width,Height)).WorkingArea;
            x=Math.Max(area.Left,Math.Min(area.Right-Width,x));y=Math.Max(area.Top,Math.Min(area.Bottom-Height,y));
        }
        if(Left!=x || Top!=y)
            Native.SetWindowPos(Handle,IntPtr.Zero,x,y,0,0,0x0010|0x0004|0x0200|0x0001);
    }
    void FollowPosition()
    {
        if(IsDisposed || dragging || !Visible || codex==IntPtr.Zero)return;
        lastMotion=DateTime.UtcNow;
        if(positionUpdating){positionPending=true;return;}
        positionUpdating=true;
        try
        {
            do
            {
                positionPending=false;
                if(!Native.IsIconic(codex) && ReadBounds(true)){UpdateDock();if(dockFits)MoveToDock();}
            }while(positionPending && !IsDisposed);
        }
        finally{positionUpdating=false;}
    }

    static readonly Dictionary<uint, bool> KnownProcesses = new Dictionary<uint, bool>();
    static bool IsMainWindow(IntPtr window)
    {
        if(window==IntPtr.Zero || !Native.IsWindow(window) || !Native.IsWindowVisible(window) || Native.IsIconic(window) || Native.GetWindow(window,4)!=IntPtr.Zero)return false;
        // Codex pets share the main process and can have a full-monitor
        // transparent surface. Process identity and size alone are insufficient.
        long extended=Native.GetWindowLongPtr(window,-20).ToInt64();
        if((extended&(0x80|0x20))!=0 || (Native.GetWindowLongPtr(window,-16).ToInt64()&0x40000000)!=0)return false;
        Native.RECT rect;
        if(!Native.GetWindowRect(window,out rect))return false;
        double scale=Math.Max(1,Native.GetDpiForWindow(window)/96d);
        return rect.Right-rect.Left>=300*scale && rect.Bottom-rect.Top>=250*scale;
    }
    void DiscoverWindow()
    {
        if(DateTime.UtcNow-lastDiscovery<TimeSpan.FromSeconds(2))return;
        lastDiscovery=DateTime.UtcNow;
        IntPtr found=IntPtr.Zero;
        Native.EnumWindows(delegate(IntPtr window,IntPtr unused)
        {
            uint pid;Native.GetWindowThreadProcessId(window,out pid);
            if(!IsCodex(pid) || !IsMainWindow(window))return true;
            found=window;return false;
        },IntPtr.Zero);
        if(found!=IntPtr.Zero)codex=found;
    }
    void SyncStacking()
    {
        if(preview || !Visible || codex==IntPtr.Zero)return;
        var above=Native.GetWindow(codex,3);
        if(above==Handle)return;
        if(above!=IntPtr.Zero && (Native.GetWindowLongPtr(above,-20).ToInt64()&8)!=0)above=IntPtr.Zero;
        Native.SetWindowPos(Handle,above,0,0,0,0,0x0010|0x0200|0x0001|0x0002);
    }
    static bool IsCodex(uint id)
    {
        bool known;
        if (KnownProcesses.TryGetValue(id, out known)) return known;
        try
        {
            using (var p = Process.GetProcessById((int)id))
            {
                string path = p.MainModule.FileName;
                known = p.ProcessName.Equals("ChatGPT", StringComparison.OrdinalIgnoreCase) &&
                    (path.IndexOf("OpenAI.Codex_", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("\\OpenAI\\Codex\\", StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }
        catch { known = false; }
        if (KnownProcesses.Count > 100) KnownProcesses.Clear();
        KnownProcesses[id] = known;
        return known;
    }
    void ConsiderForeground(IntPtr foreground,uint pid)
    {
        if(pid==ownPid || !IsCodex(pid))return;
        var target=Native.GetAncestor(foreground,2);
        if(!IsMainWindow(target))return;
        if(target!=codex){codex=target;targetMoving=false;lastMotion=DateTime.MinValue;}
    }

    void Follow()
    {
        if (preview)
        {
            if(codex == IntPtr.Zero)DiscoverWindow();
            AttachTracking();
            if(codex!=IntPtr.Zero && ReadBounds(InMotion))
            {
                UpdateDock();if(!dragging)MoveToDock();
            }
            else if(!placed)
            {
                var area=Screen.PrimaryScreen.WorkingArea;
                Location=new Point(area.Left+(area.Width-Width)/2,area.Top+(area.Height-Height)/2);
            }
            if(!placed || !Visible){Opacity=1;Show();placed=true;}
            RefreshIdle();
            return;
        }
        var foreground = Native.GetForegroundWindow();
        uint pid; Native.GetWindowThreadProcessId(foreground, out pid);
        ConsiderForeground(foreground,pid);
        if(codex==IntPtr.Zero || !Native.IsWindow(codex) || !Native.IsWindowVisible(codex))DiscoverWindow();
        AttachTracking();
        bool show = codex != IntPtr.Zero && Native.IsWindow(codex) && Native.IsWindowVisible(codex) && !Native.IsIconic(codex);
        if (!show)
        {
            targetMoving=false;lastMotion=DateTime.MinValue;
            if (Visible) { Hide(); WriteStatus(false); }
            return;
        }
        if (!ReadBounds(InMotion)) return;
        UpdateDock();
        if(!dockFits){Hide();return;}
        if (bounds.Right - bounds.Left < Width + 20 || bounds.Bottom - bounds.Top < Height + 20) { Hide(); return; }
        if (!dragging)MoveToDock();
        if (!Visible || !placed) { Opacity = 1; Show(); placed = true; WriteStatus(true); }
        SyncStacking();
        RefreshIdle();
    }
}
