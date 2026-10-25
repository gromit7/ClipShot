using System.Drawing;
namespace ClipShot;
internal static class TrayIconRenderer
{
    internal static Bitmap Render(string path,MicrophoneState state)
    {
        using var source=new Icon(path,32,32);var bitmap=source.ToBitmap();
        var color=state switch{MicrophoneState.Enabled=>Color.FromArgb(67,224,167),MicrophoneState.Muted=>Color.FromArgb(255,82,119),MicrophoneState.Mixed=>Color.FromArgb(255,202,76),_=>Color.FromArgb(150,160,178)};
        for(int y=0;y<bitmap.Height;y++)for(int x=0;x<bitmap.Width;x++)
        {
            var pixel=bitmap.GetPixel(x,y);if(pixel.A==0)continue;
            // Recolour the blue disc, retaining the white clipboard and its antialiased edges.
            double amount=Math.Clamp((Math.Max(pixel.R,Math.Max(pixel.G,pixel.B))-Math.Min(pixel.R,Math.Min(pixel.G,pixel.B)))/100d,0,1);
            int Mix(int original,int tint)=>(int)Math.Round(original*(1-amount)+tint*amount);
            bitmap.SetPixel(x,y,Color.FromArgb(pixel.A,Mix(pixel.R,color.R),Mix(pixel.G,color.G),Mix(pixel.B,color.B)));
        }
        return bitmap;
    }
}
