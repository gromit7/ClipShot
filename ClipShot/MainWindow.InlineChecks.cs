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

            foreach(var clip in store.All())if(!originalIds.Contains(clip.Id))store.Template(clip.Id,true);
            expanded["Сегодня"]=false;expanded["Шаблоны"]=true;Render();await Settle();
            var searchBottom=SearchToggle.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,SearchToggle.ActualHeight)).Y;
            var updateBottom=ManualUpdateLink.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,ManualUpdateLink.ActualHeight)).Y;
            Check(TemplateGroups.Children.Count==0&&HistoryScroll.ScrollableHeight>0&&searchBottom<=Root.ActualHeight+1&&updateBottom<=Root.ActualHeight+1,"large inline templates share history scrolling and keep search and update commands visible");

            var protectedSize=AppWindow.Size;dialogOpen=true;expanded["Шаблоны"]=false;Render();FitInlinePanel();await Task.Delay(80);
            Check(AppWindow.Size.Height==protectedSize.Height&&AppWindow.Size.Width==protectedSize.Width,"inline sizing leaves a settings or help dialog window untouched");
            dialogOpen=false;Render();await Settle();
            Check(AppWindow.Size.Height<protectedSize.Height,"inline content is measured again after the dialog guard is released");
            protectedSize=AppWindow.Size;BeginWindowResize();expanded["Шаблоны"]=true;Render();FitInlinePanel();
            Check(AppWindow.Size.Height==protectedSize.Height&&AppWindow.Size.Width==protectedSize.Width,"inline fitting does not fight an active manual resize gesture");EndWindowResize();await Settle();

            store.Set("sectionlayout","side");Render();await Task.Delay(80);
            var sidePosition=AppWindow.Position;var sideSize=AppWindow.Size;FitInlinePanel();
            Check(AppWindow.Position.X==sidePosition.X&&AppWindow.Position.Y==sidePosition.Y&&AppWindow.Size.Width==sideSize.Width&&AppWindow.Size.Height==sideSize.Height,"inline geometry never repositions or resizes the side layout");
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
