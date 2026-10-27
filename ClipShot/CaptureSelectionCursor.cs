using Microsoft.UI.Input;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace ClipShot;

// Both Win32 and XAML use the same hardware cursor artwork. A drawn crosshair
// would wait for rendering and repeat the lag that the native cursor avoids.
internal sealed class CaptureSelectionCursor : IDisposable
{
    internal nint Handle { get; private set; }
    internal InputCursor XamlCursor { get; }
    private nint previousCursor;
    private bool applied, disposed;
    internal CaptureSelectionCursor(string path)
    {
        previousCursor = GetCursor();
        Handle = Native.LoadCursorFromFile(path);
        if (Handle == 0) throw new Win32Exception();
        try { XamlCursor = FromHandle(Handle); }
        catch { Native.DestroyCursor(Handle); Handle = 0; throw; }
    }
    internal void Apply()
    {
        if (disposed) return;
        Native.SetCursor(Handle); applied = true;
    }
    internal bool IsCurrent
    {
        get
        {
            var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
            return !disposed && GetCursorInfo(ref info) && (info.Flags & 1) != 0 && Matches(info.Handle, Handle);
        }
    }
    internal static nint CurrentHandle => GetCursor();
    internal string Diagnostics
    {
        get
        {
            var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
            bool globalOk = GetCursorInfo(ref info); int error = globalOk ? 0 : Marshal.GetLastWin32Error();
            nint thread = GetCursor();
            return $"ThreadHandle=0x{thread:X}; ExpectedHandle=0x{Handle:X}; ThreadMatches={Matches(thread,Handle)}; " +
                $"GlobalOk={globalOk}; GlobalError={error}; GlobalHandle=0x{info.Handle:X}; Flags=0x{info.Flags:X}; " +
                $"GlobalMatches={globalOk&&Matches(info.Handle,Handle)}; XamlType={XamlCursor.GetType().Name}";
        }
    }
    internal static bool PointerInsideClient(nint window)
    {
        var info = new CursorInfo { Size = Marshal.SizeOf<CursorInfo>() };
        var origin = new Native.Point();
        return GetCursorInfo(ref info) && GetClientRect(window, out var client) && Native.ClientToScreen(window, ref origin) &&
            info.X >= origin.X + client.Left && info.X < (long)origin.X + client.Right &&
            info.Y >= origin.Y + client.Top && info.Y < (long)origin.Y + client.Bottom;
    }
    internal void Restore(bool ownedBeforeDetach = false)
    {
        // Deactivation may already have installed another application's cursor.
        // Only replace our artwork; never change the global cursor scheme.
        if (applied && (ownedBeforeDetach || Matches(GetCursor(), Handle))) Native.SetCursor(previousCursor != 0 ? previousCursor : LoadCursor(0, 32512));
        applied = false;
    }
    public void Dispose()
    {
        if (disposed) return;
        Restore(); disposed = true; XamlCursor.Dispose(); Native.DestroyCursor(Handle); Handle = 0;
    }

