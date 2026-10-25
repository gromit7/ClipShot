using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;
using Windows.Graphics;
namespace ClipShot;
internal sealed class MicrophoneNotice : Window
{
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1900)};
    private readonly Storyboard entrance=new();
    internal MicrophoneNotice(string text,string detail,MicrophoneState state,bool preview=false,bool appNotice=false)
    {
        Title="ClipShot — статус микрофона";var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)|0x08000080L));
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;
        AppWindow.SetPresenter(presenter);AppWindow.IsShownInSwitchers=preview;
        if(preview)Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)&~0x80L));
        var ink=Brush(14,16,25);var white=Brush(247,249,255);
        var accent=state switch{MicrophoneState.Enabled=>Brush(78,244,202),MicrophoneState.Muted=>Brush(255,79,143),MicrophoneState.Mixed=>Brush(255,205,83),_=>Brush(157,174,197)};
        var canvas=new Canvas{Width=460,Height=156,Background=ink};
        Polygon Shape(SolidColorBrush fill,double[] points,SolidColorBrush? stroke=null,double thickness=0)
        {
            var polygon=new Polygon{Fill=fill,Stroke=stroke,StrokeThickness=thickness,StrokeLineJoin=PenLineJoin.Miter};
            for(int i=0;i<points.Length;i+=2)polygon.Points.Add(new Point(points[i],points[i+1]));canvas.Children.Add(polygon);return polygon;
        }
        // Offset ink, clipped corners and a speech-tail give the panel its comic silhouette.
        Shape(accent,[14,12,444,12,456,24,456,130,440,145,391,145,366,154,369,145,14,145]);
        Shape(ink,[5,4,434,4,448,18,448,124,432,136,381,136,359,150,361,136,5,136],white,2);
        Shape(accent,[5,4,23,4,5,22]);
        Shape(accent,[421,117,439,99,439,107,429,117]);
        // Subtle halftone texture, kept away from the reading area.
        for(int row=0;row<5;row++)for(int column=0;column<8;column++)
        {
            var dot=new Ellipse{Width=2,Height=2,Fill=accent,Opacity=.13};Canvas.SetLeft(dot,354+column*10);Canvas.SetTop(dot,21+row*10);canvas.Children.Add(dot);
        }
        var emblem=new Canvas{Width=66,Height=82};Canvas.SetLeft(emblem,20);Canvas.SetTop(emblem,29);canvas.Children.Add(emblem);
        var plate=new Polygon{Points=new PointCollection{new(0,9),new(51,0),new(65,63),new(14,75)},Fill=accent};emblem.Children.Add(plate);
        void Line(double x1,double y1,double x2,double y2,double thickness=3.5)
            =>emblem.Children.Add(new Line{X1=x1,Y1=y1,X2=x2,Y2=y2,Stroke=ink,StrokeThickness=thickness,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
        var capsule=new Rectangle{Width=14,Height=28,RadiusX=7,RadiusY=7,Fill=ink};Canvas.SetLeft(capsule,26);Canvas.SetTop(capsule,17);emblem.Children.Add(capsule);
        var cradle=new Microsoft.UI.Xaml.Shapes.Path{Stroke=ink,StrokeThickness=3.5,Data=new PathGeometry{Figures={new PathFigure{StartPoint=new Point(20,35),Segments={new BezierSegment{Point1=new Point(20,59),Point2=new Point(46,59),Point3=new Point(46,35)}}}}}};emblem.Children.Add(cradle);
        Line(33,53,33,62);Line(25,62,41,62);
        if(state==MicrophoneState.Muted){Line(17,18,49,57,7);emblem.Children.Add(new Line{X1=17,Y1=18,X2=49,Y2=57,Stroke=white,StrokeThickness=2});}
        if(appNotice){emblem.Children.Clear();emblem.Children.Add(new Image{Source=new Microsoft.UI.Xaml.Media.Imaging.BitmapImage(new Uri("ms-appx:///Assets/ClipShot.png")),Width=66,Height=75,Stretch=Stretch.Uniform});}
        var copy=new StackPanel{Width=334,Spacing=5,RenderTransform=new TranslateTransform()};Canvas.SetLeft(copy,100);Canvas.SetTop(copy,23);canvas.Children.Add(copy);
        var brand=new TextBlock{Text="CLIPSHOT",FontFamily=new FontFamily("Bahnschrift"),FontSize=11,CharacterSpacing=240,Foreground=accent};copy.Children.Add(brand);
        copy.Children.Add(new TextBlock{Text=text.ToUpperInvariant(),FontFamily=new FontFamily("Bahnschrift SemiCondensed"),FontWeight=Microsoft.UI.Text.FontWeights.Bold,FontSize=23,Foreground=white,TextWrapping=TextWrapping.Wrap,MaxLines=2,TextTrimming=TextTrimming.CharacterEllipsis});
        copy.Children.Add(new TextBlock{Text=detail,FontFamily=new FontFamily("Segoe UI"),FontSize=12,Foreground=Brush(177,185,204),TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=appNotice?3:1,TextWrapping=appNotice?TextWrapping.Wrap:TextWrapping.NoWrap});
        for(int i=0;i<3;i++)Shape(accent,[101+i*9,121,106+i*9,115,110+i*9,115,105+i*9,121]);
        Content=new Viewbox{Child=canvas,Stretch=Stretch.Fill};
        Native.GetCursorPos(out var cursor);var area=DisplayArea.GetFromPoint(new PointInt32(cursor.X,cursor.Y),DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(area.X+area.Width-1,area.Y+area.Height-1));
        double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        scale=Math.Min(scale,Math.Min((area.Width-24)/460d,(area.Height-24)/156d));
        int width=Math.Max(1,(int)(460*scale)),height=Math.Max(1,(int)(156*scale));
        AppWindow.MoveAndResize(new RectInt32(area.X+area.Width-width-12,area.Y+area.Height-height-12,width,height));
        void Animate(string property,double from,double to)
        {
            var animation=new DoubleAnimation{From=from,To=to,Duration=new Duration(TimeSpan.FromMilliseconds(170)),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},EnableDependentAnimation=true};
            Storyboard.SetTarget(animation,copy);Storyboard.SetTargetProperty(animation,property);entrance.Children.Add(animation);
        }
        Animate("Opacity",0,1);Animate("(UIElement.RenderTransform).(TranslateTransform.X)",14,0);
        canvas.Loaded+=(_,_)=>entrance.Begin();
        if(appNotice)
        {
            Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)|0x80000L));Native.SetLayeredWindowAttributes(hwnd,0,255,2);
            Native.GetCursorPos(out var initialMouse);var clock=System.Diagnostics.Stopwatch.StartNew();var lifetime=new NoticeLifetime();timer.Interval=TimeSpan.FromMilliseconds(40);
            timer.Tick+=(_,_)=>
            {
                bool moved=Native.GetCursorPos(out var mouse)&&Math.Abs(mouse.X-initialMouse.X)+Math.Abs(mouse.Y-initialMouse.Y)>=3;
                byte alpha=lifetime.Opacity(clock.Elapsed,moved);if(alpha==0){Close();return;}Native.SetLayeredWindowAttributes(hwnd,0,alpha,2);
            };
        }
        else timer.Tick+=(_,_)=>Close();Closed+=(_,_)=>{timer.Stop();entrance.Stop();};AppWindow.Show(false);if(!preview)timer.Start();
    }
    private static SolidColorBrush Brush(byte r,byte g,byte b)=>new(Windows.UI.Color.FromArgb(255,r,g,b));
}
