using System.Runtime.InteropServices;
namespace ClipShot;

// Install only after an explicit button choice. The hook never performs COM or disk work.
internal sealed class MicrophoneMouse : IDisposable
{
    private readonly Hook callback;private nint hook;private readonly int button;private readonly Action toggle;
    internal MicrophoneMouse(int button,Action toggle)
    {
        this.button=button;this.toggle=toggle;callback=Process;
        hook=SetWindowsHookEx(14,callback,GetModuleHandle(null),0);
        if(hook==0)throw new System.ComponentModel.Win32Exception();
    }
    private nint Process(int code,nint message,nint pointer)
    {
        if(code>=0&&((int)message==0x020B||(int)message==0x020C))
        {
            var data=Marshal.PtrToStructure<MouseData>(pointer);
            if((data.Flags&1)==0&&(data.Data>>16)==button)
            {if((int)message==0x020C)toggle();return 1;}
        }
        return CallNextHookEx(hook,code,message,pointer);
    }
    public void Dispose(){if(hook!=0){UnhookWindowsHookEx(hook);hook=0;}GC.KeepAlive(callback);}
    private delegate nint Hook(int code,nint message,nint pointer);
    [StructLayout(LayoutKind.Sequential)]private struct MouseData{public int X,Y;public uint Data,Flags,Time;public nuint Extra;}
    [DllImport("user32.dll",SetLastError=true)]private static extern nint SetWindowsHookEx(int id,Hook callback,nint module,uint thread);
    [DllImport("user32.dll")]private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")]private static extern nint CallNextHookEx(nint hook,int code,nint message,nint pointer);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]private static extern nint GetModuleHandle(string? name);
}
