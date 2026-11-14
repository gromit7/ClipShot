using System.Runtime.InteropServices;
namespace ClipShot;
internal static class CaptureSound
{
    [DllImport("winmm.dll",EntryPoint="PlaySoundW",CharSet=CharSet.Unicode)]
    [return:MarshalAs(UnmanagedType.Bool)]private static extern bool PlaySound(string path,nint module,uint flags);
    internal static void Play()
    {
        string path=Path.Combine(AppContext.BaseDirectory,"Assets","Shutter.wav");
        if(File.Exists(path))PlaySound(path,0,0x20013); // asynchronous, no system fallback sound
    }
}
