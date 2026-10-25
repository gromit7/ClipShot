using System.Runtime.InteropServices;
namespace ClipShot;
internal static class Native
{
    internal delegate nint WndProc(nint hwnd,uint msg,nint w,nint l);
    [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW")] internal static extern nint SetWindowLongPtr(nint h,int index,nint value);
    [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW")] internal static extern nint GetWindowLongPtr(nint h,int index);
    [DllImport("user32.dll")] internal static extern bool SetWindowPos(nint h,nint after,int x,int y,int width,int height,uint flags);
    [DllImport("user32.dll")] internal static extern nint CallWindowProc(nint proc,nint h,uint m,nint w,nint l);
    [DllImport("user32.dll")] internal static extern bool AddClipboardFormatListener(nint h);
    [DllImport("user32.dll")] internal static extern bool RemoveClipboardFormatListener(nint h);
    [DllImport("user32.dll")] internal static extern uint GetClipboardSequenceNumber();
    [DllImport("user32.dll",SetLastError=true)] internal static extern bool RegisterHotKey(nint h,int id,uint modifiers,uint key);
    [DllImport("user32.dll")] internal static extern bool UnregisterHotKey(nint h,int id);
    [DllImport("user32.dll")] internal static extern bool GetCursorPos(out Point p);
    [DllImport("user32.dll")] internal static extern uint GetDpiForWindow(nint h);
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(nint h);
    [DllImport("user32.dll")] internal static extern nint LoadIcon(nint instance,nint name);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern nint LoadImage(nint instance,string name,uint type,int cx,int cy,uint flags);
    [DllImport("user32.dll")] internal static extern bool DestroyIcon(nint icon);
    [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)] internal static extern nint LoadCursorFromFile(string path);
    [DllImport("user32.dll")] internal static extern nint SetCursor(nint cursor);
    [DllImport("user32.dll")] internal static extern bool DestroyCursor(nint cursor);
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] internal static extern bool PostMessage(nint hwnd,uint msg,nint w,nint l);
    [DllImport("user32.dll")] internal static extern nint CreatePopupMenu();
    [DllImport("user32.dll",CharSet=CharSet.Unicode)] internal static extern bool AppendMenu(nint menu,uint flags,nuint id,string text);
    [DllImport("user32.dll")] internal static extern int TrackPopupMenu(nint menu,uint flags,int x,int y,int reserved,nint hwnd,nint rect);
    [DllImport("user32.dll")] internal static extern bool DestroyMenu(nint menu);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)] internal static extern bool Shell_NotifyIcon(uint operation,ref NotifyIcon data);
    [StructLayout(LayoutKind.Sequential)] internal struct Point { public int X,Y; }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] internal struct NotifyIcon
    {
        public uint Size; public nint Hwnd;public uint Id,Flags,Callback;public nint Icon;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
        public uint State,StateMask;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;
        public uint Timeout;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;
        public uint InfoFlags;public Guid Guid;public nint BalloonIcon;
    }
}
