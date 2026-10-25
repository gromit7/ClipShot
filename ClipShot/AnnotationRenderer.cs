using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ClipShot;
public static class AnnotationRenderer
{
    // Works only on the supplied image; never reads the desktop or clipboard.
    public static ScreenFrame Render(ScreenFrame source,IEnumerable<Annotation> annotations)
    {
        if(source.Width<1||source.Height<1||(long)source.Width*source.Height*4!=source.Pixels.LongLength)throw new ArgumentException("Invalid image.",nameof(source));
        using var bitmap=new Bitmap(source.Width,source.Height,PixelFormat.Format32bppArgb);
        var bounds=new Rectangle(0,0,source.Width,source.Height);
        var locked=bitmap.LockBits(bounds,ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<source.Height;y++)Marshal.Copy(source.Pixels,y*source.Width*4,locked.Scan0+y*locked.Stride,source.Width*4);}finally{bitmap.UnlockBits(locked);}
        foreach(var mark in annotations)
        {
            if(mark.Tool==MarkTool.Blur){Blur(bitmap,mark);continue;}
            using var g=Graphics.FromImage(bitmap);g.SmoothingMode=SmoothingMode.AntiAlias;g.TextRenderingHint=System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;
            var color=ColorTranslator.FromHtml(mark.Color);using var pen=new Pen(color,Math.Max(1,mark.Width)){StartCap=LineCap.Round,EndCap=LineCap.Round};using var brush=new SolidBrush(color);
            float x=(float)Math.Min(mark.Start.X,mark.End.X),y=(float)Math.Min(mark.Start.Y,mark.End.Y),w=(float)Math.Abs(mark.End.X-mark.Start.X),h=(float)Math.Abs(mark.End.Y-mark.Start.Y);
            switch(mark.Tool)
            {
                case MarkTool.Arrow:
                    g.DrawLine(pen,(float)mark.Start.X,(float)mark.Start.Y,(float)mark.End.X,(float)mark.End.Y);
                    g.FillPolygon(brush,AnnotationGeometry.ArrowHead(mark).Select(p=>new PointF((float)p.X,(float)p.Y)).ToArray());break;
                case MarkTool.Rectangle: g.DrawRectangle(pen,x,y,w,h);break;
                case MarkTool.Redact: using(var opaque=new SolidBrush(Color.FromArgb(255,32,32,32)))g.FillRectangle(opaque,x,y,w,h);break;
                case MarkTool.Text:
                    using(var font=new Font("Segoe UI",Math.Max(16,mark.Width*6),FontStyle.Bold,GraphicsUnit.Pixel))g.DrawString(mark.Text,font,brush,new RectangleF((float)mark.Start.X,(float)mark.Start.Y,Math.Max(1,source.Width-(float)mark.Start.X),Math.Max(1,source.Height-(float)mark.Start.Y)));break;
            }
        }
        byte[] pixels=new byte[source.Pixels.Length];locked=bitmap.LockBits(bounds,ImageLockMode.ReadOnly,PixelFormat.Format32bppArgb);
        try{for(int y=0;y<source.Height;y++)Marshal.Copy(locked.Scan0+y*locked.Stride,pixels,y*source.Width*4,source.Width*4);}finally{bitmap.UnlockBits(locked);}
        return source with{Pixels=pixels};
    }
    private static void Blur(Bitmap bitmap,Annotation mark)
    {
        int left=Math.Clamp((int)Math.Floor(Math.Min(mark.Start.X,mark.End.X)),0,bitmap.Width-1),top=Math.Clamp((int)Math.Floor(Math.Min(mark.Start.Y,mark.End.Y)),0,bitmap.Height-1);
        int right=Math.Clamp((int)Math.Ceiling(Math.Max(mark.Start.X,mark.End.X)),left+1,bitmap.Width),bottom=Math.Clamp((int)Math.Ceiling(Math.Max(mark.Start.Y,mark.End.Y)),top+1,bitmap.Height);
        // Deliberately strong mosaic. Solid 'Hide' is available for confidential data.
        var rect=new Rectangle(left,top,right-left,bottom-top);using var small=new Bitmap(Math.Max(1,rect.Width/14),Math.Max(1,rect.Height/14),PixelFormat.Format32bppArgb);
        using(var g=Graphics.FromImage(small)){g.InterpolationMode=InterpolationMode.HighQualityBilinear;g.DrawImage(bitmap,new Rectangle(0,0,small.Width,small.Height),rect,GraphicsUnit.Pixel);}
        using(var g=Graphics.FromImage(bitmap)){g.InterpolationMode=InterpolationMode.NearestNeighbor;g.PixelOffsetMode=PixelOffsetMode.Half;g.DrawImage(small,rect,new Rectangle(0,0,small.Width,small.Height),GraphicsUnit.Pixel);}
    }
}
