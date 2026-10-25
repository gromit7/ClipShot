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
}
