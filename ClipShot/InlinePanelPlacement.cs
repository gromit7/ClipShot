namespace ClipShot;

public readonly record struct InlinePanelBounds(int X,int Y,int Width,int Height,bool Overflow);

public static class InlinePanelPlacement
{
    // WorkArea и ширина окна — физические пиксели, размер XAML — DIP.
    // DPI применяется только к содержимому; ручную ширину повторно не масштабируем.
    public static InlinePanelBounds Choose(int width,int preferredY,int workX,int workY,int workWidth,int workHeight,double contentHeight,double scale,int chromeHeight=0)
    {
        if(workWidth<=0||workHeight<=0||width<=0)throw new ArgumentOutOfRangeException(nameof(workWidth));
        if(!double.IsFinite(contentHeight)||contentHeight<0||!double.IsFinite(scale)||scale<=0||chromeHeight<0)throw new ArgumentOutOfRangeException(nameof(contentHeight));
        int actualWidth=Math.Min(width,workWidth);
        double requestedHeight=Math.Ceiling(contentHeight*scale)+chromeHeight;
        int actualHeight=(int)Math.Clamp(requestedHeight,1,workHeight);
        int x=checked((int)((long)workX+workWidth-actualWidth));
        int y=(int)Math.Clamp((long)preferredY,workY,(long)workY+workHeight-actualHeight);
        return new(x,y,actualWidth,actualHeight,requestedHeight>workHeight);
    }
}
