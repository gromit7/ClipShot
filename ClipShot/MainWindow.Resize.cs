using Microsoft.UI.Xaml.Controls;
namespace ClipShot;
public sealed partial class MainWindow
{
    private bool windowResizing;
    private Windows.Graphics.SizeInt32 resizeStartSize;
    private void BeginWindowResize()
    {
        if(!windowResizing)resizeStartSize=AppWindow.Size;
        windowResizing=true;hoverTimer.Stop();leaveTimer.Stop();hovered=null;preview.IsOpen=false;sectionPopup.IsOpen=false;
    }
    private void EndWindowResize()
    {
        if(!windowResizing)return;windowResizing=false;
        if(AppWindow.Size.Width!=resizeStartSize.Width||AppWindow.Size.Height!=resizeStartSize.Height)
            store.SetMany(new Dictionary<string,string>{{"width",AppWindow.Size.Width.ToString()},{"height",AppWindow.Size.Height.ToString()},{"panelautofit","0"}});
        DispatcherQueue.TryEnqueue(PositionSection);
    }
    private void ResizeGripPressed(object sender,Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
    {
        if(!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)return;
        e.Handled=true;BeginWindowResize();Native.GetCursorPos(out var mouse);Native.ReleaseCapture();
        nint coordinates=(nint)unchecked((int)(((uint)(ushort)mouse.Y<<16)|(ushort)mouse.X));
        Native.SendMessage(hwnd,0x00A1,17,coordinates);
        EndWindowResize();
    }
}
