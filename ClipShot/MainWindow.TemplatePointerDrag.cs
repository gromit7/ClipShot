using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;

namespace ClipShot;
public sealed partial class MainWindow
{
    private sealed record TemplatePointerRow(string Key, Border Marker);
    private readonly Dictionary<Border,TemplatePointerRow> templateDragRows=[];
    private readonly DispatcherTimer templatePointerTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private bool templatePointerTimerReady;
    private Border? templatePointerRow;
    private Pointer? templatePointer;
    private Native.Point templatePointerStart;
    private string? templatePointerSource;
    private long templatePointerEventAt;
    private bool IsTemplateDragRow(Border row)=>templateDragRows.ContainsKey(row);

    private void RegisterTemplatePointerRow(Border row,string key,Border marker)
    {
        templateDragRows[row]=new(key,marker);
        if(!templatePointerTimerReady)
        {
            templatePointerTimerReady=true;
            templatePointerTimer.Tick+=async(_,_)=>
            {
                // Queued routed events carry their historical button/position.
                // Give them priority over already advanced hardware state.
                if(Environment.TickCount64-templatePointerEventAt<80)return;
                if(!Native.GetCursorPos(out var point)){EndTemplateDrag();return;}
                await UpdateTemplatePointerDrag(point,WindowPrimaryButtonDown());
            };
        }
        row.AddHandler(UIElement.PointerPressedEvent,new PointerEventHandler((_,args)=>
        {
            if(args.Pointer.PointerDeviceType!=PointerDeviceType.Mouse||!args.GetCurrentPoint(row).Properties.IsLeftButtonPressed||
                key[0]=='c'&&IsButtonSource(args.OriginalSource)||!TryTemplateEventPoint(row,args,out var point))return;
            BeginTemplatePointerDrag(row,key,point,args.Pointer);
        }),true);
        row.AddHandler(UIElement.PointerMovedEvent,new PointerEventHandler(async(_,args)=>
        {
            if(templatePointerRow is null||templatePointer is not null&&args.Pointer.PointerId!=templatePointer.PointerId||!TryTemplateEventPoint(row,args,out var point))return;
            templatePointerEventAt=Environment.TickCount64;
            await UpdateTemplatePointerDrag(point,args.GetCurrentPoint(row).Properties.IsLeftButtonPressed);
            if(templateDragInProgress)args.Handled=true;
        }),true);
        row.AddHandler(UIElement.PointerReleasedEvent,new PointerEventHandler(async(_,args)=>
        {
            if(templatePointerRow is null||templatePointer is not null&&args.Pointer.PointerId!=templatePointer.PointerId)return;
            templatePointerEventAt=Environment.TickCount64;
            bool dragging=templateDragInProgress;
            if(TryTemplateEventPoint(row,args,out var point))await UpdateTemplatePointerDrag(point,false);else EndTemplateDrag();
            if(dragging||SuppressTemplateTap())args.Handled=true;
        }),true);
        row.AddHandler(UIElement.PointerCanceledEvent,new PointerEventHandler((_,_)=>
        {if(ReferenceEquals(templatePointerRow,row))EndTemplateDrag();}),true);
        row.AddHandler(UIElement.PointerCaptureLostEvent,new PointerEventHandler(async(_,args)=>
        {
            // A folder Button may have captured the initial press. Its bubbling
            // capture-lost event is not loss of our row's later drag capture.
            if(!ReferenceEquals(args.OriginalSource,row)||!ReferenceEquals(templatePointerRow,row))return;
            if(!WindowPrimaryButtonDown()&&Native.GetCursorPos(out var point))await UpdateTemplatePointerDrag(point,false);
            else EndTemplateDrag();
        }),true);
    }

    // The same state machine is exercised by isolated checks with supplied screen
    // coordinates. It never exports data to OLE or the system clipboard.
    private bool BeginTemplatePointerDrag(Border row,string key,Native.Point point,Pointer? pointer=null)
    {
        if(exiting||dialogOpen||takingScreenshot||windowResizing||editor is not null||openMenus>0||templateDragInProgress||Search.Text.Trim().Length!=0||
            !templateDragRows.TryGetValue(row,out var entry)||entry.Key!=key||!IsTemplateEntryKey(key))return false;
        StopTemplatePointerTracking();
        templatePointerRow=row;templatePointerSource=key;templatePointerStart=point;templatePointer=pointer;
        templatePointerEventAt=Environment.TickCount64;
        if(pointer is not null)
        {
            // Plain clip rows have no Button to retain the press. Capturing them
            // now also delivers a coalesced release after a very short gesture.
            if(key[0]=='c')row.CapturePointer(pointer);
            templatePointerTimer.Start();
        }
        return true;
    }

    private bool TryTemplateEventPoint(Border row,PointerRoutedEventArgs args,out Native.Point point)
    {
        point=default;if(!TryTemplatePointerArea(row,out var raw,out _))return false;
        var p=args.GetCurrentPoint(row).Position;double scale=Root.XamlRoot?.RasterizationScale??1;
        point=new(){X=(int)Math.Round(raw.Left+p.X*scale),Y=(int)Math.Round(raw.Top+p.Y*scale)};return true;
    }

