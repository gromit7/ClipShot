namespace ClipShot;
public readonly record struct PanelBounds(int X,int Y,int Width,int Height,bool OnRight);
public static class SidePanelPlacement
{
    // Все размеры здесь физические. Длинному списку отдаём рабочую высоту
    // монитора вместо прежнего потолка; короткий список остаётся компактным.
    public static int HeightForContent(double contentHeight,int workHeight,double moderateHeight,int inset=8)
    {
        if(!double.IsFinite(contentHeight)||contentHeight<0||!double.IsFinite(moderateHeight)||moderateHeight<=0||workHeight<=0||inset<0)throw new ArgumentOutOfRangeException(nameof(contentHeight));
        int available=(int)Math.Max(1,(long)workHeight-2L*inset);
        return contentHeight>moderateHeight?available:(int)Math.Clamp(Math.Ceiling(contentHeight),1,available);
    }
    public static PanelBounds Choose(int x,int y,int width,int height,int workX,int workY,int workWidth,int workHeight,int desiredWidth,int desiredHeight,int anchorY,int gap=8)
    {
        int left=Math.Max(0,x-workX-gap),right=Math.Max(0,workX+workWidth-x-width-gap);bool onRight=right>=left;
        int available=Math.Max(left,right),panelWidth=Math.Min(desiredWidth,Math.Max(1,workWidth-16));
        if(available>=200)panelWidth=Math.Min(panelWidth,available);
        int panelHeight=Math.Min(desiredHeight,Math.Max(1,workHeight-16));
        int panelX=onRight?x+width+gap:x-gap-panelWidth;
        panelX=Math.Clamp(panelX,workX+8,Math.Max(workX+8,workX+workWidth-panelWidth-8));
        int panelY=Math.Clamp(anchorY,workY+8,Math.Max(workY+8,workY+workHeight-panelHeight-8));
        return new(panelX,panelY,panelWidth,panelHeight,onRight);
    }
}
