using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly Dictionary<Border,Border> clipPressOverlays=[];

    private void AttachClipFeedback(Border row,Grid contents)
    {
        // An overlay changes neither the row's size nor the drag/drop hit target.
        var overlay=new Border{Background=ClipBrush("ClipIndicatorBrush"),Opacity=0,IsHitTestVisible=false};
        contents.Children.Add(overlay);clipPressOverlays[row]=overlay;
        row.PointerPressed+=(_,args)=>
        {
            if(!IsButtonSource(args.OriginalSource)&&args.GetCurrentPoint(row).Properties.IsLeftButtonPressed)
                overlay.Opacity=.14;
        };
        row.PointerReleased+=(_,_)=>overlay.Opacity=0;
        row.PointerCanceled+=(_,_)=>overlay.Opacity=0;
        row.PointerCaptureLost+=(_,_)=>overlay.Opacity=0;
        row.PointerExited+=(_,_)=>overlay.Opacity=0;
    }

    private async Task ShowCopyFeedback(long clipId)
    {
        var row=visible.FirstOrDefault(item=>item.Clip.Id==clipId).Row;
        if(row is null||!clipPressOverlays.TryGetValue(row,out var overlay))return;
        overlay.Opacity=.22;
        // Keep quick taps visible for a few frames before a side list closes or
        // the selected clip moves to the front. Keyboard copying uses this too.
        await Task.Delay(90);
        overlay.Opacity=0;
    }
}