    private static InputCursor FromHandle(nint cursor)
    {
        const string runtimeClass = "Microsoft.UI.Input.InputCursor";
        nint className = 0, factory = 0, result = 0;
        try
        {
            Marshal.ThrowExceptionForHR(WindowsCreateString(runtimeClass, runtimeClass.Length, out className));
            // Windows App SDK's Microsoft.UI.Input.InputCursor.Interop.h:
            // IInputCursorStaticsInterop : IInspectable, CreateFromHCursor at slot 6.
            var iid = new Guid("ac6f5065-90c4-46ce-beb7-05e138e54117");
            Marshal.ThrowExceptionForHR(RoGetActivationFactory(className, ref iid, out factory));
            nint method = Marshal.ReadIntPtr(Marshal.ReadIntPtr(factory), 6 * IntPtr.Size);
            var create = Marshal.GetDelegateForFunctionPointer<CreateFromHCursor>(method);
            Marshal.ThrowExceptionForHR(create(factory, cursor, out result));
            // The interop factory copies a non-system HCURSOR; XAML owns its copy.
            return WinRT.MarshalInterface<InputCursor>.FromAbi(result);
        }
        finally
        {
            if (result != 0) Marshal.Release(result);
            if (factory != 0) Marshal.Release(factory);
            if (className != 0) WindowsDeleteString(className);
        }
    }
    private static bool Matches(nint current, nint expected)
    {
        if (current == 0 || expected == 0) return false;
        if (current == expected) return true;
        // XAML owns a copied handle, so handle identity alone is insufficient.
        // This is only used at startup/tests/teardown, never on pointer movement.
        IconInfo left = default, right = default;
        try
        {
            return GetIconInfo(current, out left) && GetIconInfo(expected, out right) && !left.IsIcon && !right.IsIcon &&
                left.XHotspot == right.XHotspot && left.YHotspot == right.YHotspot &&
                SameBitmap(left.Color, right.Color) && SameBitmap(left.Mask, right.Mask);
        }
        finally
        {
            foreach (nint bitmap in new[] { left.Color, left.Mask, right.Color, right.Mask }) if (bitmap != 0) DeleteObject(bitmap);
        }
    }
    private static bool SameBitmap(nint left, nint right)
    {
        if (left == 0 || right == 0) return left == right;
        if (GetObject(left, Marshal.SizeOf<BitmapInfo>(), out var a) == 0 || GetObject(right, Marshal.SizeOf<BitmapInfo>(), out var b) == 0 ||
            a.Width != b.Width || a.Height != b.Height || a.WidthBytes != b.WidthBytes || a.BitsPixel != b.BitsPixel || a.Planes != b.Planes) return false;
        long size = (long)Math.Abs(a.Height) * a.WidthBytes;
        if (size is <= 0 or > 1048576) return false;
        byte[] x = new byte[(int)size], y = new byte[(int)size];
        return GetBitmapBits(left, x.Length, x) == x.Length && GetBitmapBits(right, y.Length, y) == y.Length && x.AsSpan().SequenceEqual(y);
    }
    [StructLayout(LayoutKind.Sequential)] private struct IconInfo
    { [MarshalAs(UnmanagedType.Bool)] public bool IsIcon; public uint XHotspot, YHotspot; public nint Mask, Color; }
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    { public int Type, Width, Height, WidthBytes; public ushort Planes, BitsPixel; public nint Bits; }
    [StructLayout(LayoutKind.Sequential)] private struct CursorInfo
    { public int Size; public uint Flags; public nint Handle; public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct ClientRect
    { public int Left, Top, Right, Bottom; }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] private delegate int CreateFromHCursor(nint factory, nint cursor, out nint result);
    [DllImport("combase.dll", CharSet = CharSet.Unicode)] private static extern int WindowsCreateString(string text, int length, out nint value);
    [DllImport("combase.dll")] private static extern int WindowsDeleteString(nint value);
    [DllImport("combase.dll")] private static extern int RoGetActivationFactory(nint name, ref Guid iid, out nint factory);
    [DllImport("user32.dll")] private static extern nint GetCursor();
    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetCursorInfo(ref CursorInfo info);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetClientRect(nint window, out ClientRect rect);
    [DllImport("user32.dll", EntryPoint = "LoadCursorW")] private static extern nint LoadCursor(nint instance, nint name);
    [DllImport("user32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool GetIconInfo(nint cursor, out IconInfo info);
    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")] private static extern int GetObject(nint bitmap, int size, out BitmapInfo info);
    [DllImport("gdi32.dll")] private static extern int GetBitmapBits(nint bitmap, int size, [Out] byte[] bytes);
    [DllImport("gdi32.dll")] [return: MarshalAs(UnmanagedType.Bool)] private static extern bool DeleteObject(nint value);
}
