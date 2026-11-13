using System.Drawing;
using System.Drawing.Drawing2D;
namespace ClipShot;
internal static class TrayIconRenderer
{
    internal static Bitmap Render(MicrophoneState state)
    {
        var bitmap=new Bitmap(32,32,System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics=Graphics.FromImage(bitmap);graphics.SmoothingMode=SmoothingMode.AntiAlias;
        using var blue=new SolidBrush(Color.FromArgb(0,112,244));
        using var edge=new Pen(Color.White,1.5f);
        graphics.FillRectangle(blue,5,5,22,25);graphics.DrawRectangle(edge,5,5,22,25);
        using var white=new SolidBrush(Color.White);
        graphics.FillRectangle(white,11,2,10,6);
        using var line=new Pen(Color.White,2.5f){StartCap=LineCap.Round,EndCap=LineCap.Round};
        graphics.DrawLine(line,10,13,22,13);graphics.DrawLine(line,10,19,22,19);graphics.DrawLine(line,10,25,17,25);
        // Clipboard is primary. Only the small corner marker reflects microphone state.
        var colour=state switch{MicrophoneState.Enabled=>Color.FromArgb(0,224,118),MicrophoneState.Muted=>Color.FromArgb(255,54,78),MicrophoneState.Mixed=>Color.FromArgb(255,192,0),_=>Color.FromArgb(167,178,194)};
        using var badge=new SolidBrush(colour);using var ring=new Pen(Color.FromArgb(20,27,40),1.5f);
        graphics.FillEllipse(badge,21,21,10,10);graphics.DrawEllipse(ring,21,21,10,10);
        using var marker=new Pen(Color.FromArgb(20,27,40),2){StartCap=LineCap.Round,EndCap=LineCap.Round};
        if(state==MicrophoneState.Muted)graphics.DrawLine(marker,24,28,28,24);
        else if(state==MicrophoneState.Mixed)graphics.DrawLine(marker,24,26,28,26);
        return bitmap;
    }
}
