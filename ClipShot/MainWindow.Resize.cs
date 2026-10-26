using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;
using Windows.Graphics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private bool windowResizing;
    private SizeInt32 resizeStartSize;
    private enum WindowPointerGesture {None,Move,Resize}
    private WindowPointerGesture windowPointerGesture;
    private Pointer? windowGesturePointer;
    private uint windowGesturePointerId;
    private Native.Point windowGestureMouse;
    private PointInt32 windowGesturePosition;
    private SizeInt32 windowGestureSize;
    private readonly DispatcherTimer windowGestureTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private bool windowGestureReady;
    private long windowGestureEventAt;
    private bool CanStartWindowPointerGesture=>!exiting&&!dialogOpen&&!takingScreenshot&&!windowResizing&&editor is null&&openMenus==0&&AppWindow.IsVisible;
    private static bool WindowPrimaryButtonDown()=>(Native.GetAsyncKeyState(Native.GetSystemMetrics(23)==0?1:2)&0x8000)!=0;

    private void BeginWindowResize()
    {
        if(!windowResizing)resizeStartSize=AppWindow.Size;
        windowResizing=true;CancelSectionHover();CancelPanelFade();hoverTimer.Stop();leaveTimer.Stop();hovered=null;preview.IsOpen=false;sectionPopup.IsOpen=false;
    }
    private void EndWindowResize()
    {
        if(!windowResizing)return;windowResizing=false;
        if(AppWindow.Size.Width!=resizeStartSize.Width||AppWindow.Size.Height!=resizeStartSize.Height)
            store.SetMany(new Dictionary<string,string>{{"width",AppWindow.Size.Width.ToString()},{"height",AppWindow.Size.Height.ToString()},{"panelautofit","0"}});
        DispatcherQueue.TryEnqueue(PositionSection);
    }
    private void ResizeGripPressed(object sender,PointerRoutedEventArgs e)=>TryStartWindowPointerGesture(WindowPointerGesture.Resize,e);
    private void TryStartWindowPointerGesture(WindowPointerGesture gesture,PointerRoutedEventArgs e)
    {
        if(!CanStartWindowPointerGesture||e.Pointer.PointerDeviceType!=Microsoft.UI.Input.PointerDeviceType.Mouse||
            !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed||!TryWindowEventPoint(e,out var mouse))return;
        CancelPanelFade();
        if(!Root.CapturePointer(e.Pointer))return;
        e.Handled=true;BeginWindowPointerGesture(gesture,mouse,e.Pointer.PointerId,e.Pointer);
    }
    private void BeginWindowPointerGesture(WindowPointerGesture gesture,Native.Point mouse,uint pointerId,Pointer? pointer=null)
    {
        // Не вызываем WM_NCLBUTTONDOWN из routed PointerPressed: его вложенная
        // системная move/size-петля может пережить исходный отпуск кнопки.
        windowPointerGesture=gesture;windowGesturePointer=pointer;windowGesturePointerId=pointerId;
        windowGestureMouse=mouse;windowGesturePosition=AppWindow.Position;windowGestureSize=AppWindow.Size;
        windowGestureEventAt=Environment.TickCount64;
        // A deliberate move may detach the collapsed panel until a category
        // is expanded and collapsed again; automatic docking must not fight it.
        if(gesture==WindowPointerGesture.Move&&AllSectionsCollapsed)preserveCollapsedPosition=true;
        BeginWindowResize();
        if(!windowGestureReady){windowGestureTimer.Tick+=(_,_)=>PollWindowPointerGesture();windowGestureReady=true;}
        windowGestureTimer.Start();
    }
    private void PollWindowPointerGesture()
    {
        if(windowPointerGesture==WindowPointerGesture.None){windowGestureTimer.Stop();return;}
        if(exiting||dialogOpen||takingScreenshot||!AppWindow.IsVisible||!Native.GetCursorPos(out var mouse)){EndWindowPointerGesture();return;}
        if(Environment.TickCount64-windowGestureEventAt<80)return;
        // Watchdog завершает жест даже при потерянном PointerReleased вне окна.
        UpdateWindowPointerGesture(mouse,WindowPrimaryButtonDown());
    }
    private void UpdateWindowPointerGesture(Native.Point mouse,bool leftButtonDown)
    {
        if(windowPointerGesture==WindowPointerGesture.None)return;
        if(!leftButtonDown){EndWindowPointerGesture();return;}
        int dx=mouse.X-windowGestureMouse.X,dy=mouse.Y-windowGestureMouse.Y;
        if(windowPointerGesture==WindowPointerGesture.Move)
            AppWindow.Move(new PointInt32(windowGesturePosition.X+dx,windowGesturePosition.Y+dy));
        else
        {
            double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
            AppWindow.Resize(new SizeInt32(Math.Max((int)Math.Ceiling(Root.MinWidth*scale),windowGestureSize.Width+dx),Math.Max((int)Math.Ceiling(Root.MinHeight*scale),windowGestureSize.Height+dy)));
        }
    }
    private void WindowPointerMoved(object sender,PointerRoutedEventArgs e)
    {
        if(windowPointerGesture==WindowPointerGesture.None||e.Pointer.PointerId!=windowGesturePointerId)return;
        windowGestureEventAt=Environment.TickCount64;
        if(TryWindowEventPoint(e,out var point))UpdateWindowPointerGesture(point,e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed);
        else EndWindowPointerGesture(e.Pointer.PointerId);
        e.Handled=true;
    }
    private bool TryWindowEventPoint(PointerRoutedEventArgs e,out Native.Point point)
    {
        point=new Native.Point();if(!Native.ClientToScreen(hwnd,ref point))return false;
        var local=e.GetCurrentPoint(Root).Position;double scale=Root.XamlRoot?.RasterizationScale??1;
        point.X+=(int)Math.Round(local.X*scale);point.Y+=(int)Math.Round(local.Y*scale);return true;
    }
    private void WindowPointerReleased(object sender,PointerRoutedEventArgs e)
    {
        if(windowPointerGesture!=WindowPointerGesture.None&&e.Pointer.PointerId==windowGesturePointerId&&
            TryWindowEventPoint(e,out var point))
            UpdateWindowPointerGesture(point,true);
        EndWindowPointerGesture(e.Pointer.PointerId);
    }
    private void WindowPointerEnded(object sender,PointerRoutedEventArgs e)=>EndWindowPointerGesture(e.Pointer.PointerId);
    private void EndWindowPointerGesture(uint? pointerId=null)
    {
        if(windowPointerGesture==WindowPointerGesture.None||(pointerId.HasValue&&pointerId.Value!=windowGesturePointerId))return;
        var pointer=windowGesturePointer;
        // CaptureLost может прийти синхронно из ReleasePointerCapture.
        windowPointerGesture=WindowPointerGesture.None;windowGesturePointer=null;windowGestureTimer.Stop();
        if(pointer is not null)Root.ReleasePointerCapture(pointer);
        EndWindowResize();
    }
}
