namespace ClipShot;

public enum MarkTool { Arrow, Rectangle, Text, Blur, Redact }
public readonly record struct ImagePoint(double X,double Y)
{
    public ImagePoint Clamp(int width,int height)=>new(Math.Clamp(X,0,width-1),Math.Clamp(Y,0,height-1));
}
public sealed record Annotation(MarkTool Tool,ImagePoint Start,ImagePoint End,string Text="",string Color="#E53935",float Width=4);
public sealed class AnnotationHistory
{
    private readonly List<Annotation> marks=[];
    private readonly Stack<Annotation> redo=[];
    public IReadOnlyList<Annotation> Marks=>marks.AsReadOnly();
    public bool CanUndo=>marks.Count>0;
    public bool CanRedo=>redo.Count>0;
    public void Add(Annotation mark){marks.Add(mark);redo.Clear();}
    public void Undo(){if(marks.Count>0){redo.Push(marks[^1]);marks.RemoveAt(marks.Count-1);}}
    public void Redo(){if(redo.TryPop(out var mark))marks.Add(mark);}
}
public static class AnnotationGeometry
{
    public static ImagePoint SnapArrow(ImagePoint start,ImagePoint end,bool force=false)
    {
        double dx=end.X-start.X,dy=end.Y-start.Y,length=Math.Sqrt(dx*dx+dy*dy);
        if(length<1)return end;
        double angle=Math.Atan2(dy,dx),snapped=Math.Round(angle/(Math.PI/4))*(Math.PI/4);
        if(!force&&Math.Abs(angle-snapped)>Math.PI/30)return end;
        // Preserve the projected length so snapping never extends outside a drag's reach.
        double projected=dx*Math.Cos(snapped)+dy*Math.Sin(snapped);
        return new(start.X+projected*Math.Cos(snapped),start.Y+projected*Math.Sin(snapped));
    }
    public static ImagePoint[] ArrowHead(Annotation mark)
    {
        double angle=Math.Atan2(mark.End.Y-mark.Start.Y,mark.End.X-mark.Start.X);
        double length=Math.Sqrt(Math.Pow(mark.End.X-mark.Start.X,2)+Math.Pow(mark.End.Y-mark.Start.Y,2));
        double size=Math.Min(Math.Max(10,mark.Width*4),length*.5),spread=.5;
        return [mark.End,new(mark.End.X-size*Math.Cos(angle-spread),mark.End.Y-size*Math.Sin(angle-spread)),new(mark.End.X-size*Math.Cos(angle+spread),mark.End.Y-size*Math.Sin(angle+spread))];
    }
}
