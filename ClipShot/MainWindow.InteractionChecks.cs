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
            Check(TrayTip.Contains(typeof(App).Assembly.GetName().Version!.ToString(3)),"tray tooltip includes installed version");
            Check(ManualUpdateLink.Visibility==Visibility.Visible&&ManualUpdateLink.IsEnabled&&ManualUpdateLink.Content.ToString()=="Проверить обновление","quiet manual update action is always visible at bottom");
            preparedInstaller="synthetic-not-executed.exe";updateMetadataValid=true;ShowUpdateOffer();
            Check(updatePulseActive&&ManualUpdateLink.Content.ToString()=="Обновить сейчас","verified prepared update changes caption and starts gentle reminder");
            updateLaunchRequested=true;ShowUpdateOffer();
            Check(!ManualUpdateLink.IsEnabled&&!updatePulseActive&&ManualUpdateLink.Content.ToString()=="Устанавливаем обновление…","pressing update gives visible feedback and prevents repeated clicks");
            updateLaunchRequested=false;preparedInstaller=null;updateMetadataValid=false;ShowUpdateOffer();
            Check(!updatePulseActive&&ManualUpdateLink.IsEnabled,"clearing prepared update removes the reminder");
            Check(Groups.Children.Count==visible.Count+2,"empty date sections are hidden without a search");
            store.Set("sectionlayout","side");Render();await Task.Delay(150);
            Check(Groups.Children.Count==1&&TemplateGroups.Children.Count==1&&sectionPopup.IsOpen&&visible.Count==5,"compact panel keeps dates above bottom templates and opens today beside it");
            Check(AppWindow.Size.Width<400&&AppWindow.Size.Height<300&&Search.PlaceholderText.Length==0,"default panel is compact and empty search has only a magnifier");
            string autofit=store.Get("panelautofit","1");BeginWindowResize();EndWindowResize();
            Check(store.Get("panelautofit","1")==autofit,"moving without resizing preserves automatic panel sizing");
            var originalSize=AppWindow.Size;BeginWindowResize();AppWindow.Resize(new Windows.Graphics.SizeInt32(originalSize.Width+40,originalSize.Height+20));await Task.Delay(100);
            Check(!sectionPopup.IsOpen,"side popup stays hidden while resizing");EndWindowResize();await Task.Delay(150);
            Check(sectionPopup.IsOpen&&AppWindow.Size.Width==originalSize.Width+40,"main window resizes with a section open and restores its panel");AppWindow.Resize(originalSize);
            DismissSectionForExternalFocus(false);Check(sectionPopup.IsOpen,"focus within ClipShot keeps section open");DismissSectionForExternalFocus(true);
            await Task.Delay(240);Check(!sectionPopup.IsOpen&&activeSection is null&&!AppWindow.IsVisible,"switching to another application smoothly hides panel and side section");ShowNearCursor();ToggleSection("Сегодня");
            DismissSectionForExternalFocus(true);ShowNearCursor();await Task.Delay(240);
            Check(AppWindow.IsVisible&&!panelFading,"reopening cancels a pending automatic hide");ToggleSection("Сегодня");
            openMenus++;DismissSectionForExternalFocus(true);await Task.Delay(200);
            Check(AppWindow.IsVisible&&!panelFading,"an open command menu prevents automatic hiding");openMenus--;
            await Copy(visible.First(v=>v.Clip.Kind=="text").Clip);
            Check(!sectionPopup.IsOpen&&AppWindow.IsVisible,"choosing a clip collapses side section but keeps main window open");ToggleSection("Сегодня");
            Check(FooterHints.Visibility==Visibility.Collapsed,"keyboard hints are hidden from the bottom of the window");
            Check(HeaderSurface.Visibility==Visibility.Collapsed&&Root.ContextFlyout is Microsoft.UI.Xaml.Controls.MenuFlyout context&&context.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Any(i=>i.Text=="Настройки")&&context.Items.OfType<Microsoft.UI.Xaml.Controls.MenuFlyoutItem>().Any(i=>i.Text=="Справка"),"header is hidden and settings/help are available from the empty-area menu");
            ToggleVisibility();Check(!AppWindow.IsVisible,"tray click hides an open main window");ToggleVisibility();Check(AppWindow.IsVisible,"tray click restores a hidden main window");
            ToggleSection("Шаблоны");await Task.Delay(100);Check(visible.Count==1&&visible[0].Clip.Template,"side section switches to templates without duplicated panels");
            ToggleSection("Шаблоны");Check(!sectionPopup.IsOpen,"clicking the active section closes its panel");
            ToggleSection("Сегодня");await Task.Delay(150);
            hovered=visible.First(v=>v.Clip.Kind=="text").Clip;hoverAnchor=visible.First(v=>v.Clip.Id==hovered.Id).Row;ShowPreview();await Task.Delay(150);
            Check(preview.IsOpen,"preview works for a clip inside the side panel");hovered=null;preview.IsOpen=false;
            store.Set("sectionlayout","inline");Render();Check(visible.Count==6&&!sectionPopup.IsOpen,"inline preference restores the full list");
            Check(updateClient is null&&!updateTimer.IsEnabled,"isolated UI never checks or installs internet updates");
            Check(AutomaticUpdates&&!CanAutoInstall(),"automatic install is default but isolated UI cannot install");
            Native.PostMessage(hwnd,App.UpdateQuitMessage,(nint)unchecked((uint)Environment.TickCount+5000),0);await Task.Delay(100);
            Check(!exiting&&AppWindow.IsVisible,"automatic shutdown request cannot close an active test window");
            store.Set("updateinstall","manual");Check(AutomaticUpdates,"legacy manual preference keeps background updates enabled");store.Set("updateinstall","auto");
            Search.Text="EXAMPLE";
            await Task.Delay(100);
            Check(visible.Count==2&&visible.All(v=>v.Clip.Kind=="text"),"case insensitive text search across templates and history");
            Check(visible[0].Clip.Template,"templates precede history");
            Search.Text="NOT_FOUND_5a82";
            await Task.Delay(100);
            Check(visible.Count==0&&selected==-1,"empty search has no selection");
            Check(Groups.Children.Count==1&&Groups.Children[0] is Microsoft.UI.Xaml.Controls.TextBlock,"empty search shows one message and no section headings");
            Search.Text="";
            await Task.Delay(100);
            Check(visible.Count==6&&selected>=0&&selected<visible.Count,"clearing search restores rows and valid selection");
            long id=visible.First(v=>!v.Clip.Template&&v.Clip.Kind=="text").Clip.Id;
            store.Template(id,true);Render();
            Check(visible.Count(v=>v.Clip.Id==id)==1&&visible.First(v=>v.Clip.Id==id).Clip.Template,"moving to templates does not duplicate rows");
            store.Clear();Render();
            Check(visible.Count==2&&visible.All(v=>v.Clip.Template),"clear history preserves templates in UI");
            Check(Groups.Children.Count==3,"template-only history does not show empty date headings");
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
            Check(help.Content is Microsoft.UI.Xaml.Controls.ScrollViewer helpScroll&&helpScroll.Content is Microsoft.UI.Xaml.Controls.TextBlock helpText&&helpText.Text.Contains("Alt+F1")&&helpText.Text.Contains("Микрофон")&&helpText.Text.Contains("Обновления с GitHub")&&!helpText.Text.Contains("Я верю"),"help lists implemented features and current shortcuts without jokes");
            help.Hide();await helpTask;
            SettingsClick(this,new());await Task.Delay(200);
            Check(activeDialog is not null&&dialogOpen,"settings hold a tracked modal dialog");
            Check(AppWindow.Size.Width>=480&&AppWindow.Size.Height>=560,"settings temporarily expand the compact window");
            DismissSectionForExternalFocus(true);await Task.Delay(220);Check(AppWindow.IsVisible&&!panelFading,"settings stay open when another application takes focus");
            TakeScreenshot();await Task.Delay(400);
            Check(captureWindow is not null&&AppWindow.IsVisible&&activeDialog is not null,"ClipShot and its settings remain visible when taking their own screenshot");captureWindow?.Cancel();await Task.Delay(150);
            Native.PostMessage(hwnd,0x0112,0xF020,0);await Task.Delay(250);
            Check(!AppWindow.IsVisible&&!dialogOpen&&activeDialog is null,"minimize dismisses settings and clears modal state");
            Check(AppWindow.Size.Width<400&&AppWindow.Size.Height<300,"closing settings restores compact bounds");
            ShowNearCursor();SettingsClick(this,new());await Task.Delay(200);await CheckUpdatesFromTray();
            Check(!dialogOpen&&activeDialog is null,"manual check closes settings without saving and isolated UI never accesses network");
            ShowNearCursor();TakeScreenshot();await Task.Delay(400);
            Check(captureWindow is not null,"capture works after settings were minimized");captureWindow?.Cancel();await Task.Delay(150);
            store.Add("text","Настройки");Render();long untranslated=store.All().First(c=>c.Text=="Настройки").Id;
            store.Set("language","en");ApplyLanguage();Render();
            Check(Search.PlaceholderText.Length==0&&Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(Search)?.ToString()=="Search history"&&SelectHint.Text=="Select","English tooltip is applied while search stays unlabelled");
            bool ContainsText(DependencyObject node,string value){if(node is Microsoft.UI.Xaml.Controls.TextBlock t&&t.Text==value)return true;for(int i=0;i<Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChildrenCount(node);i++)if(ContainsText(Microsoft.UI.Xaml.Media.VisualTreeHelper.GetChild(node,i),value))return true;return false;}
            Check(ContainsText(visible.Single(v=>v.Clip.Id==untranslated).Row,"Настройки"),"language switch does not translate user clip contents");
            store.Set("language","ru");ApplyLanguage();Render();
            Check(Search.PlaceholderText.Length==0&&Microsoft.UI.Xaml.Controls.ToolTipService.GetToolTip(Search)?.ToString()=="Поиск по истории"&&SelectHint.Text=="Выбрать","Russian tooltip can be restored without restart");
            Check(!microphoneStarted&&!trayReady&&microphoneMouse is null,"isolated mode never installs microphone hooks or tray");
            var microphonePanel=new Microsoft.UI.Xaml.Controls.StackPanel();var saveMic=MicrophoneSettings(microphonePanel);
            Check(microphonePanel.Children.Single() is Microsoft.UI.Xaml.Controls.Expander expander&&!expander.IsExpanded,"microphone settings are initially collapsed");
            saveMic();Check(store.Get("micmouse","0")=="0"&&!microphoneStarted,"mouse button is unassigned by default and tests do not change hardware");
            nint foreground=Native.GetForegroundWindow();
            var notice=new MicrophoneNotice("Тестовый микрофон включён","Синтетические данные",MicrophoneState.Enabled);await Task.Delay(200);
            Check(Native.GetForegroundWindow()==foreground,"microphone notice does not steal focus");notice.Close();
            foreach(var state in Enum.GetValues<MicrophoneState>()){nint icon=MicrophoneIcon(state);Check(icon!=0,"shared ClipShot tray icon "+state);if(icon!=0)Native.DestroyIcon(icon);}
            Native.PostMessage(hwnd,0x0112,0xF020,0);await Task.Delay(150);
            Check(!AppWindow.IsVisible,"minimize hides the window to the tray");ShowNearCursor();
            foreach(var clip in store.All())store.Delete(clip.Id);Render();
            Check(Groups.Children.Count==1&&Groups.Children[0] is Microsoft.UI.Xaml.Controls.TextBlock,"completely empty history has no empty templates heading");
        }
        catch(Exception e){results.Add("FAIL "+e.GetType().Name+": "+e.Message);}
        File.WriteAllLines(Path.Combine(dataFolder,"interaction-checks.txt"),results);
        return results.All(r=>r.StartsWith("PASS ",StringComparison.Ordinal));
    }
}
