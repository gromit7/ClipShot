using System.Drawing;
using System.Drawing.Drawing2D;
namespace ClipShot;
internal static class TrayIconRenderer
{
    internal static Bitmap Render(string path,MicrophoneState state)
    {
        string sourcePath=state==MicrophoneState.Muted?Path.Combine(Path.GetDirectoryName(path)!,"ClipShot.Muted.ico"):path;
        using var source=new Icon(sourcePath,32,32);var bitmap=source.ToBitmap();
        if(state==MicrophoneState.Unavailable)
        {
            for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
            {var pixel=bitmap.GetPixel(x,y);int grey=(int)Math.Round(pixel.R*.2126+pixel.G*.7152+pixel.B*.0722);bitmap.SetPixel(x,y,Color.FromArgb(pixel.A,grey,grey,grey));}
        }
        if(state==MicrophoneState.Mixed)
        {
            using var graphics=Graphics.FromImage(bitmap);graphics.SmoothingMode=SmoothingMode.AntiAlias;
            using var outline=new Pen(Color.FromArgb(20,23,31),2);using var amber=new SolidBrush(Color.FromArgb(255,202,76));
            graphics.FillEllipse(amber,20,20,11,11);graphics.DrawEllipse(outline,20,20,11,11);
        }
        return bitmap;
    }
}
