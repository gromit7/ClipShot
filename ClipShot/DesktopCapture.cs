using System.ComponentModel;
using System.Runtime.InteropServices;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipShot;
internal static class DesktopCapture
{
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern nint GetDC(nint hwnd);
    [DllImport("user32.dll")] private static extern int ReleaseDC(nint hwnd,nint dc);
    [DllImport("gdi32.dll")] private static extern nint CreateCompatibleDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteDC(nint dc);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(nint obj);
    [DllImport("gdi32.dll")] private static extern nint SelectObject(nint dc,nint obj);
    [DllImport("gdi32.dll")] private static extern bool GdiFlush();
    [DllImport("gdi32.dll",SetLastError=true)] private static extern bool BitBlt(nint dest,int x,int y,int width,int height,nint source,int sx,int sy,uint op);
    [DllImport("gdi32.dll",SetLastError=true)] private static extern nint CreateDIBSection(nint dc,ref BitmapInfo info,uint usage,out nint bits,nint section,uint offset);
    [DllImport("dwmapi.dll")] internal static extern int DwmFlush();
    [StructLayout(LayoutKind.Sequential)] private struct BitmapInfo
    {
        public uint Size;public int Width,Height;public ushort Planes,BitCount;public uint Compression,ImageSize;public int XPels,YPels;public uint Used,Important;
    }
    public static ScreenFrame ReadDesktop()
    {
        int x=GetSystemMetrics(76),y=GetSystemMetrics(77),width=GetSystemMetrics(78),height=GetSystemMetrics(79);
        int length=checked(width*height*4);if(width<=0||height<=0||length>512*1024*1024)throw new InvalidOperationException("Размер рабочего стола не поддерживается.");
        nint screen=GetDC(0),memory=0,bitmap=0,previous=0;
        try
        {
            if(screen==0)throw new Win32Exception();
            memory=CreateCompatibleDC(screen);if(memory==0)throw new Win32Exception();
            var info=new BitmapInfo{Size=(uint)Marshal.SizeOf<BitmapInfo>(),Width=width,Height=-height,Planes=1,BitCount=32};
            bitmap=CreateDIBSection(screen,ref info,0,out nint bits,0,0);if(bitmap==0)throw new Win32Exception();
            previous=SelectObject(memory,bitmap);
            if(!BitBlt(memory,0,0,width,height,screen,x,y,0x40CC0020))throw new Win32Exception();
            GdiFlush();
            byte[] pixels=new byte[length];Marshal.Copy(bits,pixels,0,length);
            for(int i=3;i<pixels.Length;i+=4)pixels[i]=255;
            return new(x,y,width,height,pixels);
        }
        finally
        {
            if(previous!=0&&previous!=(nint)(-1))SelectObject(memory,previous);
            if(bitmap!=0)DeleteObject(bitmap);if(memory!=0)DeleteDC(memory);if(screen!=0)ReleaseDC(0,screen);
        }
    }
    public static async Task<byte[]> Png(ScreenFrame frame)
    {
        using var stream=new InMemoryRandomAccessStream();var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);
        encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,(uint)frame.Width,(uint)frame.Height,96,96,frame.Pixels);await encoder.FlushAsync();stream.Seek(0);
        byte[] png=new byte[(int)stream.Size];using var reader=new DataReader(stream);await reader.LoadAsync((uint)png.Length);reader.ReadBytes(png);return png;
    }
}
