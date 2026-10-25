namespace ClipShot;

public readonly record struct PixelRegion(int X,int Y,int Width,int Height)
{
    public static PixelRegion FromDrag(int ax,int ay,int bx,int by,int width,int height)
    {
        int left=Math.Clamp(Math.Min(ax,bx),0,width),top=Math.Clamp(Math.Min(ay,by),0,height);
        int right=Math.Clamp(Math.Max(ax,bx),0,width),bottom=Math.Clamp(Math.Max(ay,by),0,height);
        return new(left,top,right-left,bottom-top);
    }
}
public sealed record ScreenFrame(int X,int Y,int Width,int Height,byte[] Pixels)
{
    public ScreenFrame Crop(PixelRegion r)
    {
        if(r.Width<1||r.Height<1||r.X<0||r.Y<0||(long)r.X+r.Width>Width||(long)r.Y+r.Height>Height)throw new ArgumentOutOfRangeException(nameof(r));
        if(Width<=0||Height<=0||(long)Width*Height*4!=Pixels.LongLength)throw new ArgumentException("Invalid BGRA frame.",nameof(Pixels));
        byte[] pixels=new byte[checked(r.Width*r.Height*4)];
        for(int y=0;y<r.Height;y++)Buffer.BlockCopy(Pixels,((r.Y+y)*Width+r.X)*4,pixels,y*r.Width*4,r.Width*4);
        return new(X+r.X,Y+r.Y,r.Width,r.Height,pixels);
    }
}
