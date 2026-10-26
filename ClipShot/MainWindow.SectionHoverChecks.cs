using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Shapes;

namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunSectionHoverChecks(List<string> results)
    {
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        async Task Await(Func<bool> ready)
        {
            var deadline=Environment.TickCount64+1800;
            while(!ready()&&Environment.TickCount64<deadline)await Task.Delay(20);
        }
        var original=store.All().Select(c=>(c.Id,c.Template,c.Seen,c.Text)).ToArray();
        CollapseSection();suppressedHoverSection=null;Render();
        var focus=FocusManager.GetFocusedElement(Root.XamlRoot);
        var today=sectionHeadings["Сегодня"];
        QueueSectionHover("Сегодня",today);
        Check(!sectionPopup.IsOpen&&sectionHoverTimer.IsEnabled,"hover waits briefly instead of flashing open while crossing a heading");
        await Await(()=>sectionPopup.IsOpen);
        Check(sectionPopup.IsOpen&&activeSection=="Сегодня"&&selected==-1,"today opens on hover without activating or selecting a clip");
        Check(ReferenceEquals(focus,FocusManager.GetFocusedElement(Root.XamlRoot)),"hover preserves the existing keyboard focus without opening search");
        today=sectionHeadings["Сегодня"];QueueSectionHover("Сегодня",today);
        Check(!sectionHoverTimer.IsEnabled&&ReferenceEquals(today,sectionHeadings["Сегодня"]),"hovering an open heading does not rebuild or toggle its rows");
        var templates=sectionHeadings["Шаблоны"];
        QueueSectionHover("Шаблоны",templates);LeaveSectionHover("Шаблоны",templates);await Task.Delay(280);
        Check(activeSection=="Сегодня"&&sectionPopup.IsOpen,"leaving before the delay keeps the previous section steady");
        QueueSectionHover("Шаблоны",templates);await Await(()=>activeSection=="Шаблоны");
        Check(activeSection=="Шаблоны"&&sectionPopup.IsOpen&&visible.Count==1&&visible[0].Clip.Template&&selected==-1,"templates also open without a click and do not choose a template");
        QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);QueueSectionHover("Шаблоны",sectionHeadings["Шаблоны"]);await Task.Delay(280);
        Check(activeSection=="Шаблоны"&&!sectionHoverTimer.IsEnabled,"returning to an open section cancels an earlier pending switch");
        ToggleSection("Шаблоны");QueueSectionHover("Шаблоны",sectionHeadings["Шаблоны"]);await Task.Delay(280);
        Check(!sectionPopup.IsOpen,"click can close a hovered panel without immediately reopening it");
        LeaveSectionHover("Шаблоны",sectionHeadings["Шаблоны"]);QueueSectionHover("Шаблоны",sectionHeadings["Шаблоны"]);await Await(()=>sectionPopup.IsOpen);
        Check(sectionPopup.IsOpen&&activeSection=="Шаблоны","leaving and reentering a clicked heading enables hover again");
        OpenSearchClick(this,new());await Task.Delay(50);QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);Search.Text="EXAMPLE";await Task.Delay(280);
        Check(!sectionPopup.IsOpen&&!sectionHoverTimer.IsEnabled&&Search.Text=="EXAMPLE","search cancels a delayed hover and keeps the typed query");
        Search.Text="";await Task.Delay(80);store.Set("sectionlayout","inline");expanded["Сегодня"]=false;Render();await Task.Delay(80);
        suppressedHoverSection=null;QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);await Await(()=>expanded.GetValueOrDefault("Сегодня"));
        Check(expanded.GetValueOrDefault("Сегодня")&&!sectionPopup.IsOpen&&selected==-1,$"inline layout also expands on hover without selecting a row (expanded={expanded.GetValueOrDefault("Сегодня")}, popup={sectionPopup.IsOpen}, selected={selected})");
        expanded["Сегодня"]=true;
        store.Set("sectionlayout","side");CollapseSection();Render();
        dialogOpen=true;QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);
        Check(!sectionHoverTimer.IsEnabled,"a settings or help dialog cannot trigger hover behind it");dialogOpen=false;
        QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);Hide();await Task.Delay(280);
        Check(!AppWindow.IsVisible&&!sectionPopup.IsOpen&&!sectionHoverTimer.IsEnabled,"hiding cancels pending hover so no detached popup appears later");
        Check(original.SequenceEqual(store.All().Select(c=>(c.Id,c.Template,c.Seen,c.Text))),"opening and switching headings leaves synthetic history and templates unchanged");
        ShowNearCursor();CollapseSection();suppressedHoverSection=null;Render();ToggleSection("Сегодня");
        await RunSectionAutoCloseChecks(results);

        string theme=store.Get("theme","Default");
        store.Set("theme","CatPink");ApplyTheme();Render();await Task.Delay(80);
        Check(activeTheme.Id=="CatPink"&&Root.RequestedTheme==ElementTheme.Light&&catArtwork is {IsHitTestVisible:false},"pink cat theme uses a light palette and noninteractive native artwork");
        Check(catArtwork?.Visibility==Visibility.Visible&&catArtwork.ActualWidth==76&&catArtwork.ActualHeight==22,"both cats fit the existing compact footer without resizing the panel");
        var colors=catArtwork!.Children.OfType<Ellipse>().Select(s=>(s.Fill as Microsoft.UI.Xaml.Media.SolidColorBrush)?.Color).ToArray();
        Check(colors.Contains(ThemeColor("#28232C"))&&colors.Contains(ThemeColor("#C86B28")),"cat artwork contains distinct black and ginger fur rather than emoji font variants");
        var caption=ManualUpdateLink.Content;ManualUpdateLink.Content=new string('Ж',90);await Task.Delay(80);PositionCatArtwork();
        Check(catArtwork.Visibility==Visibility.Collapsed,"a long update caption gets priority over decorative cats");ManualUpdateLink.Content=caption;
        store.Set("theme","Dark");ApplyTheme();Render();await Task.Delay(40);
        Check(catArtwork.Visibility==Visibility.Collapsed,"switching away from pink removes its cat decoration");
        store.Set("theme",theme);ApplyTheme();Render();
    }
    private async Task RunSectionAutoCloseChecks(List<string> results)
    {
        if(!isolated)throw new InvalidOperationException("Section checks require isolated data.");
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        async Task Settle(){await Task.Delay(80);Root.UpdateLayout();PositionSection();await Task.Delay(40);Root.UpdateLayout();}
        static Native.Point Center(SectionScreenArea area)=>new(){X=(int)Math.Floor((area.Left+area.Right)/2),Y=(int)Math.Floor((area.Top+area.Bottom)/2)};
        SectionScreenArea Heading(string title)
        {
            if(!TrySectionElementArea(sectionHeadings[title],out var area))throw new InvalidOperationException("Synthetic heading was not measured.");return area;
        }
        SectionScreenArea ListArea()
        {
            if(sectionBorder is null||!TrySectionPopupArea(sectionPopup,sectionBorder,out var area))throw new InvalidOperationException("Synthetic popup was not measured.");return area;
        }
        Native.Point Outside()=>new(){X=-100000,Y=-100000};
        string layout=store.Get("sectionlayout","side");var originalExpanded=expanded.ToDictionary(pair=>pair.Key,pair=>pair.Value);
        string? originalSection=activeSection;bool initialized=sectionInitialized;int menus=openMenus;
        var originalHistory=store.All().Select(clip=>(clip.Id,clip.Text,clip.Name,clip.Template,clip.Seen)).ToArray();
        var oldPointer=sectionAutoClosePointerOverride;var oldClock=sectionAutoCloseClockOverride;
        Native.Point? pointer=null;long clock=1000;
        try
        {
            ForgetSectionAutoClose();sectionAutoClosePointerOverride=()=>pointer;sectionAutoCloseClockOverride=()=>clock;
            store.Set("sectionlayout","side");activeSection="Сегодня";sectionInitialized=true;Render();ShowNearCursor();await Settle();
            pointer=Outside();QueueSectionHover("Сегодня",sectionHeadings["Сегодня"]);clock+=500;PollSectionAutoClose();
            Check(sectionPopup.IsOpen&&hoverOpenedSections.Count==0,"programmatic or keyboard opening does not close merely because the real cursor is elsewhere");
            pointer=Center(Heading("Сегодня"));ObserveOpenSectionPointer("Сегодня");PollSectionAutoClose();
            Check(hoverOpenedSections.ContainsKey("Сегодня"),"entering an already open default Today heading arms mouse-leave tracking");
            pointer=Outside();clock++;PollSectionAutoClose();clock+=299;PollSectionAutoClose();
            Check(sectionPopup.IsOpen,"leaving a hovered heading has a 300ms grace period");clock+=2;PollSectionAutoClose();
            Check(!sectionPopup.IsOpen&&activeSection is null&&AppWindow.IsVisible,"leaving heading and flyout closes only its section after the grace period");

            ToggleSection("Сегодня");await Settle();pointer=Center(Heading("Сегодня"));ObserveOpenSectionPointer("Сегодня");
            var gap=SectionGapArea(Heading("Сегодня"),ListArea());pointer=Center(gap??ListArea());clock+=500;PollSectionAutoClose();
            Check(sectionPopup.IsOpen,"the small heading-to-flyout corridor does not flicker closed while crossing it");
            var body=ListArea();pointer=new(){X=(int)body.Right-3,Y=(int)((body.Top+body.Bottom)/2)};PollSectionAutoClose();clock+=500;PollSectionAutoClose();
            Check(sectionPopup.IsOpen,"the flyout scroll bar is inside the protected list hit area");

            var previewRow=visible.First(item=>item.Clip.Kind=="text");hovered=previewRow.Clip;hoverAnchor=previewRow.Row;leaveTimer.Stop();ShowPreview();await Settle();
            bool measured=preview.Child is FrameworkElement child&&TrySectionPopupArea(preview,child,out _);
            if(measured&&preview.Child is FrameworkElement content&&TrySectionPopupArea(preview,content,out var previewBounds))pointer=Center(previewBounds);
            clock+=500;PollSectionAutoClose();clock+=500;PollSectionAutoClose();
            Check(measured&&preview.IsOpen&&sectionPopup.IsOpen,"moving into the active clip preview keeps its hovered section open");preview.IsOpen=false;hovered=null;

            pointer=Outside();clock++;PollSectionAutoClose();openMenus++;clock+=500;PollSectionAutoClose();
            Check(sectionPopup.IsOpen,"an open clip context menu suspends hover closing");openMenus--;clock++;PollSectionAutoClose();clock+=301;PollSectionAutoClose();
            Check(!sectionPopup.IsOpen,"closing a context menu restarts the leave grace instead of leaving a detached list open");

            ToggleSection("Сегодня");await Settle();pointer=Center(Heading("Сегодня"));ObserveOpenSectionPointer("Сегодня");
            // Rebuilding the visual tree is not physical pointer departure.
            pointer=Outside();lastSectionAutoClosePointer=pointer;rendering=true;LeaveSectionHover("Сегодня",sectionHeadings["Сегодня"]);rendering=false;
            Render();await Settle();clock+=500;PollSectionAutoClose();clock+=500;PollSectionAutoClose();
            Check(sectionPopup.IsOpen,"render-induced PointerExited with a stationary observed cursor does not start auto-close");
            ForgetSectionAutoClose();ToggleSection("Сегодня");ToggleSection("Сегодня");pointer=Outside();clock+=500;PollSectionAutoClose();
            Check(sectionPopup.IsOpen&&hoverOpenedSections.Count==0,"manual click reopening remains open until an actual pointer entry arms tracking");

            ForgetSectionAutoClose();store.Set("sectionlayout","inline");expanded["Сегодня"]=false;expanded["Шаблоны"]=false;Render();await Settle();
            suppressedHoverSection=null;pointer=Center(Heading("Сегодня"));QueueSectionHover("Сегодня",sectionHeadings["Сегодня"],pointerObserved:true);OpenPendingHoverSection();await Settle();
            pointer=Center(Heading("Сегодня"));PollSectionAutoClose();clock+=500;PollSectionAutoClose();
            Check(expanded.GetValueOrDefault("Сегодня")&&hoverOpenedSections.ContainsKey("Сегодня"),"inline hover expansion also owns an independent leave timer");
            var inlineRow=sectionAutoCloseRows["Сегодня"].First(row=>TrySectionElementArea(row,out _));
            if(TrySectionElementArea(inlineRow,out var inlineBounds))pointer=Center(inlineBounds);clock+=500;PollSectionAutoClose();
            Check(expanded.GetValueOrDefault("Сегодня"),"moving from an inline heading into its rows does not collapse the section");
            pointer=Outside();clock++;PollSectionAutoClose();clock+=301;PollSectionAutoClose();await Settle();
            Check(!expanded.GetValueOrDefault("Сегодня")&&!sectionPopup.IsOpen,"leaving an inline section collapses its expanded state for natural compact resizing");
            Check(originalHistory.SequenceEqual(store.All().Select(clip=>(clip.Id,clip.Text,clip.Name,clip.Template,clip.Seen))),"hover auto-close never edits or reorders synthetic clips and templates");
        }
        finally
        {
            ForgetSectionAutoClose();sectionAutoClosePointerOverride=oldPointer;sectionAutoCloseClockOverride=oldClock;openMenus=menus;preview.IsOpen=false;hovered=null;
            expanded.Clear();foreach(var pair in originalExpanded)expanded[pair.Key]=pair.Value;
            store.Set("sectionlayout",layout);activeSection=originalSection;sectionInitialized=initialized;Render();await Settle();
        }
    }
}
