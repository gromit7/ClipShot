using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;

namespace ClipShot;
public sealed partial class MainWindow
{
    private UiText words=new("ru");
    private string T(string text)=>words.Translate(text);
    private string ShortcutTip=>$"ClipShot · {Win32Hotkey.Describe(AppSettings.FunctionKey(store.Get("key","112"),112),store.Get("ctrl","0")=="1")} — {T("История")} · {Win32Hotkey.Describe(AppSettings.FunctionKey(store.Get("shotkey","113"),113),store.Get("shotctrl","0")=="1")} — {T("Снимок")}";
    private string TrayTip
    {
        get
        {
            string text=microphoneStarted?$"ClipShot · {MicrophoneText(microphoneResult.State)} · {ShortcutTip["ClipShot · ".Length..]}":ShortcutTip;
            text=text.Replace("ClipShot ·",$"ClipShot {typeof(App).Assembly.GetName().Version?.ToString(3)} ·");return text.Length>127?text[..126]+"…":text;
        }
    }
    private void ApplyLanguage()
    {
        words=new UiText(store.Get("language","ru"));
        string version=typeof(App).Assembly.GetName().Version?.ToString(3)??"";
        Title=$"ClipShot {version} — {T("буфер обмена и скриншоты")}";TitleText.Text="ClipShot";Search.PlaceholderText="";
        ToolTipService.SetToolTip(Search,T("Поиск по истории"));
        ToolTipService.SetToolTip(SearchToggle,T("Поиск по истории"));
        ToolTipService.SetToolTip(WindowHandle,T("Перетащить окно"));AutomationProperties.SetName(WindowHandle,T("Перетащить окно"));
        AutomationProperties.SetName(SearchToggle,T("Поиск по истории"));
        ToolTipService.SetToolTip(SearchClose,T("Закрыть поиск"));
        AutomationProperties.SetName(SearchClose,T("Закрыть поиск"));
        SelectHint.Text=T("Выбрать");CopyHint.Text=T("Копировать");CloseHint.Text=T("Закрыть");
        AutomationProperties.SetName(Search,T("Поиск по истории и шаблонам"));
        AutomationProperties.SetName(SettingsButton,T("Настройки"));ToolTipService.SetToolTip(SettingsButton,T("Настройки"));
        AutomationProperties.SetName(HelpButton,T("Справка"));ToolTipService.SetToolTip(HelpButton,T("Справка"));
        Status.Text=T(UiText.Russian(Status.Text));
        ConfigureWindowContextMenu();ShowUpdateOffer();
        if(trayReady){tray.Tip=TrayTip;Native.Shell_NotifyIcon(1,ref tray);}
    }
}
