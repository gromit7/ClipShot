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
            Search.Text="test";await Task.Delay(100);
            await Copy(visible[0].Clip);
            Check(AppWindow.IsVisible&&Search.Text=="test","copy keeps the window and search open");
            Check(store.Get("autopaste","0")=="0","automatic paste is off by default");
            await TryPastePrevious();
            Check(sequence==Native.GetClipboardSequenceNumber(),"isolated copy does not write system clipboard");
            Check(store.All().Count==count,"copy selection does not duplicate history");
            Search.Text="";await Task.Delay(100);
            ShowNearCursor();
            hovered=visible[0].Clip with {Kind="image",Image=[1,2,3]};hoverAnchor=visible[0].Row;Status.Text="";
            ShowPreview();for(int attempt=0;attempt<20&&Status.Text=="";attempt++)await Task.Delay(100);
            Check(!preview.IsOpen&&Status.Text.Contains("предпросмотр"),"invalid image preview reports error without crashing");
            var help=CreateHelpDialog();var helpTask=help.ShowAsync();await Task.Delay(150);
            Check(help.Content is Microsoft.UI.Xaml.Controls.TextBlock helpText&&helpText.Text=="Я верю в тебя, ты разберёшься сам.","help displays the agreed Russian instruction");
            help.Hide();await helpTask;
            store.Add("text","Настройки");Render();long untranslated=store.All().First(c=>c.Text=="Настройки").Id;
            store.Set("language","en");ApplyLanguage();Render();
            Check(Search.PlaceholderText=="Search history"&&SelectHint.Text=="Select","English is applied only after explicit selection");
            bool ContainsText(DependencyObject node,string value){if(node is Microsoft.UI.Xaml.Controls.TextBlock t&&t.Text==value)return true;for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);i++)if(ContainsText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,i),value))return true;return false;}
            Check(ContainsText(visible.Single(v=>v.Clip.Id==untranslated).Row,"Настройки"),"language switch does not translate user clip contents");
            store.Set("language","ru");ApplyLanguage();Render();
            Check(Search.PlaceholderText=="Поиск по истории"&&SelectHint.Text=="Выбрать","Russian can be restored without restart");
            Check(!microphoneStarted&&!microphoneTrayReady&&microphoneMouse is null,"isolated mode never installs microphone hooks or tray");
            var microphonePanel=new Microsoft.UI.Xaml.Controls.StackPanel();var saveMic=MicrophoneSettings(microphonePanel);
            Check(microphonePanel.Children.Single() is Microsoft.UI.Xaml.Controls.Expander expander&&!expander.IsExpanded,"microphone settings are initially collapsed");
            saveMic();Check(store.Get("micmouse","0")=="0"&&!microphoneStarted,"mouse button is unassigned by default and tests do not change hardware");
            nint foreground=Native.GetForegroundWindow();
            var notice=new MicrophoneNotice("Тестовый микрофон включён","Синтетические данные",MicrophoneState.Enabled);await Task.Delay(200);
            Check(Native.GetForegroundWindow()==foreground,"microphone notice does not steal focus");notice.Close();
            foreach(var state in Enum.GetValues<MicrophoneState>()){nint icon=MicrophoneIcon(state);Check(icon!=0,"microphone tray icon "+state);if(icon!=0)Native.DestroyIcon(icon);}
            Native.PostMessage(hwnd,0x0112,0xF020,0);await Task.Delay(150);
            Check(!AppWindow.IsVisible,"minimize hides the window to the tray");ShowNearCursor();
        }
        catch(Exception e){results.Add("FAIL "+e.GetType().Name+": "+e.Message);}
        File.WriteAllLines(Path.Combine(dataFolder,"interaction-checks.txt"),results);
        return results.All(r=>r.StartsWith("PASS ",StringComparison.Ordinal));
    }
}
