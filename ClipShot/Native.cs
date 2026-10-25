using System.Runtime.InteropServices;
namespace ClipShot;
internal static class Native
{
    [DllImport("user32.dll")]internal static extern bool ReleaseCapture();
    [DllImport("user32.dll")]internal static extern nint SendMessage(nint window,uint message,nint w,nint l);
    [DllImport("user32.dll")]internal static extern bool ClientToScreen(nint window,ref Point point);
    [DllImport("user32.dll")]internal static extern bool SetLayeredWindowAttributes(nint window,uint colour,byte alpha,uint flags);
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
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] internal static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetAncestor(nint window,uint flags);
    [DllImport("user32.dll")] internal static extern bool IsWindow(nint window);
    [DllImport("user32.dll")] internal static extern bool IsIconic(nint window);
    [DllImport("user32.dll")] internal static extern uint GetWindowThreadProcessId(nint window,out uint process);
    [DllImport("user32.dll")] internal static extern bool GetGUIThreadInfo(uint thread,ref GuiThreadInfo info);
    [DllImport("user32.dll",SetLastError=true)] internal static extern uint SendInput(uint count,[In] KeyboardInput[] inputs,int size);
    [StructLayout(LayoutKind.Sequential)] internal struct GuiThreadInfo {public uint Size,Flags;public nint Active,Focus,Capture,MenuOwner,MoveSize,Caret;public int Left,Top,Right,Bottom;}
    [StructLayout(LayoutKind.Explicit,Size=40)] internal struct KeyboardInput
    {
        [FieldOffset(0)]public uint Type;[FieldOffset(8)]public ushort Key;[FieldOffset(10)]public ushort Scan;
        [FieldOffset(12)]public uint Flags;[FieldOffset(16)]public uint Time;[FieldOffset(24)]public nint Extra;
    }
    internal static KeyboardInput KeyInput(ushort key,bool up)=>new(){Type=1,Key=key,Flags=up?2u:0u};
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
