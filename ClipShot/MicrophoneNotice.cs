using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Graphics;
namespace ClipShot;
internal sealed class MicrophoneNotice : Window
{
    private readonly DispatcherTimer timer=new(){Interval=TimeSpan.FromMilliseconds(1800)};
    internal MicrophoneNotice(string text,string detail,MicrophoneState state)
    {
        Title="ClipShot";var hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        Native.SetWindowLongPtr(hwnd,-20,(nint)((long)Native.GetWindowLongPtr(hwnd,-20)|0x08000080L));
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsAlwaysOnTop=true;presenter.IsResizable=false;
        AppWindow.SetPresenter(presenter);AppWindow.IsShownInSwitchers=false;
        var content=new StackPanel{Spacing=5};content.Children.Add(new TextBlock{Text=text,FontSize=18,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,Foreground=new SolidColorBrush(Microsoft.UI.Colors.White)});
        content.Children.Add(new TextBlock{Text=detail,FontSize=12,MaxLines=2,TextWrapping=TextWrapping.Wrap,Foreground=new SolidColorBrush(Microsoft.UI.Colors.White),Opacity=.8});
        Content=new Border{Padding=new Thickness(20,14,20,14),Background=new SolidColorBrush(state==MicrophoneState.Muted?Windows.UI.Color.FromArgb(255,116,35,43):Windows.UI.Color.FromArgb(255,30,50,43)),Child=content};
        Native.GetCursorPos(out var cursor);var area=DisplayArea.GetFromPoint(new PointInt32(cursor.X,cursor.Y),DisplayAreaFallback.Nearest).WorkArea;
        double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);int width=(int)(380*scale),height=(int)(104*scale);
        AppWindow.MoveAndResize(new RectInt32(area.X+area.Width-width-20,area.Y+area.Height-height-20,width,height));
        timer.Tick+=(_,_)=>Close();Closed+=(_,_)=>timer.Stop();AppWindow.Show(false);timer.Start();
    }
}
