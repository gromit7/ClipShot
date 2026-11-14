using Microsoft.UI.Input;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Content;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Foundation;
using Windows.Graphics;
using Windows.System;

namespace ClipShot;
internal sealed class RegionCaptureWindow : Window
{
    private readonly ScreenFrame frame;
    private readonly UiText words;
    private sealed class CaptureSurface : Grid
    {
        internal void SetSelectionCursor(InputCursor? cursor)=>ProtectedCursor=cursor;
        internal bool HasSelectionCursor=>ProtectedCursor is not null;
    }
    private readonly CaptureSurface surface=new();
    private readonly CaptureSelectionCursor cursor;
    private InputPointerSource? islandPointerSource;
    private ContentIsland? captureIsland;
    private InputNonClientPointerSource? nonClientInput;
    private string islandCursorBinding="NotLoaded";
    private readonly nint hwnd,previousProcedure;
    private readonly Native.WndProc cursorProcedure;
    private readonly ContentControl focus=new(){IsTabStop=true,HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Stretch};
    private readonly Image background=new(){Stretch=Stretch.Fill};
    private readonly Image clear=new(){Stretch=Stretch.Fill};
    private readonly RectangleGeometry clip=new(){Rect=new Rect(0,0,0,0)};
    private readonly Canvas guides=new(){IsHitTestVisible=false};
    private readonly Rectangle outlineShadow=new(){Stroke=new SolidColorBrush(Microsoft.UI.Colors.Black),StrokeThickness=6,Visibility=Visibility.Collapsed};
    private readonly Rectangle outline=new(){Stroke=new SolidColorBrush(Microsoft.UI.Colors.White),StrokeThickness=2,Visibility=Visibility.Collapsed};
    private readonly Rectangle[] corners=Enumerable.Range(0,4).Select(_=>new Rectangle{Width=8,Height=8,Fill=new SolidColorBrush(Microsoft.UI.Colors.White),Stroke=new SolidColorBrush(Microsoft.UI.Colors.Black),StrokeThickness=1,Visibility=Visibility.Collapsed}).ToArray();
    private readonly TextBlock dimensions=new(){Foreground=new SolidColorBrush(Microsoft.UI.Colors.White),FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};
    private readonly Border sizeBadge;
    private PixelRegion? pendingRegion,lastRegion;
    private readonly TaskCompletionSource<ScreenFrame?> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TaskCompletionSource<bool> surfaceReady=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int startX,startY;private bool dragging,finished;
    private readonly bool test;
    public RegionCaptureWindow(ScreenFrame frame,bool test=false,UiText? language=null,bool ignoreHardwareInput=false)
    {
        words=language??new UiText("ru");this.frame=frame;this.test=test;Title=words.Translate("ClipShot — выделение области");
        cursor=new CaptureSelectionCursor(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","Selection.cur"));
        hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);cursorProcedure=CursorMessage;
        previousProcedure=Native.SetWindowLongPtr(hwnd,-4,System.Runtime.InteropServices.Marshal.GetFunctionPointerForDelegate(cursorProcedure));
        sizeBadge=new Border{Child=dimensions,Padding=new Thickness(9,5,9,5),Background=new SolidColorBrush(Windows.UI.Color.FromArgb(245,24,24,24)),BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.White),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),Visibility=Visibility.Collapsed};
        var presenter=OverlappedPresenter.Create();presenter.SetBorderAndTitleBar(false,false);presenter.IsResizable=false;presenter.IsMaximizable=false;presenter.IsMinimizable=false;presenter.IsAlwaysOnTop=true;AppWindow.SetPresenter(presenter);AppWindow.IsShownInSwitchers=false;
        // Remove the native non-client frame as well as the visible title bar.
        // https://learn.microsoft.com/windows/win32/winmsg/wm-nccalcsize
        long style=(long)Native.GetWindowLongPtr(hwnd,-16);
        Native.SetWindowLongPtr(hwnd,-16,(nint)((style&~0x00CF0000L)|0x80000000L));
        uint square=1,noBorder=0xFFFFFFFE;
        Native.DwmSetWindowAttribute(hwnd,33,ref square,4);Native.DwmSetWindowAttribute(hwnd,34,ref noBorder,4);
        Native.SetWindowPos(hwnd,0,frame.X,frame.Y,frame.Width,frame.Height,0x0034);
        AppWindow.MoveAndResize(new RectInt32(frame.X,frame.Y,frame.Width,frame.Height));
        var bitmap=new WriteableBitmap(frame.Width,frame.Height);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(frame.Pixels);bitmap.Invalidate();background.Source=bitmap;clear.Source=bitmap;clear.Clip=clip;
        surface.Children.Add(background);surface.Children.Add(new Border{Background=new SolidColorBrush(Windows.UI.Color.FromArgb(165,0,0,0))});surface.Children.Add(clear);
        guides.Children.Add(outlineShadow);guides.Children.Add(outline);foreach(var corner in corners)guides.Children.Add(corner);guides.Children.Add(sizeBadge);surface.Children.Add(guides);
        surface.Children.Add(new Border{Background=new SolidColorBrush(Windows.UI.Color.FromArgb(230,32,32,32)),CornerRadius=new CornerRadius(8),Padding=new Thickness(18,10,18,10),Margin=new Thickness(20),HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Top,IsHitTestVisible=false,Child=new TextBlock{Text=words.Translate("Выделите область мышью · Esc — отмена"),Foreground=new SolidColorBrush(Microsoft.UI.Colors.White)}});
        focus.Content=surface;Content=focus;
        if(!ignoreHardwareInput){surface.PointerPressed+=Pressed;surface.PointerMoved+=Moved;surface.PointerReleased+=Released;}
        surface.Loaded+=(_,_)=>
        {
            if(finished)return;
            // Also clear WinAppSDK's separate non-client input sink, not only HWND styles.
            nonClientInput=InputNonClientPointerSource.GetForWindowId(AppWindow.Id);
            nonClientInput.ClearAllRegionRects();
            nonClientInput.SetRegionRects(NonClientRegionKind.Passthrough,[new RectInt32(0,0,frame.Width,frame.Height)]);
            // Configure XAML only after it has a live input site. Native SetCursor
            // changes this UI thread's cursor, not the island's input-thread cursor.
            surface.SetSelectionCursor(null);surface.SetSelectionCursor(cursor.XamlCursor);
            try
            {
                // A child XAML visual is not the island's root visual:
                // GetByVisual(child) can return null. Use XamlRoot's input owner.
                captureIsland=surface.XamlRoot?.ContentIsland;
                if(captureIsland is not null)
                {
                    islandPointerSource=InputPointerSource.GetForIsland(captureIsland);
                    if(islandPointerSource is not null)
                    {
                        // With a stationary mouse XAML has no pointer-enter element
                        // yet and its default island cursor is Arrow. Set the actual
                        // input owner's initial cursor once; ProtectedCursor handles
                        // subsequent hit-testing/capture without a cursor timer.
                        islandPointerSource.Cursor=cursor.XamlCursor;
                        islandCursorBinding="Bound";
                    }
                    else islandCursorBinding="PointerSourceUnavailable";
                }
                else islandCursorBinding="IslandUnavailable";
            }
            catch(System.Runtime.InteropServices.COMException error){islandCursorBinding=$"Error0x{error.HResult:X8}";}
            cursor.Apply();surfaceReady.TrySetResult(true);
        };
        CompositionTarget.Rendering+=RenderPending;
        if(!ignoreHardwareInput)surface.PointerCaptureLost+=(_,_)=>{if(dragging){dragging=false;Reset();}};
        focus.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler((_,e)=>{if(e.Key==VirtualKey.Escape){e.Handled=true;Cancel();}}),true);
        Closed+=(_,_)=>
        {
            CompositionTarget.Rendering-=RenderPending;
            bool ownedCursor=cursor.IsCurrent;
            try
            {
                surface.SetSelectionCursor(null);
                if(captureIsland is {IsClosed:false}&&islandPointerSource is not null)islandPointerSource.Cursor=null;
            }
            // The input site can close between IsClosed and Cursor=null.
            // CsWinRT maps RO_E_CLOSED to ObjectDisposedException.
            catch(System.Runtime.InteropServices.COMException){}
            catch(ObjectDisposedException){}
            finally
            {
                islandPointerSource=null;captureIsland=null;
                Native.SetWindowLongPtr(hwnd,-4,previousProcedure);
                cursor.Restore(ownedCursor);
                try{cursor.Dispose();}
                finally{finished=true;surfaceReady.TrySetCanceled();completion.TrySetResult(null);}
            }
        };
        Activated+=(_,e)=>{if(e.WindowActivationState==WindowActivationState.Deactivated&&!test)Cancel();else if(e.WindowActivationState!=WindowActivationState.Deactivated&&!finished)cursor.Apply();};
    }
    public Task<ScreenFrame?> SelectAsync()
    {
        Activate();Native.SetForegroundWindow(WinRT.Interop.WindowNative.GetWindowHandle(this));focus.Focus(FocusState.Programmatic);
        // WM_SETCURSOR follows mouse movement; activation with a stationary mouse
        // does not guarantee that message. Make the mode visible immediately.
        cursor.Apply();
        return completion.Task;
    }
    public void Cancel()=>Finish(null);
    private void Finish(ScreenFrame? result)
    {if(finished)return;finished=true;completion.TrySetResult(result);Close();}
    private (int X,int Y) Point(PointerRoutedEventArgs e)
    {
        var p=e.GetCurrentPoint(surface).Position;
        return ((int)Math.Round(p.X*frame.Width/Math.Max(1,surface.ActualWidth)),(int)Math.Round(p.Y*frame.Height/Math.Max(1,surface.ActualHeight)));
    }
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        var p=e.GetCurrentPoint(surface);if(p.Properties.IsRightButtonPressed){Cancel();return;}if(!p.Properties.IsLeftButtonPressed)return;
        (startX,startY)=Point(e);dragging=true;surface.CapturePointer(e.Pointer);e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e)
    {if(!dragging)return;var p=Point(e);pendingRegion=PixelRegion.FromDrag(startX,startY,p.X,p.Y,frame.Width,frame.Height);}
    private void RenderPending(object? sender,object e)
    {if(finished||!dragging||pendingRegion is not PixelRegion r||lastRegion==r)return;pendingRegion=null;lastRegion=r;Draw(r);}
    private void Released(object sender,PointerRoutedEventArgs e)
    {
        if(!dragging)return;var p=Point(e);var region=PixelRegion.FromDrag(startX,startY,p.X,p.Y,frame.Width,frame.Height);dragging=false;surface.ReleasePointerCaptures();
        if(region.Width<2||region.Height<2){Reset();return;}Finish(frame.Crop(region));
    }
    private void Reset(){pendingRegion=lastRegion=null;clip.Rect=new Rect(0,0,0,0);outline.Visibility=outlineShadow.Visibility=sizeBadge.Visibility=Visibility.Collapsed;foreach(var corner in corners)corner.Visibility=Visibility.Collapsed;dimensions.Text="";}
    private void Draw(PixelRegion r)
    {
        double sx=surface.ActualWidth/frame.Width,sy=surface.ActualHeight/frame.Height;
        clip.Rect=new Rect(r.X*sx,r.Y*sy,r.Width*sx,r.Height*sy);
        PositionBorder(outlineShadow,r,sx,sy);PositionBorder(outline,r,sx,sy);
        for(int i=0;i<4;i++){corners[i].Visibility=Visibility.Visible;Canvas.SetLeft(corners[i],(r.X+(i%2)*r.Width)*sx-4);Canvas.SetTop(corners[i],(r.Y+(i/2)*r.Height)*sy-4);}
        dimensions.Text=$"{r.Width} × {r.Height} {words.Translate("пикс.")}";sizeBadge.Visibility=Visibility.Visible;
        Canvas.SetLeft(sizeBadge,Math.Clamp(r.X*sx,4,Math.Max(4,surface.ActualWidth-180)));Canvas.SetTop(sizeBadge,r.Y*sy>=42?r.Y*sy-38:Math.Min(surface.ActualHeight-38,(r.Y+r.Height)*sy+10));
    }
    private static void PositionBorder(Rectangle border,PixelRegion r,double sx,double sy)
    {border.Visibility=Visibility.Visible;border.Width=r.Width*sx;border.Height=r.Height*sy;Canvas.SetLeft(border,r.X*sx);Canvas.SetTop(border,r.Y*sy);}
    internal void PreviewTestSelection(PixelRegion region){dragging=true;pendingRegion=region;}
    internal bool TestSelectionRendered(PixelRegion region)=>lastRegion==region;
    internal void CompleteTest(PixelRegion region)
    {if(test&&lastRegion!=region)throw new InvalidOperationException("Pending selection was not rendered");Finish(frame.Crop(region));}
    internal bool HasSelectionCursor=>surface.HasSelectionCursor;
    internal Task TestSurfaceReady=>surfaceReady.Task;
    internal bool SelectionCursorIsCurrent=>cursor.IsCurrent;
    internal bool TestPointerInsideClient=>CaptureSelectionCursor.PointerInsideClient(hwnd);
    internal bool TestOwnsPointer=>CaptureSelectionCursor.PointerOwnedBy(hwnd);
    internal string TestCursorDiagnostics=>$"HasSelectionCursor={HasSelectionCursor}; PointerInsideClient={TestPointerInsideClient}; OwnsPointer={TestOwnsPointer}; IslandCursorBinding={islandCursorBinding}; {cursor.Diagnostics}; {CaptureSelectionCursor.OwnershipDiagnostics(hwnd)}";
    private nint CursorMessage(nint window,uint message,nint w,nint l)
    {
        if(message==0x0083)return 0; // WM_NCCALCSIZE: the entire window is client area.
        if(message==0x0084&&!finished)return 1; // WM_NCHITTEST: no resize/drag border.
        if(message==0x20&&!finished){cursor.Apply();return 1;}
        return Native.CallWindowProc(previousProcedure,window,message,w,l);
    }
    internal void CheckClientEdges()
    {
        var regions=nonClientInput?.GetRegionRects(NonClientRegionKind.Passthrough);
        if(regions is not{Length:1}||regions[0].X!=0||regions[0].Y!=0||regions[0].Width!=frame.Width||regions[0].Height!=frame.Height)
            throw new InvalidOperationException("Capture input sink does not pass the full surface to XAML");
        if(!Native.GetWindowRect(hwnd,out var outer)||!Native.GetClientRect(hwnd,out var inner))throw new InvalidOperationException("Capture geometry unavailable");
        var origin=new Native.Point();if(!Native.ClientToScreen(hwnd,ref origin))throw new InvalidOperationException("Capture origin unavailable");
        if(origin.X!=frame.X||origin.Y!=frame.Y||outer.Left!=frame.X||outer.Top!=frame.Y||inner.Right!=frame.Width||inner.Bottom!=frame.Height||outer.Right-outer.Left!=frame.Width||outer.Bottom-outer.Top!=frame.Height)
            throw new InvalidOperationException("Native capture frame reduces or shifts the client area");
        foreach(var point in new[]{(0,0),(frame.Width/2,0),(frame.Width-1,0),(0,frame.Height/2),(frame.Width-1,frame.Height/2),(0,frame.Height-1),(frame.Width/2,frame.Height-1),(frame.Width-1,frame.Height-1)})
        {
            long packed=(ushort)(frame.X+point.Item1)|((long)(ushort)(frame.Y+point.Item2)<<16);
            if(Native.SendMessage(hwnd,0x0084,0,(nint)packed)!=1)throw new InvalidOperationException("Capture edge consumes client input");
        }
    }
}
