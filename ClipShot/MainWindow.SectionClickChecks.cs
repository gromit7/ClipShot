using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Shapes;

namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunSectionClickChecks(List<string> results)
    {
        if(!isolated)throw new InvalidOperationException("Section checks require isolated data.");
        void Check(bool ok,string name)=>results.Add($"{(ok?"PASS":"FAIL")} {name}");
        async Task Settle(){await Task.Delay(100);Root.UpdateLayout();PositionSection();await Task.Delay(40);}
        // Invoke the same accessible button command used by mouse click and keyboard.
        void Click(string title)
        {
            var peer=new Microsoft.UI.Xaml.Automation.Peers.ButtonAutomationPeer((Button)sectionHeadings[title]);
            ((Microsoft.UI.Xaml.Automation.Provider.IInvokeProvider)peer.GetPattern(Microsoft.UI.Xaml.Automation.Peers.PatternInterface.Invoke)).Invoke();
        }
        var original=store.All().Select(c=>(c.Id,c.Template,c.Seen,c.Text)).ToArray();
        string layout=store.Get("sectionlayout","side");
        store.Set("sectionlayout","side");CollapseSection();Render();await Settle();
        Click("Сегодня");await Settle();
        Check(sectionPopup.IsOpen&&activeSection=="Сегодня"&&visible.Count>0,"heading click opens today's side list through its accessible command");
        var size=AppWindow.Size;await Task.Delay(650);
        Check(sectionPopup.IsOpen&&activeSection=="Сегодня"&&AppWindow.Size.Equals(size),"an open side section remains stable without another heading command");
        Click("Шаблоны");await Settle();
        Check(sectionPopup.IsOpen&&activeSection=="Шаблоны"&&visible.All(item=>item.Clip.Template),"clicking templates replaces the side list without copying a clip");
        Click("Шаблоны");await Settle();
        Check(!sectionPopup.IsOpen&&activeSection is null,"clicking the active side heading closes it");
        await Task.Delay(450);Check(!sectionPopup.IsOpen,"a closed section never reopens from a pending hover timer");
        store.Set("sectionlayout","inline");expanded["Сегодня"]=false;expanded["Шаблоны"]=false;Render();await Settle();
        Click("Сегодня");await Settle();Click("Шаблоны");await Settle();
        Check(expanded.GetValueOrDefault("Сегодня")&&expanded.GetValueOrDefault("Шаблоны")&&!sectionPopup.IsOpen,"inline heading commands independently expand both sections");
        size=AppWindow.Size;await Task.Delay(650);
        Check(expanded.GetValueOrDefault("Сегодня")&&expanded.GetValueOrDefault("Шаблоны")&&AppWindow.Size.Equals(size),"inline sections retain their height without pointer departure timers");
        Click("Сегодня");await Settle();Click("Шаблоны");await Settle();
        Check(!expanded.GetValueOrDefault("Сегодня")&&!expanded.GetValueOrDefault("Шаблоны"),"repeated inline commands collapse both sections for compact docking");
        Check(original.SequenceEqual(store.All().Select(c=>(c.Id,c.Template,c.Seen,c.Text))),"section commands preserve synthetic history, timestamps and templates");
        expanded["Сегодня"]=true;expanded["Шаблоны"]=true;store.Set("sectionlayout",layout);activeSection="Сегодня";sectionInitialized=true;Render();await Settle();
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
