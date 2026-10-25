using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Input;

namespace ClipShot;
public sealed partial class MainWindow
{
    // Exercises production rendering and settings against a dedicated synthetic database.
    private async Task<bool> RunInteractionChecks()
    {
        var results=new List<string>();
        void Check(bool ok,string name){results.Add($"{(ok?"PASS":"FAIL")} {name}");}
        try
        {
            Search.Text="EXAMPLE";
            await Task.Delay(100);
            Check(visible.Count==2&&visible.All(v=>v.Clip.Kind=="text"),"case insensitive text search across templates and history");
            Check(visible[0].Clip.Template,"templates precede history");
            Search.Text="NOT_FOUND_5a82";
            await Task.Delay(100);
            Check(visible.Count==0&&selected==-1,"empty search has no selection");
            Search.Text="";
            await Task.Delay(100);
            Check(visible.Count==6&&selected==0,"clearing search restores rows and selection");
            long id=visible.First(v=>!v.Clip.Template&&v.Clip.Kind=="text").Clip.Id;
            store.Template(id,true);Render();
            Check(visible.Count(v=>v.Clip.Id==id)==1&&visible.First(v=>v.Clip.Id==id).Clip.Template,"moving to templates does not duplicate rows");
            store.Clear();Render();
            Check(visible.Count==2&&visible.All(v=>v.Clip.Template),"clear history preserves templates in UI");
            store.Set("days","broken");Prune();
            Check(visible.Count==2,"invalid retention value does not crash");
            store.Set("theme","Light");ApplyTheme();Render();
            Check(Root.RequestedTheme==ElementTheme.Light,"light theme applied");
            store.Set("theme","Dark");ApplyTheme();Render();
            Check(Root.RequestedTheme==ElementTheme.Dark,"dark theme applied");
            Search.Text="test";Hide();
            Check(Search.Text==""&&!preview.IsOpen,"hide clears query and preview");
            ShowNearCursor();
            Check(ReferenceEquals(FocusManager.GetFocusedElement(Root.XamlRoot),Search),"opening focuses search");
            Check(!trayReady&&!historyKeyReady&&!screenshotKeyReady,"test mode has no tray or global hotkeys");
            uint sequence=Native.GetClipboardSequenceNumber();int count=store.All().Count;
            await Copy(visible[0].Clip);
            Check(sequence==Native.GetClipboardSequenceNumber(),"isolated copy does not write system clipboard");
            Check(store.All().Count==count,"copy selection does not duplicate history");
            ShowNearCursor();
            hovered=visible[0].Clip with {Kind="image",Image=[1,2,3]};hoverAnchor=visible[0].Row;Status.Text="";
            ShowPreview();for(int attempt=0;attempt<20&&Status.Text=="";attempt++)await Task.Delay(100);
            Check(!preview.IsOpen&&Status.Text.Contains("предпросмотр"),"invalid image preview reports error without crashing");
            var help=CreateHelpDialog();var helpTask=help.ShowAsync();await Task.Delay(150);
            Check(help.Content is Microsoft.UI.Xaml.Controls.TextBlock helpText&&helpText.Text=="Я верю в тебя, ты разберёшься сам.","help displays the agreed Russian instruction");
            help.Hide();await helpTask;
        }
        catch(Exception e){results.Add("FAIL "+e.GetType().Name+": "+e.Message);}
        File.WriteAllLines(Path.Combine(dataFolder,"interaction-checks.txt"),results);
        return results.All(r=>r.StartsWith("PASS ",StringComparison.Ordinal));
    }
}
