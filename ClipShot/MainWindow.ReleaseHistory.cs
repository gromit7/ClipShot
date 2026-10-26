namespace ClipShot;
public sealed partial class MainWindow
{
    private void ShowInstalledHistory(bool installed)
    {
        if(isolated||exiting)return;
        var current=typeof(App).Assembly.GetName().Version!;
        var history=ReleaseHistory.Load(Path.Combine(AppContext.BaseDirectory,"Assets","ReleaseHistory.json"),current);
        updateFeedbackTimer.Stop();installationNotice?.Close();
        installationNotice=new($"{T(installed?"Программа установлена":"Программа обновлена")} · {current.ToString(3)}",
            T("История, шаблоны и настройки сохранены."),MicrophoneState.Enabled,appNotice:true,
            history:history,historyTitle:T("Что нового"));
    }
}
