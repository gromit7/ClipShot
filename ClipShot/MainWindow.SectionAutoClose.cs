using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;

namespace ClipShot;
public sealed partial class MainWindow
{
    private sealed class HoverSectionState(bool side)
    {
        internal readonly bool Side=side;
        internal long? OutsideSince;
        internal bool OutsideObserved;
    }
    private readonly record struct SectionScreenArea(double Left,double Top,double Right,double Bottom)
    {
        internal bool Contains(Native.Point point)=>point.X>=Left&&point.X<Right&&point.Y>=Top&&point.Y<Bottom;
    }
    private readonly DispatcherTimer sectionAutoCloseTimer=new(){Interval=TimeSpan.FromMilliseconds(60)};
    private readonly Dictionary<string,HoverSectionState> hoverOpenedSections=[];
    private readonly Dictionary<string,List<FrameworkElement>> sectionAutoCloseRows=[];
    private bool sectionAutoCloseReady;
    private Native.Point? lastSectionAutoClosePointer;
    // Isolated checks inject desktop observations without moving the system cursor.
    private Func<Native.Point?>? sectionAutoClosePointerOverride;
    private Func<long>? sectionAutoCloseClockOverride;

    private void RegisterSectionAutoCloseGroup(string title)
    {
        if(sectionHeadings.Count==1)sectionAutoCloseRows.Clear();
        sectionAutoCloseRows[title]=[];
    }
    private void RegisterSectionAutoCloseRow(string title,FrameworkElement row)
    {
        if(sectionAutoCloseRows.TryGetValue(title,out var rows))rows.Add(row);
    }
    private Native.Point? ReadSectionAutoClosePointer()
    {
        if(sectionAutoClosePointerOverride is not null)return sectionAutoClosePointerOverride();
        if(isolated)return null;
        return Native.GetCursorPos(out var point)?point:null;
    }
    private void TrackSectionAutoClose(string title,bool pointerObserved)
    {
        if(!pointerObserved||(isolated&&sectionAutoClosePointerOverride is null))return;
        if(SideSections)hoverOpenedSections.Clear();
        else foreach(string old in hoverOpenedSections.Where(pair=>pair.Value.Side).Select(pair=>pair.Key).ToArray())hoverOpenedSections.Remove(old);
        hoverOpenedSections[title]=new(SideSections);lastSectionAutoClosePointer=ReadSectionAutoClosePointer();
        if(!sectionAutoCloseReady){sectionAutoCloseTimer.Tick+=(_,_)=>PollSectionAutoClose();sectionAutoCloseReady=true;}
        sectionAutoCloseTimer.Start();
    }
    private void KeepSectionAutoClose(string title,bool pointerObserved)
    {
        if(!pointerObserved)return;
        if(!hoverOpenedSections.TryGetValue(title,out var state)){TrackSectionAutoClose(title,true);return;}
        state.OutsideSince=null;state.OutsideObserved=false;lastSectionAutoClosePointer=ReadSectionAutoClosePointer();
    }
    private void ObserveOpenSectionPointer(string title)
    {
        if(CanOpenSectionOnHover&&(SideSections?activeSection==title&&sectionPopup.IsOpen:expanded.GetValueOrDefault(title)))KeepSectionAutoClose(title,true);
    }
    private void ForgetSectionAutoClose(string? title=null)
    {
        if(title is null)hoverOpenedSections.Clear();else hoverOpenedSections.Remove(title);
        if(hoverOpenedSections.Count==0){sectionAutoCloseTimer.Stop();lastSectionAutoClosePointer=null;}
    }
    private void PollSectionAutoClose()
    {
        if(hoverOpenedSections.Count==0){sectionAutoCloseTimer.Stop();return;}
        if(exiting||!AppWindow.IsVisible||dialogOpen||takingScreenshot||windowResizing||panelFading||editor is not null||Search.Text.Trim().Length!=0)
        {ForgetSectionAutoClose();return;}
        var point=ReadSectionAutoClosePointer();
        bool moved=point is {} current&&lastSectionAutoClosePointer is {} previous&&(current.X!=previous.X||current.Y!=previous.Y);
        if(point is not null)lastSectionAutoClosePointer=point;
        long now=sectionAutoCloseClockOverride?.Invoke()??Environment.TickCount64;bool changed=false;
        foreach(var pair in hoverOpenedSections.ToArray())
        {
            string title=pair.Key;var state=pair.Value;
            if(state.Side!=SideSections||!sectionHeadings.ContainsKey(title)||(state.Side?(activeSection!=title||!sectionPopup.IsOpen):!expanded.GetValueOrDefault(title)))
            {ForgetSectionAutoClose(title);continue;}
            if(openMenus>0||templateDragInProgress){state.OutsideSince=null;state.OutsideObserved=true;continue;}
            if(rendering||point is null||!TrySectionAutoCloseAreas(title,state.Side,out var areas))
            {state.OutsideSince=null;if(moved)state.OutsideObserved=true;continue;}
            if(areas.Any(area=>area.Contains(point.Value)))
            {state.OutsideSince=null;state.OutsideObserved=false;continue;}
            // Removing/recreating a heading during Render can leave the pointer
            // outside its new bounds. A stationary cursor alone never starts close.
            if(!moved&&!state.OutsideObserved)continue;
            state.OutsideObserved=true;state.OutsideSince??=now;
            if(now-state.OutsideSince.Value<300)continue;
            ForgetSectionAutoClose(title);changed=true;
            if(state.Side){activeSection=null;sectionInitialized=true;sectionPopup.IsOpen=false;}
            else expanded[title]=false;
        }
        if(changed){preview.IsOpen=false;hoverTimer.Stop();hovered=null;Render();selected=-1;Highlight();}
    }
    private bool TrySectionAutoCloseAreas(string title,bool side,out List<SectionScreenArea> areas)
    {
        areas=[];
        if(Root.XamlRoot is null||!sectionHeadings.TryGetValue(title,out var heading)||!TrySectionElementArea(heading,out var headingArea))return false;
        if(ClipSectionElementArea(heading,headingArea) is {} visibleHeading)areas.Add(visibleHeading);
        SectionScreenArea? content=null;
        if(side)
        {
            if(sectionBorder is null||!TrySectionPopupArea(sectionPopup,sectionBorder,out var listArea))return false;
            areas.Add(listArea);content=listArea;
            if(SectionGapArea(headingArea,listArea) is {} gap)areas.Add(gap);
        }
        else if(sectionAutoCloseRows.TryGetValue(title,out var rows)&&rows.Count>0)
        {
            // Rows of one group are contiguous. Two endpoint transforms suffice;
            // polling a large inline history must not walk every clip on the UI thread.
            if(!TrySectionElementArea(rows[0],out var first)||!TrySectionElementArea(rows[^1],out var last))return false;
            var raw=new SectionScreenArea(Math.Min(first.Left,last.Left),Math.Min(first.Top,last.Top),Math.Max(first.Right,last.Right),Math.Max(first.Bottom,last.Bottom));
            if(ClipSectionElementArea(rows[0],raw) is {} area){areas.Add(area);content=area;}
            if(content is {} inlineRows&&ClipSectionElementArea(heading,new(Math.Min(headingArea.Left,inlineRows.Left),Math.Min(headingArea.Top,inlineRows.Top),Math.Max(headingArea.Right,inlineRows.Right),Math.Max(headingArea.Bottom,inlineRows.Bottom))) is {} inlineGroup)areas.Add(inlineGroup);
        }
        bool ownPreview=preview.IsOpen&&hoverAnchor is not null&&(side?sectionRows.Children.Contains(hoverAnchor):sectionAutoCloseRows.TryGetValue(title,out var ownRows)&&ownRows.Contains(hoverAnchor));
        if(ownPreview&&preview.IsOpen&&preview.Child is FrameworkElement child&&TrySectionPopupArea(preview,child,out var previewArea))
        {
            areas.Add(previewArea);
            if(content is {} source&&SectionGapArea(source,previewArea) is {} gap)areas.Add(gap);
        }
        return true;
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
    private SectionScreenArea? SectionGapArea(SectionScreenArea first,SectionScreenArea second)
    {
        double scale=Root.XamlRoot?.RasterizationScale??1,padding=3*scale;
        double top=Math.Max(first.Top,second.Top),bottom=Math.Min(first.Bottom,second.Bottom);
        if(bottom<=top)return null;
        if(second.Left>=first.Right&&second.Left-first.Right<=32*scale)return new(first.Right-padding,top,second.Left+padding,bottom);
        if(first.Left>=second.Right&&first.Left-second.Right<=32*scale)return new(second.Right-padding,top,first.Left+padding,bottom);
        return null;
    }
}
