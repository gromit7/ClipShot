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
    internal bool IsClosed{get;private set;}
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1900)};
    private readonly Storyboard entrance=new();
    internal MicrophoneNotice(string text,string detail,MicrophoneState state,bool preview=false,bool appNotice=false,IReadOnlyList<ReleaseHistoryEntry>? history=null,string historyTitle="Что нового")
    {
        Title="ClipShot — статус микрофона";var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)|0x08000080L));
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;
        AppWindow.SetPresenter(presenter);AppWindow.IsShownInSwitchers=preview;
        if(preview)Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)&~0x80L));
        Title=appNotice?"ClipShot — уведомление":"ClipShot — микрофон";
        var ink=Brush(24,39,61);var white=Brush(255,255,255);var accent=Brush(0,98,204);
        bool showHistory=appNotice&&history is {Count:>0};double noticeHeight=156;
        if(showHistory)
        {
            Native.GetCursorPos(out var pointer);var work=DisplayArea.GetFromPoint(new PointInt32(pointer.X,pointer.Y),DisplayAreaFallback.Nearest).WorkArea;
            AppWindow.Move(new PointInt32(work.X+work.Width-1,work.Y+work.Height-1));
            noticeHeight=ReleaseHistoryLayout.HeightForWorkArea(work.Height,Math.Max(1,Native.GetDpiForWindow(hwnd)/96d));
        }
        var canvas=new Canvas{Width=460,Height=noticeHeight,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
        canvas.Children.Add(new Border{Width=460,Height=noticeHeight,Background=Brush(247,250,255),BorderBrush=Brush(172,195,225),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(8)});
        var stripe=new Border{Width=444,Height=4,Background=Brush(0,112,244)};Canvas.SetLeft(stripe,8);canvas.Children.Add(stripe);
        // The same simple clipboard silhouette as the tray, with state only as a small marker.
        var emblem=new Canvas{Width=32,Height=32,RenderTransform=new ScaleTransform{ScaleX=2,ScaleY=2}};Canvas.SetLeft(emblem,20);Canvas.SetTop(emblem,34);canvas.Children.Add(emblem);
        var board=new Rectangle{Width=22,Height=25,Fill=Brush(0,112,244),Stroke=white,StrokeThickness=.75};Canvas.SetLeft(board,5);Canvas.SetTop(board,5);emblem.Children.Add(board);
        var clip=new Rectangle{Width=10,Height=6,Fill=white};Canvas.SetLeft(clip,11);Canvas.SetTop(clip,2);emblem.Children.Add(clip);
        foreach(var row in new[]{(13d,22d),(19d,22d),(25d,17d)})emblem.Children.Add(new Line{X1=10,Y1=row.Item1,X2=row.Item2,Y2=row.Item1,Stroke=white,StrokeThickness=2.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round});
        if(!appNotice)
        {
            var stateBrush=state switch{MicrophoneState.Enabled=>Brush(0,224,118),MicrophoneState.Muted=>Brush(255,54,78),MicrophoneState.Mixed=>Brush(255,192,0),_=>Brush(167,178,194)};
            var badge=new Ellipse{Width=10,Height=10,Fill=stateBrush,Stroke=ink,StrokeThickness=1};Canvas.SetLeft(badge,21);Canvas.SetTop(badge,21);emblem.Children.Add(badge);
            if(state==MicrophoneState.Muted)emblem.Children.Add(new Line{X1=24,Y1=28,X2=28,Y2=24,Stroke=ink,StrokeThickness=2});
        }
        var copy=new StackPanel{Width=334,Spacing=6,RenderTransform=new TranslateTransform()};Canvas.SetLeft(copy,104);Canvas.SetTop(copy,18);canvas.Children.Add(copy);
        copy.Children.Add(new TextBlock{Text=showHistory?"ClipShot · "+historyTitle:"ClipShot",FontFamily=new FontFamily("Segoe UI"),FontSize=12,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=accent});
        copy.Children.Add(new TextBlock{Text=text,FontFamily=new FontFamily("Segoe UI"),FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,FontSize=21,Foreground=ink,TextWrapping=TextWrapping.Wrap,MaxLines=2,TextTrimming=TextTrimming.CharacterEllipsis});
        if(showHistory)
        {
            var releases=new StackPanel{Spacing=13,Margin=new Thickness(0,3,12,8)};
            foreach(var release in history!.Take(ReleaseHistory.MaximumReleases))
            {
                var group=new StackPanel{Spacing=4};
                group.Children.Add(new TextBlock{Text=release.Version.ToString(3),FontFamily=new FontFamily("Segoe UI"),FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,FontSize=13,Foreground=accent});
                foreach(string change in release.Changes.Take(ReleaseHistory.MaximumChanges))
                {
                    var row=new Grid();row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(13)});row.ColumnDefinitions.Add(new ColumnDefinition{Width=new GridLength(1,GridUnitType.Star)});
                    row.Children.Add(new TextBlock{Text="•",FontSize=12,Foreground=accent});
                    var line=new TextBlock{Text=change,FontFamily=new FontFamily("Segoe UI"),FontSize=12,Foreground=ink,TextWrapping=TextWrapping.Wrap};Grid.SetColumn(line,1);row.Children.Add(line);group.Children.Add(row);
                }
                releases.Children.Add(group);
            }
            var scroll=new ScrollViewer{Content=releases,MaxHeight=Math.Max(32,noticeHeight-140),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollMode=ScrollMode.Disabled,VerticalScrollMode=ScrollMode.Enabled};
            Microsoft.UI.Xaml.Automation.AutomationProperties.SetName(scroll,historyTitle);copy.Children.Add(scroll);
        }
        else copy.Children.Add(new TextBlock{Text=detail,FontFamily=new FontFamily("Segoe UI"),FontSize=13,Foreground=Brush(74,94,119),TextTrimming=TextTrimming.CharacterEllipsis,MaxLines=3,TextWrapping=TextWrapping.Wrap});
        Content=new Viewbox{Child=canvas,Stretch=Stretch.Fill};
        Native.GetCursorPos(out var cursor);var area=DisplayArea.GetFromPoint(new PointInt32(cursor.X,cursor.Y),DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.Move(new PointInt32(area.X+area.Width-1,area.Y+area.Height-1));
        double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        scale=Math.Min(scale,Math.Min((area.Width-24)/460d,(area.Height-24)/noticeHeight));
        int width=Math.Max(1,(int)(460*scale)),height=Math.Max(1,(int)(noticeHeight*scale));
        AppWindow.MoveAndResize(new RectInt32(area.X+area.Width-width-12,area.Y+area.Height-height-12,width,height));
        void Animate(string property,double from,double to)
        {
            var animation=new DoubleAnimation{From=from,To=to,Duration=new Duration(TimeSpan.FromMilliseconds(170)),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},EnableDependentAnimation=true};
            Storyboard.SetTarget(animation,copy);Storyboard.SetTargetProperty(animation,property);entrance.Children.Add(animation);
        }
        Animate("Opacity",0,1);Animate("(UIElement.RenderTransform).(TranslateTransform.X)",14,0);
        canvas.Loaded+=(_,_)=>entrance.Begin();
        if(showHistory)
        {
            // Release notes stay readable until explicitly dismissed. Wheel
            // scrolling and pointer movement must never start an expiry timer.
            canvas.Tapped+=(_,_)=>Close();
        }
        else if(appNotice)
        {
            Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)|0x80000L));Native.SetLayeredWindowAttributes(hwnd,0,255,2);
            Native.GetCursorPos(out var initialMouse);var clock=System.Diagnostics.Stopwatch.StartNew();var lifetime=new NoticeLifetime();timer.Interval=TimeSpan.FromMilliseconds(40);
            timer.Tick+=(_,_)=>
            {
                bool input=Native.GetCursorPos(out var mouse);bool moved=input&&Math.Abs(mouse.X-initialMouse.X)+Math.Abs(mouse.Y-initialMouse.Y)>=3;
                byte alpha=lifetime.Opacity(clock.Elapsed,moved);if(alpha==0){Close();return;}Native.SetLayeredWindowAttributes(hwnd,0,alpha,2);
            };
        }
        else timer.Tick+=(_,_)=>Close();Closed+=(_,_)=>{IsClosed=true;timer.Stop();entrance.Stop();};AppWindow.Show(false);if(!preview&&!showHistory)timer.Start();
    }
    private static SolidColorBrush Brush(byte r,byte g,byte b)=>new(Windows.UI.Color.FromArgb(255,r,g,b));
}
