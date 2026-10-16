using System;
using System.Runtime.InteropServices;

// Out-of-context WinEvents execute on the installing UI message-loop thread.
// Only the exact top-level target's native window events reach the hot path.
sealed class WindowTracking : IDisposable
{
    delegate void WinEvent(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time);
    readonly WinEvent callback;
    readonly Action locationChanged,visibilityChanged;
    readonly Action<bool> movingChanged;
    IntPtr foregroundHook,locationHook,moveHook,visibilityHook,target;
    bool disposed;
    DateTime nextRetry;
    internal long PositionEvents;
    internal bool LocationEventsActive { get {return locationHook!=IntPtr.Zero;} }
    internal bool MovingEventsActive { get {return moveHook!=IntPtr.Zero;} }
    internal bool VisibilityEventsActive { get {return visibilityHook!=IntPtr.Zero;} }
    internal bool ForegroundEventsActive { get {return foregroundHook!=IntPtr.Zero;} }
    internal WindowTracking(Action location,Action<bool> moving,Action visibility)
    {
        locationChanged=location;movingChanged=moving;visibilityChanged=visibility;
        callback=OnEvent; // Strong reference retained until all hooks are removed.
        foregroundHook=SetWinEventHook(3,3,IntPtr.Zero,callback,0,0,0);
    }
    internal void Attach(IntPtr window)
    {
        if(disposed || (window==target && (locationHook!=IntPtr.Zero || DateTime.UtcNow<nextRetry)))return;
        Release(ref locationHook);Release(ref moveHook);Release(ref visibilityHook);
        target=window;
        nextRetry=DateTime.UtcNow.AddSeconds(5);
        if(target==IntPtr.Zero)return;
        uint pid;Native.GetWindowThreadProcessId(target,out pid);
        if(pid==0)return;
        locationHook=SetWinEventHook(0x800B,0x800B,IntPtr.Zero,callback,pid,0,0);
        moveHook=SetWinEventHook(0x000A,0x000B,IntPtr.Zero,callback,pid,0,0);
        visibilityHook=SetWinEventHook(0x8001,0x8003,IntPtr.Zero,callback,pid,0,0);
    }
    void OnEvent(IntPtr hook,uint kind,IntPtr window,int objectId,int childId,uint thread,uint time)
    {
        if(disposed)return;
        try
        {
            if(kind==3){visibilityChanged();return;}
            if(window!=target || objectId!=0 || childId!=0)return;
            if(kind==0x800B){PositionEvents++;locationChanged();}
            else if(kind==0x000A)movingChanged(true);
            else if(kind==0x000B)movingChanged(false);
            else visibilityChanged();
        }
        catch { /* Never allow managed exceptions to cross the native callback. */ }
    }
    static void Release(ref IntPtr hook)
    {
        if(hook==IntPtr.Zero)return;
        UnhookWinEvent(hook);hook=IntPtr.Zero;
    }
    public void Dispose()
    {
        if(disposed)return;
        disposed=true;
        Release(ref locationHook);Release(ref moveHook);Release(ref visibilityHook);Release(ref foregroundHook);
        GC.KeepAlive(callback);
    }
    [DllImport("user32.dll")] static extern IntPtr SetWinEventHook(uint first,uint last,IntPtr module,WinEvent callback,uint process,uint thread,uint flags);
    [DllImport("user32.dll")] static extern bool UnhookWinEvent(IntPtr hook);
}