    private async Task<bool> UpdateTemplatePointerDrag(Native.Point point,bool primaryDown,Func<Task<string?>>? nameProvider=null)
    {
        var row=templatePointerRow;string? source=templatePointerSource;
        if(row is null||source is null)return false;
        if(exiting||dialogOpen||takingScreenshot||windowResizing||editor is not null||openMenus>0||!AppWindow.IsVisible||Search.Text.Trim().Length!=0||!IsTemplateDragRow(row))
        {EndTemplateDrag();return false;}
        long dx=(long)point.X-templatePointerStart.X,dy=(long)point.Y-templatePointerStart.Y;
        if(!templateDragInProgress)
        {
            // A click remains a normal click; only six physical pixels of actual
            // movement claim the gesture and suppress subsequent taps.
            if(dx*dx+dy*dy<36){if(!primaryDown)StopTemplatePointerTracking();return false;}
            // A queued release can contain the first delivered movement. It is
            // still a real drag, but there is no down pointer left to capture.
            if(primaryDown&&templatePointer is not null)row.CapturePointer(templatePointer);
            templateDragInProgress=true;templateDraggedEntry=source;templateTapSuppressedUntil=long.MaxValue;
            hoverTimer.Stop();leaveTimer.Stop();hovered=null;preview.IsOpen=false;CancelPanelFade();
        }
        var target=FindTemplatePointerTarget(point);
        ShowTemplatePointerTarget(target);
        if(primaryDown)return false;
        // Clear tracking before releasing capture, whose event can be synchronous.
        // Keep the busy flag throughout an optional naming dialog and Store write.
        StopTemplatePointerTracking();
        try{return target is {} hit&&await ApplyTemplateDrop(source,hit.Entry.Key,hit.Zone,nameProvider);}
        catch{if(!exiting)Status.Text=T("Не удалось переместить шаблон.");return false;}
        finally{EndTemplateDrag();if(!exiting)Render();}
    }

    private (TemplatePointerRow Entry,TemplateDropZone Zone)? FindTemplatePointerTarget(Native.Point point)
    {
        foreach(var pair in templateDragRows)
        {
            if(pair.Value.Key==templateDraggedEntry||!TryTemplatePointerArea(pair.Key,out var raw,out var clipped)||!clipped.Contains(point))continue;
            double scale=Root.XamlRoot?.RasterizationScale??1;
            var zone=TemplateZone((point.Y-raw.Top)/scale,(raw.Bottom-raw.Top)/scale);
            bool child=pair.Value.Key[0]=='c'&&long.TryParse(pair.Value.Key.AsSpan(2),out long id)&&renderedTemplateParents.ContainsKey(id);
            if(templateDraggedEntry?.StartsWith("f:",StringComparison.Ordinal)==true&&(zone==TemplateDropZone.Inside||child))return null;
            return(pair.Value,zone);
        }
        return null;
    }

    private void ShowTemplatePointerTarget((TemplatePointerRow Entry,TemplateDropZone Zone)? target)
    {
        if(templateDropIndicator is not null)templateDropIndicator.Visibility=Visibility.Collapsed;
        templateDropIndicator=null;
        if(target is not {} hit)return;
        var marker=hit.Entry.Marker;templateDropIndicator=marker;
        marker.BorderThickness=hit.Zone switch{TemplateDropZone.Before=>new Thickness(0,3,0,0),TemplateDropZone.After=>new Thickness(0,0,0,3),_=>new Thickness(2)};
        marker.Visibility=Visibility.Visible;
    }

    private bool TryTemplatePointerArea(FrameworkElement row,out SectionScreenArea raw,out SectionScreenArea clipped)
    {
        raw=clipped=default;
        try
        {
        bool popup=false;
        for(var node=(DependencyObject?)row;node is not null;node=VisualTreeHelper.GetParent(node))
            if(ReferenceEquals(node,sectionBorder)){popup=true;break;}
        if(popup)
        {
            if(sectionBorder is null||!TrySectionPopupArea(sectionPopup,sectionBorder,out var borderArea))return false;
            double scale=Root.XamlRoot?.RasterizationScale??1;
            SectionScreenArea Area(FrameworkElement element)
            {
                var p=element.TransformToVisual(sectionBorder).TransformPoint(new Windows.Foundation.Point());
                return new(borderArea.Left+p.X*scale,borderArea.Top+p.Y*scale,borderArea.Left+(p.X+element.ActualWidth)*scale,borderArea.Top+(p.Y+element.ActualHeight)*scale);
            }
            raw=Area(row);clipped=IntersectSectionAreas(raw,borderArea);
            for(var node=VisualTreeHelper.GetParent(row);node is not null;node=VisualTreeHelper.GetParent(node))
                if(node is ScrollViewer viewport)clipped=IntersectSectionAreas(clipped,Area(viewport));
        }
        else
        {
            if(!TrySectionElementArea(row,out raw)||ClipSectionElementArea(row,raw) is not {} area)return false;
            clipped=area;
        }
        return row.Visibility==Visibility.Visible&&clipped.Right>clipped.Left&&clipped.Bottom>clipped.Top;
        }
        catch(InvalidOperationException){return false;}
        catch(ArgumentException){return false;}
    }

    private void StopTemplatePointerTracking()
    {
        templatePointerTimer.Stop();var row=templatePointerRow;var pointer=templatePointer;
        templatePointerRow=null;templatePointerSource=null;templatePointer=null;
        if(row is not null&&pointer is not null)row.ReleasePointerCapture(pointer);
    }
}
