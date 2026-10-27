using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private ContentDialog? activeDialog;
    private ContentDialog? dismissOnExternalFocusDialog;
    private bool DismissSettingsForExternalFocus()
    {
        if(exiting||takingScreenshot||activeDialog is null||!ReferenceEquals(activeDialog,dismissOnExternalFocusDialog))return false;
        // Hide dismisses the settings with Cancel semantics. Its finally restores
        // compact bounds without bringing focus back from the user's document.
        Hide();return true;
    }
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog,bool dismissOnExternalFocus=false)
    {
        EndWindowPointerGesture();
        activeDialog=dialog;dialogOpen=true;CancelSectionHover();CancelPanelFade();sectionPopup.IsOpen=false;preview.IsOpen=false;
        if(dismissOnExternalFocus)dismissOnExternalFocusDialog=dialog;
        var original=new RectInt32(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height);
        double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        bool enlarged=original.Width<480*scale||original.Height<560*scale;
        try
        {
            if(enlarged)
            {
                var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
                int width=Math.Min(area.Width,(int)(520*scale)),height=Math.Min(area.Height,(int)(680*scale));
                AppWindow.MoveAndResize(new RectInt32(Math.Clamp(original.X,area.X,Math.Max(area.X,area.X+area.Width-width)),Math.Clamp(original.Y,area.Y,Math.Max(area.Y,area.Y+area.Height-height)),width,height));
                await Task.Delay(80);
            }
            if(exiting||activeDialog!=dialog||!AppWindow.IsVisible)return ContentDialogResult.None;
            if(dialog.Content is ScrollViewer scroll)scroll.MaxHeight=Math.Max(200,Root.ActualHeight-180);
            return await dialog.ShowAsync();
        }
        finally
        {
            if(enlarged&&!exiting)AppWindow.MoveAndResize(original);
            if(ReferenceEquals(activeDialog,dialog)){activeDialog=null;dialogOpen=false;}
            if(ReferenceEquals(dismissOnExternalFocusDialog,dialog))dismissOnExternalFocusDialog=null;
            if(!exiting&&AppWindow.IsVisible){FocusPanel();WatchPanelFocus();}
        }
    }
}
