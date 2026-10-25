using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using System.Text.Json;
namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly MicrophoneController microphones=new(new CoreAudioMicrophones());
    private readonly DispatcherTimer microphoneTimer=new(){Interval=TimeSpan.FromMilliseconds(750)};
    private IReadOnlyList<MicrophoneDevice> microphoneDevices=[];
    private MicrophoneResult microphoneResult=new(MicrophoneState.Unavailable,[],true);
    private MicrophoneMouse? microphoneMouse;private MicrophoneNotice? microphoneNotice;
    private bool microphoneBusy,microphoneStarted,microphonePending;private int microphoneGeneration;
    private string[] MicrophoneIds(){try{return JsonSerializer.Deserialize<string[]>(store.Get("micids","[]"))??[];}catch{return [];}}
    private void StartMicrophone()
    {
        if(isolated)return;
        StopMicrophone();if(store.Get("micenabled","1")!="1")return;
        microphoneStarted=true;
        if(store.Get("mickey","M")!="none")
        {
            uint key=store.Get("mickey","M")=="F3"?114u:77u;
            if(!Native.RegisterHotKey(hwnd,3,0x4003,key))Status.Text=T("Сочетание микрофона занято. Выберите другое в настройках.");
        }
        int button=store.Get("micmouse","0") switch{"1"=>1,"2"=>2,_=>0};
        if(button>0)try{microphoneMouse=new(button,()=>DispatcherQueue.TryEnqueue(()=>RefreshMicrophone(true)));}catch{Status.Text=T("Не удалось назначить кнопку мыши.");}
        microphoneTimer.Tick-=MicrophoneTick;microphoneTimer.Tick+=MicrophoneTick;microphoneTimer.Start();RefreshMicrophone(false);
    }
    private void MicrophoneTick(object? sender,object e)=>RefreshMicrophone(false);
    private async void RefreshMicrophone(bool toggle)
    {
        if(isolated||!microphoneStarted||exiting)return;
        if(microphoneBusy){if(toggle)microphonePending=!microphonePending;return;}
        microphoneBusy=true;int generation=microphoneGeneration;string mode=store.Get("micmode","default");var ids=MicrophoneIds();
        try
        {
            var result=await Task.Run(()=>
            {
                var state=toggle?microphones.Toggle(mode,ids):microphones.Read(mode,ids);
                IReadOnlyList<MicrophoneDevice> all;
                try{all=new CoreAudioMicrophones().Read();}catch{all=[];}
                return (state,all);
            });
            if(exiting||!microphoneStarted||generation!=microphoneGeneration)return;
            var previousState=microphoneResult.State;microphoneResult=result.state;microphoneDevices=result.all;
            UpdateMicrophoneTray();
            if(toggle||previousState!=microphoneResult.State&&previousState!=MicrophoneState.Unavailable)
            {
                microphoneNotice?.Close();
                microphoneNotice=new(MicrophoneText(microphoneResult.State),toggle&&!microphoneResult.Success?T("Не все микрофоны удалось переключить. Проверьте подключение."):string.Join(", ",microphoneResult.Devices.Select(d=>d.Name)),microphoneResult.State);
            }
        }
        catch{if(!exiting&&microphoneStarted&&generation==microphoneGeneration){microphoneResult=new(MicrophoneState.Unavailable,[],false);UpdateMicrophoneTray();}}
        finally{microphoneBusy=false;if(microphonePending&&!exiting){microphonePending=false;RefreshMicrophone(true);}}
    }
    private string MicrophoneText(MicrophoneState state)=>T(state switch
    {MicrophoneState.Enabled=>"Микрофон включён",MicrophoneState.Muted=>"Микрофон выключен",MicrophoneState.Mixed=>"Часть микрофонов включена",_=>"Микрофон недоступен"});
    private string sharedTraySignature="";
    private string TraySignature=>$"{microphoneStarted}/{microphoneResult.State}/{TrayTip}";
    private void UpdateMicrophoneTray()
    {
        if(!isolated&&!exiting&&(!trayReady||sharedTraySignature!=TraySignature))CreateTray();
    }
    private static nint MicrophoneIcon(MicrophoneState state)
    {
        using var bitmap=TrayIconRenderer.Render(Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico"),state);
        return bitmap.GetHicon();
    }
    private void StopMicrophone()
    {
        microphoneGeneration++;microphonePending=false;microphoneStarted=false;microphoneTimer.Stop();Native.UnregisterHotKey(hwnd,3);microphoneMouse?.Dispose();microphoneMouse=null;microphoneNotice?.Close();microphoneNotice=null;
        if(!isolated&&!exiting&&trayReady)CreateTray();
    }
    private Action MicrophoneSettings(StackPanel panel)
    {
        var content=new StackPanel{Spacing=10,MaxWidth=340};
        var enabled=new CheckBox{Content=T("Показывать статус и управлять микрофоном"),IsChecked=store.Get("micenabled","1")=="1"};content.Children.Add(enabled);
        var mode=new ComboBox{Header=T("Какими микрофонами управлять"),HorizontalAlignment=HorizontalAlignment.Stretch};foreach(string text in new[]{"По умолчанию в Windows","Всеми подключёнными","Выбранными"})mode.Items.Add(T(text));mode.SelectedIndex=store.Get("micmode","default") switch{"all"=>1,"selected"=>2,_=>0};content.Children.Add(mode);
        var ids=MicrophoneIds().ToHashSet();var devices=new List<(string Id,CheckBox Box)>();
        var devicePanel=new StackPanel{Spacing=4};foreach(var device in microphoneDevices){var box=new CheckBox{Content=device.Name,IsChecked=ids.Contains(device.Id)};devices.Add((device.Id,box));devicePanel.Children.Add(box);}content.Children.Add(devicePanel);
        void Selection()=>devicePanel.Visibility=mode.SelectedIndex==2?Visibility.Visible:Visibility.Collapsed;Selection();mode.SelectionChanged+=(_,_)=>Selection();
        var key=new ComboBox{Header=T("Переключение с клавиатуры"),HorizontalAlignment=HorizontalAlignment.Stretch};foreach(string text in new[]{"Ctrl+Alt+M","Ctrl+Alt+F3",T("Не назначено")})key.Items.Add(text);key.SelectedIndex=store.Get("mickey","M") switch{"F3"=>1,"none"=>2,_=>0};content.Children.Add(key);
        var mouse=new ComboBox{Header=T("Кнопка мыши"),HorizontalAlignment=HorizontalAlignment.Stretch};foreach(string text in new[]{"Не назначено","Боковая Назад (X1)","Боковая Вперёд (X2)"})mouse.Items.Add(T(text));mouse.SelectedIndex=store.Get("micmouse","0") switch{"1"=>1,"2"=>2,_=>0};content.Children.Add(mouse);
        content.Children.Add(new TextBlock{Text=T("Выбранная боковая кнопка переключает микрофон вместо перехода назад/вперёд. В Logi Options+ можно назначить Ctrl+Alt+M на кнопку MX Vertical. Зелёный — включён, красный — выключен, жёлтый — смешанный статус, серый — недоступен. Статус означает отключение звука в Windows, а не использование микрофона приложением."),TextWrapping=TextWrapping.Wrap,FontSize=12,Opacity=.7});
        panel.Children.Add(new Expander{Header=T("Микрофон"),Content=content,HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch});
        return ()=>{store.SetMany(new Dictionary<string,string>{{"micenabled",enabled.IsChecked==true?"1":"0"},{"micmode",mode.SelectedIndex switch{1=>"all",2=>"selected",_=>"default"}},{"micids",JsonSerializer.Serialize(devices.Where(d=>d.Box.IsChecked==true).Select(d=>d.Id))},{"mickey",key.SelectedIndex switch{1=>"F3",2=>"none",_=>"M"}},{"micmouse",mouse.SelectedIndex.ToString()}});StartMicrophone();};
    }
}
