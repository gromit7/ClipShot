using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;

namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunInlinePanelChecks(List<string> results)
    {
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        async Task Settle()
        {
            QueueInlinePanelFit();
            for(int i=0;i<8;i++){await Task.Delay(40);Root.UpdateLayout();}
        }
        async Task CheckLargeSideSection(string title)
        {
            string previousLayout=store.Get("sectionlayout","side");
            string? previousSection=activeSection;bool previousInitialized=sectionInitialized;
            var beforeSide=new RectInt32(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height);
            // Здесь проверяем viewport и скролл, а физический hover/leave — отдельно.
            openMenus++;
            try
            {
                store.Set("sectionlayout","side");activeSection=title;sectionInitialized=true;Render();
                await Task.Delay(120);Root.UpdateLayout();PositionSection();await Task.Delay(80);Root.UpdateLayout();
                var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
                double dpi=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
                var origin=new Native.Point();Native.ClientToScreen(hwnd,ref origin);
                var main=new RectInt32(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height);
                var scroll=(ScrollViewer)sectionBorder!.Child;
                Check(sectionPopup.IsOpen&&sectionBorder.ActualHeight*dpi>=area.Height-18&&scroll.ViewportHeight*dpi>=area.Height-16-12*dpi,$"large {title} side section uses almost the entire monitor work height");
                double x=origin.X+sectionPopup.HorizontalOffset*dpi,y=origin.Y+sectionPopup.VerticalOffset*dpi;
                double right=x+sectionBorder.ActualWidth*dpi,bottom=y+sectionBorder.ActualHeight*dpi;
                var source=sectionHeadings[title];var heading=source.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point());
                double headingLeft=origin.X+heading.X*dpi,headingRight=headingLeft+source.ActualWidth*dpi;
                double headingTop=origin.Y+heading.Y*dpi,headingBottom=headingTop+source.ActualHeight*dpi;
                Check(x>=area.X+7&&y>=area.Y+7&&right<=area.X+area.Width-7&&bottom<=area.Y+area.Height-7&&source.Visibility==Visibility.Visible&&headingTop>=area.Y&&headingBottom<=area.Y+area.Height&&(right<=headingLeft||x>=headingRight),$"large {title} popup stays within work area and leaves its source heading visible");
                for(int attempt=0;attempt<30;attempt++)
                {
                    scroll.ChangeView(null,scroll.ScrollableHeight,null,true);await Task.Delay(20);Root.UpdateLayout();
                    if(scroll.ScrollableHeight>0&&Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<=2)break;
                }
                var last=visible.Last().Row;double lastTop=last.TransformToVisual(scroll).TransformPoint(new Windows.Foundation.Point()).Y;
                Check(scroll.ScrollableHeight>0&&Math.Abs(scroll.VerticalOffset-scroll.ScrollableHeight)<=2&&lastTop>=-2&&lastTop+last.ActualHeight<=scroll.ActualHeight+2,$"last row of large {title} side section is reachable in the real scroll viewport");
                PositionSection();
                Check(AppWindow.Position.X==main.X&&AppWindow.Position.Y==main.Y&&AppWindow.Size.Width==main.Width&&AppWindow.Size.Height==main.Height,$"positioning a large {title} popup leaves the main window unchanged");
                if(title=="Сегодня")
                {
                    double largeHeight=sectionBorder.ActualHeight;
                    activeSection="Шаблоны";Render();await Task.Delay(100);Root.UpdateLayout();PositionSection();await Task.Delay(60);Root.UpdateLayout();
                    Check(visible.Count==1&&visible[0].Clip.Template&&((area.Height-16)<=160*dpi+2||sectionBorder.ActualHeight<largeHeight-1),"switching from a long today list to one template shrinks the existing side popup");
                }
            }
            finally
            {
                openMenus=Math.Max(0,openMenus-1);store.Set("sectionlayout",previousLayout);activeSection=previousSection;sectionInitialized=previousInitialized;
                compactResizing=true;try{AppWindow.MoveAndResize(beforeSide);}finally{compactResizing=false;}
                Render();await Settle();
            }
        }
        var originalIds=store.All().Select(c=>c.Id).ToHashSet();
        var originalExpanded=expanded.ToDictionary(p=>p.Key,p=>p.Value);
        string layout=store.Get("sectionlayout","side"),autofit=store.Get("panelautofit","1"),query=Search.Text;
        string? section=activeSection;bool initialized=sectionInitialized;
        var original=new RectInt32(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height);
        try
        {
            store.Set("sectionlayout","inline");Search.Text="";expanded["Сегодня"]=false;expanded["Шаблоны"]=false;
            Render();ShowNearCursor();await Settle();
            var area=DisplayArea.Primary.WorkArea;
            int collapsed=AppWindow.Size.Height,width=AppWindow.Size.Width;
            Check(AppWindow.Position.X+width==area.X+area.Width&&AppWindow.Position.Y>=area.Y&&AppWindow.Position.Y+collapsed<=area.Y+area.Height,"inline panel docks to the primary work area right edge and stays above the taskbar");
            Check(AppWindow.Position.Y+collapsed==area.Y+area.Height,"fully collapsed inline categories dock to the bottom right above the taskbar");
            ToggleSection("Сегодня");await Settle();
            int opened=AppWindow.Size.Height;
            Check(opened>collapsed&&AppWindow.Size.Width==width,"opening an inline section grows the real window vertically without changing its width");
            ToggleSection("Сегодня");await Settle();
            Check(AppWindow.Size.Height<opened&&Math.Abs(AppWindow.Size.Height-collapsed)<=1,"closing an inline section shrinks the window back to its measured content");

            double scale=Root.XamlRoot.RasterizationScale;
            int manualWidth=Math.Min(area.Width,(int)Math.Ceiling(340*scale));
            AppWindow.Resize(new SizeInt32(manualWidth,AppWindow.Size.Height));await Settle();
            ToggleSection("Сегодня");await Settle();
            Check(AppWindow.Size.Width==manualWidth&&AppWindow.Position.X+AppWindow.Size.Width==area.X+area.Width,"inline fitting preserves a manually selected width and restores its right edge");

            int count=(int)Math.Ceiling(area.Height/(40*scale))+20;
            for(int i=0;i<count;i++)store.Add("text","synthetic inline overflow "+i);
            expanded["Сегодня"]=true;Render();await Settle();
            Check(AppWindow.Size.Height<=area.Height&&AppWindow.Position.Y>=area.Y&&AppWindow.Position.Y+AppWindow.Size.Height<=area.Y+area.Height,"large inline history remains inside the primary work area");
            Check(HistoryScroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto&&HistoryScroll.ScrollableHeight>0,"overflowing inline history uses the existing vertical scroll viewer");
            await CheckLargeSideSection("Сегодня");

            foreach(var clip in store.All())if(!originalIds.Contains(clip.Id))store.Template(clip.Id,true);
            expanded["Сегодня"]=false;expanded["Шаблоны"]=true;Render();await Settle();
            var searchBottom=SearchToggle.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,SearchToggle.ActualHeight)).Y;
            var updateBottom=ManualUpdateLink.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,ManualUpdateLink.ActualHeight)).Y;
            Check(TemplateGroups.Children.Count==0&&HistoryScroll.ScrollableHeight>0&&searchBottom<=Root.ActualHeight+1&&updateBottom<=Root.ActualHeight+1,"large inline templates share history scrolling and keep search and update commands visible");
            await CheckLargeSideSection("Шаблоны");

            var protectedSize=AppWindow.Size;dialogOpen=true;expanded["Шаблоны"]=false;Render();FitInlinePanel();await Task.Delay(80);
            Check(AppWindow.Size.Height==protectedSize.Height&&AppWindow.Size.Width==protectedSize.Width,"inline sizing leaves a settings or help dialog window untouched");
            dialogOpen=false;Render();await Settle();
            Check(AppWindow.Size.Height<protectedSize.Height,"inline content is measured again after the dialog guard is released");
            protectedSize=AppWindow.Size;BeginWindowResize();expanded["Шаблоны"]=true;Render();FitInlinePanel();
            Check(AppWindow.Size.Height==protectedSize.Height&&AppWindow.Size.Width==protectedSize.Width,"inline fitting does not fight an active manual resize gesture");EndWindowResize();await Settle();

            store.Set("sectionlayout","side");Render();await Task.Delay(80);
            var sidePosition=AppWindow.Position;var sideSize=AppWindow.Size;FitInlinePanel();
            Check(AppWindow.Position.X==sidePosition.X&&AppWindow.Position.Y==sidePosition.Y&&AppWindow.Size.Width==sideSize.Width&&AppWindow.Size.Height==sideSize.Height,"inline geometry never repositions or resizes the side layout");
            activeSection=null;sectionInitialized=true;Render();await Task.Delay(120);
            Check(!sectionPopup.IsOpen&&AppWindow.Position.X+AppWindow.Size.Width==area.X+area.Width&&AppWindow.Position.Y+AppWindow.Size.Height==area.Y+area.Height,"fully collapsed side categories dock to the primary work area bottom right");
            OpenSearchClick(this,new());await Task.Delay(80);
            AppWindow.Move(new PointInt32(area.X,area.Y));var searchingPosition=AppWindow.Position;DockCollapsedPanel();
            Check(AppWindow.Position.X==searchingPosition.X&&AppWindow.Position.Y==searchingPosition.Y,"an open search prevents collapsed-panel docking while the user is working");CollapseSearch(false);
        }
        finally
        {
            dialogOpen=false;if(windowResizing)EndWindowResize();
            foreach(var clip in store.All())if(!originalIds.Contains(clip.Id))store.Delete(clip.Id);
            expanded.Clear();foreach(var pair in originalExpanded)expanded[pair.Key]=pair.Value;
            store.SetMany(new Dictionary<string,string>{{"sectionlayout",layout},{"panelautofit",autofit},{"width",original.Width.ToString()},{"height",original.Height.ToString()}});
            activeSection=section;sectionInitialized=initialized;Search.Text=query;Render();
            compactResizing=true;try{AppWindow.MoveAndResize(original);}finally{compactResizing=false;}
            await Task.Delay(80);PositionSection();
        }
    }
}
