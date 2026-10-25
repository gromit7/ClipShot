using Microsoft.UI.Xaml;
using Windows.Graphics;

namespace ClipShot;
public sealed partial class MainWindow
{
    // Проверяем те же переходы состояния и геометрию, что использует настоящий
    // pointer capture. Доставку физических событий мыши проверяют отдельно.
    private async Task RunWindowDragChecks(List<string> results)
    {
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        var position=AppWindow.Position;var size=AppWindow.Size;
        string autofit=store.Get("panelautofit","1");
        const uint pointerId=701;
        Native.Point start=new(){X=300,Y=300};
        try
        {
            OpenSearchClick(this,new());
            Check(!IsWindowDragSource(Search)&&!IsWindowDragSource(ManualUpdateLink)&&!IsWindowDragSource(sectionHeadings["Сегодня"])&&!IsWindowDragSource(visible[0].Row),"search, clips, command buttons and section headings cannot start window dragging");
            Check(!IsWindowDragSource(SearchGlyph)&&IsWindowDragSource(HistoryScroll)&&IsWindowDragSource(Root),"magnifier is a command while empty history and root remain drag surfaces");
            Search.Text="drag check";await Task.Delay(80);
            Check(ReferenceEquals(Microsoft.UI.Xaml.Input.FocusManager.GetFocusedElement(Root.XamlRoot),Search)&&windowPointerGesture==WindowPointerGesture.None&&AppWindow.Position.X==position.X&&AppWindow.Position.Y==position.Y,"focusing and typing in search leaves window drag inactive and position unchanged");
            Search.Text="";await Task.Delay(80);

            BeginWindowPointerGesture(WindowPointerGesture.Move,start,pointerId);
            UpdateWindowPointerGesture(new(){X=330,Y=320},false);
            var stopped=AppWindow.Position;
            UpdateWindowPointerGesture(new(){X=390,Y=370},true);
            Check(stopped.X==position.X&&stopped.Y==position.Y&&AppWindow.Position.X==position.X&&AppWindow.Position.Y==position.Y&&!windowResizing&&!windowGestureTimer.IsEnabled,"movement without a held button cancels the gesture and cannot make the window follow later movement");

            BeginWindowPointerGesture(WindowPointerGesture.Move,start,pointerId);
            UpdateWindowPointerGesture(new(){X=324,Y=312},true);
            Check(AppWindow.Position.X==position.X+24&&AppWindow.Position.Y==position.Y+12,"drag displacement uses the original screen position rather than moving client coordinates");
            EndWindowPointerGesture(pointerId+1);
            Check(windowResizing&&windowPointerGesture==WindowPointerGesture.Move,"an unrelated pointer cannot finish the captured mouse gesture");
            EndWindowPointerGesture(pointerId);
            stopped=AppWindow.Position;UpdateWindowPointerGesture(new(){X=380,Y=350},true);
            Check(windowPointerGesture==WindowPointerGesture.None&&!windowResizing&&!windowGestureTimer.IsEnabled&&AppWindow.Position.X==stopped.X&&AppWindow.Position.Y==stopped.Y,"release or capture loss of the captured pointer clears drag state before any later movement");
            Check(store.Get("panelautofit","1")==autofit,"moving a window preserves automatic panel sizing");

            BeginWindowPointerGesture(WindowPointerGesture.Resize,start,pointerId);
            UpdateWindowPointerGesture(new(){X=340,Y=320},true);
            Check(AppWindow.Size.Width==size.Width+40&&AppWindow.Size.Height==size.Height+20&&!sectionPopup.IsOpen,"resize grip changes the pixel size while keeping its side popup closed");
            EndWindowPointerGesture(pointerId);
            Check(store.Get("panelautofit","1")=="0"&&!windowResizing,"finishing a real resize saves its size and turns off automatic sizing");

            BeginWindowPointerGesture(WindowPointerGesture.Move,start,pointerId);Hide();
            Check(windowPointerGesture==WindowPointerGesture.None&&!windowGestureTimer.IsEnabled&&!AppWindow.IsVisible,"hiding cancels an active gesture and its button watchdog");
            ShowNearCursor();CollapseSection();suppressedHoverSection=null;Render();
            QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);
            long deadline=Environment.TickCount64+1800;
            while(!sectionPopup.IsOpen&&Environment.TickCount64<deadline)await Task.Delay(20);
            Check(sectionPopup.IsOpen&&activeSection=="Сегодня"&&!windowResizing,"section hover works again after a drag was cancelled");
        }
        finally
        {
            EndWindowPointerGesture();AppWindow.Move(position);AppWindow.Resize(size);
            await Task.Delay(80);
            store.SetMany(new Dictionary<string,string>{{"panelautofit",autofit},{"width",size.Width.ToString()},{"height",size.Height.ToString()}});
            PositionSection();
        }
    }
}
