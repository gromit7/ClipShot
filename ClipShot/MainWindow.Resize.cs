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
        windowResizing=true;CancelPanelFade();hoverTimer.Stop();leaveTimer.Stop();hovered=null;preview.IsOpen=false;sectionPopup.IsOpen=false;
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
            !e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed||!TryWindowPressPoint(e,out var mouse))return;
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
        BeginWindowResize();
        if(!windowGestureReady){windowGestureTimer.Tick+=(_,_)=>PollWindowPointerGesture();windowGestureReady=true;}
        windowGestureTimer.Start();
    }
    private void PollWindowPointerGesture()
    {
        if(windowPointerGesture==WindowPointerGesture.None){windowGestureTimer.Stop();return;}
        if(exiting||dialogOpen||takingScreenshot||!AppWindow.IsVisible||!Native.GetCursorPos(out var mouse)){EndWindowPointerGesture();return;}
        if(WindowPrimaryButtonDown())UpdateWindowPointerGesture(mouse,true);
        // Свежий routed release может ещё ждать обработки после быстрой мыши.
        // При отпущенной кнопке watchdog только снимает жест, не двигает окно.
        else if(Environment.TickCount64-windowGestureEventAt>=80)EndWindowPointerGesture();
    }
    private void UpdateWindowPointerGesture(Native.Point mouse,bool leftButtonDown)
    {
        if(windowPointerGesture==WindowPointerGesture.None)return;
        if(!leftButtonDown){EndWindowPointerGesture();return;}
        int dx=mouse.X-windowGestureMouse.X,dy=mouse.Y-windowGestureMouse.Y;
        if(windowPointerGesture==WindowPointerGesture.Move)
        {
            var position=new PointInt32(windowGesturePosition.X+dx,windowGesturePosition.Y+dy);
            if(AppWindow.Position.X==position.X&&AppWindow.Position.Y==position.Y)return;
            // Только фактическое движение считается ручным размещением.
            // Нажатие на пустом месте не отключает обычное закрепление панели.
            if(AllSectionsCollapsed)preserveCollapsedPosition=true;
            else preserveExpandedDragPosition=true;
            AppWindow.Move(position);
        }
        else
        {
            double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
            var size=new SizeInt32(Math.Max((int)Math.Ceiling(Root.MinWidth*scale),windowGestureSize.Width+dx),Math.Max((int)Math.Ceiling(Root.MinHeight*scale),windowGestureSize.Height+dy));
            if(AppWindow.Size.Width!=size.Width||AppWindow.Size.Height!=size.Height)AppWindow.Resize(size);
        }
    }
    private void WindowPointerMoved(object sender,PointerRoutedEventArgs e)
    {
        if(windowPointerGesture==WindowPointerGesture.None||e.Pointer.PointerId!=windowGesturePointerId)return;
        windowGestureEventAt=Environment.TickCount64;
        if(!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)CompleteWindowPointerGesture(e.Pointer.PointerId);
        else if(!Native.GetCursorPos(out var point))EndWindowPointerGesture(e.Pointer.PointerId);
        else if(WindowPrimaryButtonDown())UpdateWindowPointerGesture(point,true);
        e.Handled=true;
    }
    private bool TryWindowPressPoint(PointerRoutedEventArgs e,out Native.Point point)
    {
        // На Press окно ещё не сдвигалось: это единственное преобразование
        // local→screen в жесте, сохраняющее начало даже у coalesced ввода.
        point=new Native.Point();if(!Native.ClientToScreen(hwnd,ref point))return false;
        var local=e.GetCurrentPoint(Root).Position;double scale=Root.XamlRoot?.RasterizationScale??1;
        point.X+=(int)Math.Round(local.X*scale);point.Y+=(int)Math.Round(local.Y*scale);return true;
    }
    private void WindowPointerReleased(object sender,PointerRoutedEventArgs e)=>CompleteWindowPointerGesture(e.Pointer.PointerId);
    private void CompleteWindowPointerGesture(uint pointerId)
    {
        if(windowPointerGesture==WindowPointerGesture.None||pointerId!=windowGesturePointerId)return;
        // Единственный источник геометрии во время движения и при завершении —
        // экранный cursor sample. Исторические local coordinates после Move
        // нельзя прибавлять к уже изменившемуся ClientToScreen origin.
        if(Native.GetCursorPos(out var point))UpdateWindowPointerGesture(point,true);
        EndWindowPointerGesture(pointerId);
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
