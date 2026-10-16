using System;
using System.Drawing;
using System.Runtime.InteropServices;

static class Native
{
    const int DwmVisibleFrameBorderThickness=37;
    [StructLayout(LayoutKind.Sequential)] internal struct RECT { internal int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] internal struct GUITHREADINFO
    {
        internal uint cbSize,flags;
        internal IntPtr active,focus,capture,menuOwner,moveSize,caret;
        internal RECT caretRect;
    }
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GUITHREADINFO state);
    [DllImport("user32.dll")] internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);
    [DllImport("user32.dll")] internal static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern IntPtr WindowFromPoint(Point point);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] internal static extern IntPtr GetAncestor(IntPtr window, uint flags);
    [DllImport("user32.dll")] internal static extern bool GetWindowRect(IntPtr window, out RECT rect);
    [DllImport("user32.dll")] internal static extern bool IsIconic(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsZoomed(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindowVisible(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool IsWindow(IntPtr window);
    internal delegate bool WindowEnum(IntPtr window,IntPtr unused);
    [DllImport("user32.dll")] internal static extern bool EnumWindows(WindowEnum callback,IntPtr unused);
    [DllImport("user32.dll")] internal static extern IntPtr GetWindow(IntPtr window,uint relation);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern IntPtr GetWindowLongPtr(IntPtr window,int index);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(IntPtr window);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(IntPtr window, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("dwmapi.dll")] internal static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out RECT rect, int size);
    [DllImport("dwmapi.dll",EntryPoint="DwmGetWindowAttribute")] static extern int DwmGetBorder(IntPtr window,int attribute,out uint value,int size);
    internal static int VisibleBorder(IntPtr window)
    {
        if(window==IntPtr.Zero || IsZoomed(window))return 0;
        uint value;return DwmGetBorder(window,DwmVisibleFrameBorderThickness,out value,4)==0 && value<=8?(int)value:0;
    }
}
