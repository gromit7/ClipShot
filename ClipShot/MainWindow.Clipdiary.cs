using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer clipdiaryOfferTimer=new(){Interval=TimeSpan.FromSeconds(8)};
    private bool clipdiaryImportRunning;
    private CancellationTokenSource? clipdiaryImportStop;
    private bool quitAfterClipdiaryImport;

    private async Task CancelImportAndQuit()
    {
        quitAfterClipdiaryImport=true;clipdiaryImportStop?.Cancel();
        while(clipdiaryImportRunning)await Task.Delay(25);
        Quit();
    }

    private async Task AddCapturedClip(string kind,string text,string html="",string rtf="",byte[]? image=null)
    {
        if(!clipdiaryImportRunning){store.Add(kind,text,html,rtf,image);return;}
        // Clipboard decoding already runs asynchronously. A competing writer must
        // also wait off the UI thread while the short import merge commits.
        await Task.Run(()=>{using var target=new Store(Path.Combine(dataFolder,"clips.db"));target.Add(kind,text,html,rtf,image);});
    }

    // Detection checks paths only. The source is opened only after the explicit Import click.
    private void StartClipdiaryOffer()
    {
        if(isolated||store.Get("clipdiaryoffer","1")=="0")return;
        clipdiaryOfferTimer.Tick+=async(_,_)=>
        {
            if(exiting){clipdiaryOfferTimer.Stop();return;}
            if(UpdateOperationBusy||updateInstalling||updateDownloading||updateChecking||preparedInstaller is not null)return;
            clipdiaryOfferTimer.Stop();
            var found=ClipdiaryImport.DetectInstallation();
            if(found is not null)await ShowClipdiaryImport(Path.GetDirectoryName(found.HistoryPath??found.TemplatesPath??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Clipdiary","diary.db")),true);
        };
        clipdiaryOfferTimer.Start();
    }

    private async Task ShowClipdiaryImport(string? folder=null,bool offered=false)
    {
        if(dialogOpen||clipdiaryImportRunning||exiting)return;
        bool wasVisible=AppWindow.IsVisible;
        if(!wasVisible)ShowNearCursor();
        var source=new TextBox{Header=T("Папка данных Clipdiary"),Text=folder??Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Clipdiary"),AcceptsReturn=false};
        var history=new CheckBox{Content=T("История: текст и изображения"),IsChecked=true};
        var templates=new CheckBox{Content=T("Шаблоны"),IsChecked=true};
        var keep=new CheckBox{Content=T("Хранить всю импортированную историю без ограничения срока"),IsChecked=true};
        var never=new CheckBox{Content=T("Больше не предлагать при запуске"),Visibility=offered?Visibility.Visible:Visibility.Collapsed};
        var detail=new TextBlock{Text=T("Исходные данные Clipdiary не изменяются. Повторный импорт не создаёт дубли. Неподдерживаемые записи будут пропущены."),TextWrapping=TextWrapping.Wrap};
        var progress=new ProgressRing{IsActive=false,Visibility=Visibility.Collapsed,Width=28,Height=28};
        var panel=new StackPanel{Spacing=12};
        foreach(var element in new UIElement[]{detail,source,history,templates,keep,never,progress})panel.Children.Add(element);
        var dialog=new ContentDialog{XamlRoot=Root.XamlRoot,Title=T("Импорт из Clipdiary"),Content=new ScrollViewer{Content=panel},PrimaryButtonText=T("Импортировать"),CloseButtonText=T(offered?"Позже":"Закрыть"),DefaultButton=ContentDialogButton.Close};
        void RefreshSource()
        {
            history.IsChecked=File.Exists(Path.Combine(source.Text.Trim(),"diary.db"));
            templates.IsChecked=File.Exists(Path.Combine(source.Text.Trim(),"snippets.sdb"));
        }
        source.TextChanged+=(_,_)=>RefreshSource();RefreshSource();
        using var cancellation=new CancellationTokenSource();
        bool completed=false;
        dialog.CloseButtonClick+=(_,e)=>
        {
            if(clipdiaryImportRunning){cancellation.Cancel();e.Cancel=true;detail.Text=T("Отменяем импорт…");}
        };
        dialog.Closing+=(_,e)=>{if(clipdiaryImportRunning){cancellation.Cancel();e.Cancel=true;}};
        dialog.PrimaryButtonClick+=async(_,e)=>
        {
            e.Cancel=true;
            if(clipdiaryImportRunning||completed)return;
            if(history.IsChecked!=true&&templates.IsChecked!=true){detail.Text=T("Выберите историю или шаблоны.");return;}
            string path=source.Text.Trim();bool readHistory=history.IsChecked==true,readTemplates=templates.IsChecked==true,keepAll=readHistory&&keep.IsChecked==true;
            if(!Directory.Exists(path)){detail.Text=T("Папка не найдена. Укажите папку с diary.db и snippets.sdb.");return;}
            if((readHistory&&!File.Exists(Path.Combine(path,"diary.db")))||(readTemplates&&!File.Exists(Path.Combine(path,"snippets.sdb"))))
            {detail.Text=T("Папка не найдена. Укажите папку с diary.db и snippets.sdb.");return;}
            var deferral=e.GetDeferral();
            clipdiaryImportRunning=true;clipdiaryImportStop=cancellation;dialog.IsPrimaryButtonEnabled=false;
            source.IsEnabled=history.IsEnabled=templates.IsEnabled=keep.IsEnabled=false;
            progress.IsActive=true;progress.Visibility=Visibility.Visible;dialog.CloseButtonText=T("Отмена");
            try
            {
                // A separate connection avoids using the UI Store from a worker thread.
                var status=new Progress<int>(count=>{if(clipdiaryImportRunning&&!completed&&!exiting)detail.Text=$"{T("Обработано записей")}: {count}";});
                var result=await Task.Run(()=>
                {
                    IEnumerable<ClipdiaryImportResult> Records()
                    {
                        if(readHistory)foreach(var row in ClipdiaryImport.ReadHistory(Path.Combine(path,"diary.db"),cancellation.Token))yield return row;
                        if(readTemplates)foreach(var row in ClipdiaryImport.ReadTemplates(Path.Combine(path,"snippets.sdb"),cancellation.Token))yield return row;
                    }
                    using var target=new Store(Path.Combine(dataFolder,"clips.db"));
                    return target.Import(Records(),keepAll,cancellation.Token,status);
                });
                if(exiting)return;completed=true;
                detail.Text=$"{T("Импорт завершён")}. {T("Добавлено")}: {result.Imported}. {T("Уже были")}: {result.Existing}. {T("Пропущено")}: {result.Skipped}.";
                store.Set("clipdiaryoffer","0");Render();
            }
            catch(OperationCanceledException){if(!exiting)detail.Text=T("Импорт отменён. Изменения не сохранены.");completed=true;}
            catch(Exception){if(!exiting&&!completed)detail.Text=T("Не удалось импортировать данные. Проверьте папку и формат базы. Изменения не сохранены.");completed=true;}
            finally
            {
                clipdiaryImportRunning=false;clipdiaryImportStop=null;progress.IsActive=false;progress.Visibility=Visibility.Collapsed;
                dialog.CloseButtonText=T("Закрыть");deferral.Complete();
            }
        };
        try{await ShowDialogAsync(dialog);if(never.IsChecked==true)store.Set("clipdiaryoffer","0");}
        finally{if(!wasVisible&&!exiting)Hide();}
    }
}
