using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ClipShot;
public sealed partial class MainWindow
{
    // Shared screen geometry for template drag hit testing; section lifetime is click controlled.
    private readonly record struct SectionScreenArea(double Left,double Top,double Right,double Bottom)
    {
        internal bool Contains(Native.Point point)=>point.X>=Left&&point.X<Right&&point.Y>=Top&&point.Y<Bottom;
    }
    private bool TrySectionElementArea(FrameworkElement element,out SectionScreenArea area)
    {
        area=default;
        if(Root.XamlRoot is null||element.Visibility!=Visibility.Visible||element.ActualWidth<=0||element.ActualHeight<=0)return false;
        double scale=Root.XamlRoot.RasterizationScale;if(!double.IsFinite(scale)||scale<=0)return false;
        try
        {
            var origin=new Native.Point();if(!Native.ClientToScreen(hwnd,ref origin))return false;
            var position=element.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
            area=new(origin.X+position.X*scale,origin.Y+position.Y*scale,origin.X+(position.X+element.ActualWidth)*scale,origin.Y+(position.Y+element.ActualHeight)*scale);return true;
        }
        catch(InvalidOperationException){return false;}catch(ArgumentException){return false;}
    }
    private SectionScreenArea? ClipSectionElementArea(FrameworkElement element,SectionScreenArea area)
    {
        if(!TrySectionElementArea(Root,out var client))return null;
        area=IntersectSectionAreas(area,client);
        for(var parent=VisualTreeHelper.GetParent(element);parent is not null;parent=VisualTreeHelper.GetParent(parent))
            if(ReferenceEquals(parent,HistoryScroll)&&TrySectionElementArea(HistoryScroll,out var viewport)){area=IntersectSectionAreas(area,viewport);break;}
        return area.Right>area.Left&&area.Bottom>area.Top?area:null;
    }
    private static SectionScreenArea IntersectSectionAreas(SectionScreenArea first,SectionScreenArea second)
        =>new(Math.Max(first.Left,second.Left),Math.Max(first.Top,second.Top),Math.Min(first.Right,second.Right),Math.Min(first.Bottom,second.Bottom));
    private bool TrySectionPopupArea(Popup popup,FrameworkElement child,out SectionScreenArea area)
    {
        area=default;if(!popup.IsOpen||Root.XamlRoot is null||child.ActualWidth<=0||child.ActualHeight<=0)return false;
        double scale=Root.XamlRoot.RasterizationScale;var origin=new Native.Point();
        if(!double.IsFinite(scale)||scale<=0||!Native.ClientToScreen(hwnd,ref origin))return false;
        area=new(origin.X+popup.HorizontalOffset*scale,origin.Y+popup.VerticalOffset*scale,origin.X+(popup.HorizontalOffset+child.ActualWidth)*scale,origin.Y+(popup.VerticalOffset+child.ActualHeight)*scale);return true;
    }
}
