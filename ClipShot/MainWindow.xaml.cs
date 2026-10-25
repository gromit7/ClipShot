using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Windowing;
using Microsoft.Win32;
using System.Runtime.InteropServices;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;
using Windows.System;

namespace ClipShot;
public sealed partial class MainWindow : Window
{
    private readonly Store store;
    private readonly nint hwnd;
    private readonly Native.WndProc procedure;
    private readonly nint previous;
    private Native.NotifyIcon tray;
    private readonly uint taskbarCreated=Native.RegisterWindowMessage("TaskbarCreated");
    private readonly bool shellTest=Environment.GetCommandLineArgs().Contains("--shell-test");
    private bool trayReady,historyKeyReady,screenshotKeyReady;
    private int historyHotkeyCount,screenshotHotkeyCount;
    private readonly bool isolated;
    private bool exiting, capturing, captureAgain, rendering, dialogOpen;
    private bool takingScreenshot;
    private RegionCaptureWindow? captureWindow;
    private uint ownSequence;
    private readonly List<(Clip Clip, Border Row)> visible = [];
    private readonly Dictionary<string,bool> expanded = new() { ["Шаблоны"]=true,["Сегодня"]=true };
    private int selected=-1;
    private readonly DispatcherTimer hoverTimer = new() { Interval=TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer leaveTimer = new() { Interval=TimeSpan.FromMilliseconds(250) };
    private readonly DispatcherTimer cleanupTimer = new() { Interval=TimeSpan.FromHours(1) };
    private Clip? hovered;
    private Border? hoverAnchor;
    private readonly Popup preview = new() { IsLightDismissEnabled=false,ShouldConstrainToRootBounds=false };
    private bool overPreview;
    private string dataFolder;

    public MainWindow(bool smoke)
    {
        InitializeComponent(); isolated=smoke;
        Status.RegisterPropertyChangedCallback(TextBlock.TextProperty,(_,_)=>Status.Visibility=string.IsNullOrEmpty(Status.Text)?Visibility.Collapsed:Visibility.Visible);
        Title="ClipShot — буфер обмена и скриншоты";
        dataFolder=TestEnvironment.DataFolder(smoke,Environment.GetEnvironmentVariable("CLIPSHOT_TEST_ROOT"));
        store=new Store(Path.Combine(dataFolder,"clips.db"));
        hwnd=WinRT.Interop.WindowNative.GetWindowHandle(this);
        ExtendsContentIntoTitleBar=true;SetTitleBar(TitleDragRegion);
        AppWindow.TitleBar.PreferredHeightOption=TitleBarHeightOption.Tall;
        Native.SetWindowLongPtr(hwnd,-16,(nint)((long)Native.GetWindowLongPtr(hwnd,-16)&~0x30000L));
        Native.SetWindowPos(hwnd,0,0,0,0,0,0x27);
        AppWindow.SetIcon(Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico"));
        AppWindow.Resize(new SizeInt32(AppSettings.WindowWidth(store.Get("width","520")),AppSettings.WindowHeight(store.Get("height","680"))));
        if(AppWindow.Presenter is OverlappedPresenter presenter) presenter.IsMaximizable=false;
        procedure=WindowMessage;
        previous=Native.SetWindowLongPtr(hwnd,-4,Marshal.GetFunctionPointerForDelegate(procedure));
        if(!isolated)
        {
            if(!Native.AddClipboardFormatListener(hwnd)) Status.Text="Не удалось подключиться к буферу обмена.";
            if(store.Get("initialized","0")=="0")
            {
                try { SetStartup(true);store.Set("startup","1");store.Set("initialized","1"); }
                catch { Status.Text="Автозапуск не включён. Проверьте настройки."; }
            }
            else if(store.Get("startup","0")=="1")
            {try{SetStartup(true);}catch{Status.Text="Не удалось обновить путь автозапуска.";}}
        }
        if(!isolated||shellTest){RegisterKey();RegisterScreenshotKey();CreateTray();}
        ApplyTheme(); Root.ActualThemeChanged+=(_,_)=>{StyleTitleBar();Render();}; Prune(); Render();
        Root.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler(RootKeyDown),true);
        hoverTimer.Tick+=(_,_)=>{hoverTimer.Stop();ShowPreview();};
        leaveTimer.Tick+=(_,_)=>{leaveTimer.Stop();if(!overPreview&&hovered==null)preview.IsOpen=false;};
        cleanupTimer.Tick+=(_,_)=>{Prune();Render();}; cleanupTimer.Start();
        AppWindow.Closing+=(_,e)=>{ if(!exiting){e.Cancel=true;Hide();} };
        AppWindow.Changed+=(_,e)=> { if(!exiting&&e.DidSizeChange && AppWindow.Size.Width>0){ store.Set("width",AppWindow.Size.Width.ToString());store.Set("height",AppWindow.Size.Height.ToString()); } };
        if(isolated) SeedSmoke();
    }
    private void Prune()=>store.Prune(AppSettings.RetentionDays(store.Get("days","90")));
    private bool RegisterKey()
    {
        bool ok=Native.RegisterHotKey(hwnd,1,Win32Hotkey.Modifiers(store.Get("ctrl","0")=="1"),AppSettings.FunctionKey(store.Get("key","112"),112));
        historyKeyReady=ok;if(!ok)Status.Text="Клавиша истории занята другой программой. Выберите другую в настройках.";return ok;
    }
    private bool RegisterScreenshotKey()
    {
        bool ok=Native.RegisterHotKey(hwnd,2,Win32Hotkey.Modifiers(store.Get("shotctrl","0")=="1"),AppSettings.FunctionKey(store.Get("shotkey","113"),113));
        screenshotKeyReady=ok;if(!ok)Status.Text="Клавиша снимка занята другой программой. Выберите другую в настройках.";return ok;
    }
    private void CreateTray()
    {
        if(tray.Icon!=0)Native.DestroyIcon(tray.Icon);
        tray=new(){Size=(uint)Marshal.SizeOf<Native.NotifyIcon>(),Hwnd=hwnd,Id=1,Flags=7,Callback=0x8001,Icon=Native.LoadImage(0,Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico"),1,32,32,0x10),Tip="ClipShot · Alt+F1 — история · Alt+F2 — снимок",Info="",InfoTitle=""};
        trayReady=tray.Icon!=0&&Native.Shell_NotifyIcon(0,ref tray);
        if(!trayReady)Status.Text="Не удалось добавить значок в трей. Перезапустите приложение.";
    }
    private nint WindowMessage(nint h,uint msg,nint w,nint l)
    {
        if(msg==App.ShowMessage){DispatcherQueue.TryEnqueue(ShowNearCursor);return 0;}
        if(msg==App.QuitMessage){DispatcherQueue.TryEnqueue(Quit);return 0;}
        if(msg==taskbarCreated&&(!isolated||shellTest)){DispatcherQueue.TryEnqueue(CreateTray);return 0;}
        if(msg==0x031D && !isolated) { DispatcherQueue.TryEnqueue(()=>Capture()); }
        if(msg==0x0312)
        {
            if((int)w==2){screenshotHotkeyCount++;DispatcherQueue.TryEnqueue(()=>TakeScreenshot());}
            else {historyHotkeyCount++;DispatcherQueue.TryEnqueue(ShowNearCursor);}
            if(shellTest)WriteShellReport();return 0;
        }
        if(msg==0x8001)
        {
            int mouse=(int)l;
            if(mouse==0x0202)DispatcherQueue.TryEnqueue(ShowNearCursor);
            if(mouse==0x0205)
            {
                Native.GetCursorPos(out var p);Native.SetForegroundWindow(hwnd);
                nint menu=Native.CreatePopupMenu();Native.AppendMenu(menu,0,1,"Открыть историю");Native.AppendMenu(menu,0,4,"Снимок выделенной области");Native.AppendMenu(menu,0,2,"Настройки");Native.AppendMenu(menu,0,3,"Выход");
                int command=Native.TrackPopupMenu(menu,0x100,p.X,p.Y,0,hwnd,0);Native.DestroyMenu(menu);
                DispatcherQueue.TryEnqueue(()=>{if(command==1)ShowNearCursor();if(command==2){ShowNearCursor();SettingsClick(this,new());}if(command==4)TakeScreenshot();if(command==3)Quit();});
            }
            return 0;
        }
        return Native.CallWindowProc(previous,h,msg,w,l);
    }
    public void ShowNearCursor()
    {
        if(takingScreenshot||exiting)return;
        Prune();Render();Native.GetCursorPos(out var p);
        var area=DisplayArea.GetFromPoint(new PointInt32(p.X,p.Y),DisplayAreaFallback.Nearest).WorkArea;
        int x=Math.Clamp(p.X+12,area.X,Math.Max(area.X,area.X+area.Width-AppWindow.Size.Width));
        int y=Math.Clamp(p.Y+12,area.Y,Math.Max(area.Y,area.Y+area.Height-AppWindow.Size.Height));
        AppWindow.Move(new PointInt32(x,y));AppWindow.Show();Activate();Native.SetForegroundWindow(hwnd);Search.Focus(FocusState.Programmatic);
    }
    private void Hide(){preview.IsOpen=false;hoverTimer.Stop();AppWindow.Hide();Search.Text="";}
    private void Quit()
    {
        if(exiting)return;
        exiting=true;hoverTimer.Stop();leaveTimer.Stop();cleanupTimer.Stop();
        captureWindow?.Cancel();
        if(!isolated)Native.RemoveClipboardFormatListener(hwnd);
        if(!isolated||shellTest){Native.UnregisterHotKey(hwnd,1);Native.UnregisterHotKey(hwnd,2);Native.Shell_NotifyIcon(2,ref tray);if(tray.Icon!=0)Native.DestroyIcon(tray.Icon);}
        Native.SetWindowLongPtr(hwnd,-4,previous);store.Dispose();Close();Application.Current.Exit();
    }
    private async void TakeScreenshot()
    {
        if(takingScreenshot||dialogOpen)return;
        takingScreenshot=true;
        try
        {
            Hide();await Task.Delay(160);DesktopCapture.DwmFlush();
            var frame=shellTest?SyntheticFrame():DesktopCapture.ReadDesktop();captureWindow=new RegionCaptureWindow(frame,shellTest);
            var selectedFrame=await captureWindow.SelectAsync();captureWindow=null;
            if(selectedFrame is null||exiting)return;
            byte[] png=await DesktopCapture.Png(selectedFrame);
            if(exiting)return;
            // No full-desktop image is written to disk. Only the selected crop enters history.
            using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}stream.Seek(0);
            if(!isolated){var data=new DataPackage{RequestedOperation=DataPackageOperation.Copy};data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));Clipboard.SetContent(data);Clipboard.Flush();ownSequence=Native.GetClipboardSequenceNumber();}
            if(exiting)return;store.Add("image","",image:png);Render();
            if(shellTest)WriteShellReport($"Crop={selectedFrame.Width}x{selectedFrame.Height}");
        }
        catch
        {
            captureWindow?.Cancel();captureWindow=null;
            if(!exiting){takingScreenshot=false;ShowNearCursor();Status.Text="Не удалось сделать снимок. Повторите попытку.";}
        }
        finally{takingScreenshot=false;}
    }
    private async void Capture()
    {
        if(capturing){captureAgain=true;return;}
        capturing=true;
        try
        {
            for(int attempt=0;attempt<4;attempt++)
            {
                uint sequence=Native.GetClipboardSequenceNumber();
                if(sequence==ownSequence)break;
                try
                {
                    var content=Clipboard.GetContent();
                    if(content.Contains(StandardDataFormats.Bitmap))
                    {
                        using var source=await (await content.GetBitmapAsync()).OpenReadAsync();
                        var decoder=await BitmapDecoder.CreateAsync(source);
                        using var bitmap=await decoder.GetSoftwareBitmapAsync();
                        using var output=new InMemoryRandomAccessStream();
                        var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,output);encoder.SetSoftwareBitmap(bitmap);await encoder.FlushAsync();
                        if(output.Size>50*1024*1024){Status.Text="Изображение больше 50 МБ не сохранено.";break;}
                        output.Seek(0);byte[] bytes=new byte[(int)output.Size];using var reader=new DataReader(output);await reader.LoadAsync((uint)bytes.Length);reader.ReadBytes(bytes);
                        store.Add("image","",image:bytes);
                    }
                    else if(content.Contains(StandardDataFormats.Text))
                    {
                        string text=await content.GetTextAsync();
                        if(text.Length>0)store.Add("text",text,content.Contains(StandardDataFormats.Html)?await content.GetHtmlFormatAsync():"",content.Contains(StandardDataFormats.Rtf)?await content.GetRtfAsync():"");
                    }
                    Render();break;
                }
                catch when(attempt<3){await Task.Delay(100*(attempt+1));}
                catch {Status.Text="Не удалось прочитать буфер обмена. Следующие записи будут обработаны.";}
            }
        }
        finally {capturing=false;if(captureAgain){captureAgain=false;Capture();}}
    }
    private async Task Copy(Clip clip,bool plain=false)
    {
        if(exiting)return;
        try
        {
            if(isolated){store.Add(clip.Kind,clip.Text,clip.Html,clip.Rtf,clip.Image);Hide();return;}
            var data=new DataPackage();data.RequestedOperation=DataPackageOperation.Copy;
            if(clip.Image is not null)
            {
                using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(clip.Image);await writer.StoreAsync();}
                stream.Seek(0);data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));Clipboard.SetContent(data);Clipboard.Flush();
            }
            else
            {
                data.SetText(clip.Text);if(!plain){if(clip.Html.Length>0)data.SetHtmlFormat(clip.Html);if(clip.Rtf.Length>0)data.SetRtf(clip.Rtf);}Clipboard.SetContent(data);Clipboard.Flush();
            }
            ownSequence=Native.GetClipboardSequenceNumber();
            // Choosing an existing clip is a new use, without recording a duplicate.
            store.Add(clip.Kind,clip.Text,clip.Html,clip.Rtf,clip.Image);Hide();
        }
        catch {Status.Text="Не удалось скопировать. Повторите выбор.";}
    }
    private void SearchChanged(object sender,TextChangedEventArgs e){if(!exiting&&store is not null)Render();}
    private void Render()
    {
        if(rendering||exiting)return;rendering=true;
        try
        {
            hoverTimer.Stop();preview.IsOpen=false;hovered=null;
            long? previousId=selected>=0&&selected<visible.Count?visible[selected].Clip.Id:null;
            Groups.Children.Clear();visible.Clear();
            string query=Search.Text.Trim();
            var groups=HistoryView.Groups(store.All(),query,store.Get("templateorder",""),DateTime.Today);
            foreach(var group in groups)AddGroup(group.Title,group.Clips,query.Length>0);
            if(groups.All(g=>g.Clips.Count==0))Groups.Children.Add(new TextBlock{Text=query.Length>0?"Ничего не найдено":"Скопируйте текст или картинку — они появятся здесь.",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(8,24,8,8),Opacity=.65});
            selected=previousId.HasValue?visible.FindIndex(v=>v.Clip.Id==previousId):-1;
            if(selected<0&&visible.Count>0)selected=0;Highlight();
        }
        finally{rendering=false;}
    }
    private void AddGroup(string title,List<Clip> clips,bool searching)
    {
        if(searching&&clips.Count==0)return;
        bool open=searching||expanded.GetValueOrDefault(title);
        var heading=new Grid{ColumnSpacing=10};heading.ColumnDefinitions.Add(new(){Width=new GridLength(14)});heading.ColumnDefinitions.Add(new(){Width=new GridLength(20)});heading.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});heading.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        var arrow=new FontIcon{Glyph=open?"\uE70D":"\uE76C",FontSize=11};heading.Children.Add(arrow);
        if(title is "Шаблоны" or "Сегодня"){var icon=new FontIcon{Glyph=title=="Шаблоны"?"\uE8A5":"\uE823",FontSize=18,Foreground=ClipBrush("ClipSecondaryBrush")};Grid.SetColumn(icon,1);heading.Children.Add(icon);}
        var label=new TextBlock{Text=title,FontSize=14,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold};Grid.SetColumn(label,2);heading.Children.Add(label);
        var count=new TextBlock{Text=clips.Count.ToString(),FontSize=13,Foreground=ClipBrush("ClipSecondaryBrush")};Grid.SetColumn(count,3);heading.Children.Add(count);
        var button=new Button{Content=heading,Height=46,CornerRadius=new CornerRadius(0),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent),BorderThickness=new Thickness(0),Padding=new Thickness(18,0,20,0)};
        button.Click+=(_,_)=>{expanded[title]=!open;Render();};Groups.Children.Add(new Border{Child=button,BorderBrush=ClipBrush("ClipSeparatorBrush"),BorderThickness=new Thickness(0,0,0,1)});
        if(!open)return;
        foreach(var clip in clips)
        {
            var grid=new Grid{ColumnSpacing=12,Margin=new Thickness(20,10,20,10),MinHeight=30};grid.ColumnDefinitions.Add(new(){Width=new GridLength(clip.Image is null?26:76)});grid.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            var text=new TextBlock{Text=clip.Label,Foreground=primaryBrush,FontSize=14,MaxLines=1,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};
            if(clip.Image is not null)
            {
                var img=new Image{Width=74,Height=44,Stretch=Stretch.UniformToFill};grid.Children.Add(new Border{Child=img,CornerRadius=new CornerRadius(4),HorizontalAlignment=HorizontalAlignment.Left});
                var sub=new TextBlock{Text="PNG",FontSize=12,Foreground=ClipBrush("ClipSecondaryBrush")};
                var details=new StackPanel{Spacing=3,VerticalAlignment=VerticalAlignment.Center};details.Children.Add(text);details.Children.Add(sub);Grid.SetColumn(details,1);grid.Children.Add(details);LoadThumbnail(img,sub,clip.Image);
            }
            else {grid.Children.Add(new FontIcon{Glyph=Uri.TryCreate(clip.Text,UriKind.Absolute,out var uri)&&uri.Scheme is "http" or "https"?"\uE71B":"\uE8A5",FontSize=19,Foreground=ClipBrush("ClipSecondaryBrush")});Grid.SetColumn(text,1);grid.Children.Add(text);}
            var time=new TextBlock{Text=clip.Template?"":clip.Seen.Date==DateTime.Today?clip.Seen.ToString("HH:mm"):clip.Seen.ToString("dd.MM"),FontSize=12,Foreground=ClipBrush("ClipSecondaryBrush"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(time,2);grid.Children.Add(time);
            var indicator=new Border{Width=3,Height=28,CornerRadius=new CornerRadius(2),Background=ClipBrush("ClipIndicatorBrush"),HorizontalAlignment=HorizontalAlignment.Left,VerticalAlignment=VerticalAlignment.Center,Margin=new Thickness(3,0,0,0),Visibility=Visibility.Collapsed};
            var contents=new Grid();contents.Children.Add(grid);contents.Children.Add(indicator);
            var row=new Border{Child=contents,Tag=indicator,BorderBrush=ClipBrush("ClipSeparatorBrush"),BorderThickness=new Thickness(0,0,0,1),Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent)};
            int index=visible.Count;visible.Add((clip,row));Groups.Children.Add(row);
            row.Tapped+=async(_,_)=>await Copy(clip);
            row.PointerEntered+=(_,_)=>{selected=index;Highlight();hovered=clip;hoverAnchor=row;hoverTimer.Stop();hoverTimer.Start();};
            row.PointerExited+=(_,_)=>{hoverTimer.Stop();hovered=null;leaveTimer.Start();};
            var menu=new MenuFlyout();
            void Item(string title,Action action){var item=new MenuFlyoutItem{Text=title};item.Click+=(_,_)=>action();menu.Items.Add(item);}
            Item("Копировать",async()=>await Copy(clip));
            if(clip.Kind=="text")Item("Копировать как обычный текст",async()=>await Copy(clip,true));
            Item(clip.Template?"Убрать из шаблонов":"В шаблоны · Ctrl+S",()=>{store.Template(clip.Id,!clip.Template);Render();});
            if(clip.Template)
            {
                Item("Изменить шаблон…",async()=>await Rename(clip));
                Item("Переместить выше",()=>MoveTemplate(clip.Id,-1));
                Item("Переместить ниже",()=>MoveTemplate(clip.Id,1));
            }
            Item("Удалить",()=>{store.Delete(clip.Id);Render();});row.ContextFlyout=menu;
        }
    }
    private void Highlight()
    {
        for(int i=0;i<visible.Count;i++)
        {visible[i].Row.Background=i==selected?ClipBrush("ClipSelectionBrush"):new SolidColorBrush(Microsoft.UI.Colors.Transparent);StyleRow(visible[i].Row.Child,i==selected);if(visible[i].Row.Tag is Border indicator)indicator.Visibility=i==selected?Visibility.Visible:Visibility.Collapsed;}
    }
    private Brush ClipBrush(string key)=>(Brush)((ResourceDictionary)Root.Resources.ThemeDictionaries[Root.ActualTheme==ElementTheme.Light?"Light":"Default"])[key];
    private static async void LoadThumbnail(Image image,TextBlock detail,byte[] bytes)
    {try{await LoadImageAsync(image,bytes);if(image.Source is BitmapImage bitmap)detail.Text=$"{bitmap.PixelWidth} × {bitmap.PixelHeight} · PNG";}catch{detail.Text="PNG";}}
    private void RootKeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(dialogOpen)return;
        if(e.Key==VirtualKey.Escape){Hide();e.Handled=true;return;}
        if(e.Key==VirtualKey.Down||e.Key==VirtualKey.Up)
        {if(visible.Count>0){selected=Math.Clamp(selected+(e.Key==VirtualKey.Down?1:-1),0,visible.Count-1);Highlight();visible[selected].Row.StartBringIntoView();}e.Handled=true;}
        if(e.Key==VirtualKey.Enter&&selected>=0){_=Copy(visible[selected].Clip);e.Handled=true;}
        if(e.Key==VirtualKey.S && (Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0 && selected>=0)
        {store.Template(visible[selected].Clip.Id,true);Render();e.Handled=true;}
    }
    private async void ShowPreview()
    {
        if(exiting)return;
        try
        {
        var clip=hovered;var anchor=hoverAnchor;if(clip is null||anchor is null)return;overPreview=false;
        var body=new StackPanel{Spacing=10,Width=400,RequestedTheme=Root.ActualTheme};
        body.Children.Add(new TextBlock{Text=clip.Kind=="image"?"Изображение":clip.Name.Length>0?clip.Name:"Текст",FontWeight=Microsoft.UI.Text.FontWeights.SemiBold});
        if(clip.Image is not null){var img=new Image{MaxHeight=420,Stretch=Stretch.Uniform};body.Children.Add(img);await LoadImageAsync(img,clip.Image);}
        else body.Children.Add(new ScrollViewer{MaxHeight=420,IsTabStop=false,Content=new TextBlock{Text=clip.Text,TextWrapping=TextWrapping.Wrap,IsTextSelectionEnabled=false}});
        body.PointerEntered+=(_,_)=>{overPreview=true;leaveTimer.Stop();};body.PointerExited+=(_,_)=>{overPreview=false;leaveTimer.Start();};
        if(exiting||hovered?.Id!=clip.Id)return;
        PaintText(body,primaryBrush);
        var border=new Border{Child=body,Padding=new Thickness(16),CornerRadius=new CornerRadius(activeTheme.Corner),BorderThickness=new Thickness(1),RequestedTheme=Root.ActualTheme,Background=ClipBrush("ClipSearchBrush"),BorderBrush=ClipBrush("ClipSeparatorBrush")};
        preview.Child=border;preview.XamlRoot=Root.XamlRoot;
        double scale=Root.XamlRoot.RasterizationScale;
        var point=anchor.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,0));
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
        double width=432,height=clip.Image is null?Math.Min(480,80+clip.Text.Length/45*20):500;
        double right=AppWindow.Position.X+(Root.ActualWidth+width+12)*scale;
        preview.HorizontalOffset=right<=area.X+area.Width?Root.ActualWidth+8:-width-8;
        preview.VerticalOffset=Math.Max(0,Math.Min(point.Y,(area.Y+area.Height-AppWindow.Position.Y)/scale-height-40));
        preview.IsOpen=true;
        Search.Focus(FocusState.Programmatic);
        if(isolated)DispatcherQueue.TryEnqueue(()=>File.WriteAllText(Path.Combine(dataFolder,"preview-ok.txt"),$"PreviewOpen={preview.IsOpen}; Focus={(FocusManager.GetFocusedElement(Root.XamlRoot) as FrameworkElement)?.Name}"));
        }
        catch
        {
            if(!exiting){preview.IsOpen=false;Status.Text="Не удалось показать предпросмотр этой записи.";}
        }
    }
    private static async void LoadImage(Image image,byte[] bytes){try{await LoadImageAsync(image,bytes);}catch{}}
    private static async Task LoadImageAsync(Image image,byte[] bytes)
    {using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(bytes);await writer.StoreAsync();}stream.Seek(0);var bitmap=new BitmapImage();await bitmap.SetSourceAsync(stream);image.Source=bitmap;}
    private async Task Rename(Clip clip)
    {
        var text=new TextBox{Text=clip.Name,PlaceholderText="Название необязательно"};
        var content=new TextBox{Text=clip.Text,AcceptsReturn=true,TextWrapping=TextWrapping.Wrap,MinHeight=100,MaxHeight=280,Header="Содержимое"};
        var panel=new StackPanel{Spacing=12,MinWidth=320};panel.Children.Add(text);if(clip.Kind=="text")panel.Children.Add(content);
        var dialog=new ContentDialog{Title="Изменить шаблон",Content=panel,PrimaryButtonText="Сохранить",CloseButtonText="Отмена",XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
        dialogOpen=true;var result=await dialog.ShowAsync();dialogOpen=false;
        if(result==ContentDialogResult.Primary)
        {
            try {if(clip.Kind=="text"){if(string.IsNullOrWhiteSpace(content.Text)){Status.Text="Пустой шаблон не сохранён.";return;}store.EditText(clip.Id,text.Text.Trim(),content.Text);}else store.Edit(clip.Id,text.Text.Trim());Render();}
            catch(Microsoft.Data.Sqlite.SqliteException){Status.Text="Такой текст уже есть в истории. Добавьте существующий клип в шаблоны.";}
        }
    }
    private void MoveTemplate(long id,int direction)
    {
        var ids=visible.Where(v=>v.Clip.Template).Select(v=>v.Clip.Id).ToList();int i=ids.IndexOf(id),j=i+direction;
        if(i<0||j<0||j>=ids.Count)return;(ids[i],ids[j])=(ids[j],ids[i]);store.Set("templateorder",string.Join(',',ids));Render();
    }
    private void SetStartup(bool value)
    {
        using var key=Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
        if(value)key.SetValue("ClipNest",$"\"{Environment.ProcessPath}\" --background");else key.DeleteValue("ClipNest",false);
    }
    private async void SettingsClick(object sender,RoutedEventArgs e)
    {
        if(dialogOpen||exiting)return;
        preview.IsOpen=false;
        var panel=new StackPanel{Spacing=12,MinWidth=320};
        var days=new ComboBox{Header="Хранить историю",HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var s in new[]{"30 дней","90 дней","Бессрочно"})days.Items.Add(s);days.SelectedIndex=store.Get("days","90") switch{"30"=>0,"0"=>2,_=>1};panel.Children.Add(days);
        var theme=new ComboBox{Header="Тема",HorizontalAlignment=HorizontalAlignment.Stretch};foreach(var item in AppThemes.All)theme.Items.Add(item.Name);theme.SelectedIndex=AppThemes.All.ToList().FindIndex(t=>t.Id==activeTheme.Id);panel.Children.Add(theme);
        ComboBox HotkeyCombo(string header,string keyName,string ctrlName,int fallback)
        {
            var box=new ComboBox{Header=header,HorizontalAlignment=HorizontalAlignment.Stretch};
            for(int modifier=0;modifier<2;modifier++)for(int f=1;f<=12;f++)box.Items.Add((modifier==0?"Alt+":"Ctrl+Alt+")+"F"+f);
            box.SelectedIndex=(int)AppSettings.FunctionKey(store.Get(keyName,fallback.ToString()),(uint)fallback)-112+(store.Get(ctrlName,"0")=="1"?12:0);return box;
        }
        var keys=HotkeyCombo("Открыть историю","key","ctrl",112);panel.Children.Add(keys);
        var shotKeys=HotkeyCombo("Снимок выделенной области","shotkey","shotctrl",113);panel.Children.Add(shotKeys);
        var keyError=new TextBlock{TextWrapping=TextWrapping.Wrap,MaxWidth=340};panel.Children.Add(keyError);
        var startup=new CheckBox{Content="Запускать с Windows",IsChecked=store.Get("startup","1")=="1"};panel.Children.Add(startup);
        var clear=new Button{Content="Очистить историю…"}; bool wantsClear=false;clear.Click+=(_,_)=>{wantsClear=true;clear.Content="История будет очищена после сохранения";};panel.Children.Add(clear);
        panel.Children.Add(new TextBlock{Text="Шаблоны не удаляются. Данные хранятся только на этом ПК.",TextWrapping=TextWrapping.Wrap,MaxWidth=340,Opacity=.65,FontSize=12});
        panel.Children.Add(new TextBlock{Text=$"ClipShot {typeof(App).Assembly.GetName().Version?.ToString(3)} · буфер обмена и скриншоты",TextWrapping=TextWrapping.Wrap,MaxWidth=340,Opacity=.65,FontSize=12});
        var dialog=new ContentDialog{Title="Настройки ClipShot",Content=panel,PrimaryButtonText="Сохранить",CloseButtonText="Отмена",XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
        dialog.PrimaryButtonClick+=(_,args)=>
        {
            if(keys.SelectedIndex==shotKeys.SelectedIndex){keyError.Text="Для истории и снимка нужны разные сочетания.";args.Cancel=true;return;}
            string oldKey=store.Get("key","112"),oldCtrl=store.Get("ctrl","0"),oldShot=store.Get("shotkey","113"),oldShotCtrl=store.Get("shotctrl","0");
            if(!isolated){Native.UnregisterHotKey(hwnd,1);Native.UnregisterHotKey(hwnd,2);}
            store.SetMany(new Dictionary<string,string>{{"key",(112+keys.SelectedIndex%12).ToString()},{"ctrl",keys.SelectedIndex>=12?"1":"0"},{"shotkey",(112+shotKeys.SelectedIndex%12).ToString()},{"shotctrl",shotKeys.SelectedIndex>=12?"1":"0"}});
            if(!isolated)
            {
                bool historyOk=RegisterKey(),shotOk=RegisterScreenshotKey();
                if(!historyOk||!shotOk)
                {
                    Native.UnregisterHotKey(hwnd,1);Native.UnregisterHotKey(hwnd,2);
                    store.SetMany(new Dictionary<string,string>{{"key",oldKey},{"ctrl",oldCtrl},{"shotkey",oldShot},{"shotctrl",oldShotCtrl}});RegisterKey();RegisterScreenshotKey();
                    keyError.Text="Одно из сочетаний занято другой программой. Выберите другое.";args.Cancel=true;
                }
            }
        };
        dialogOpen=true;var result=await dialog.ShowAsync();dialogOpen=false;
        if(result!=ContentDialogResult.Primary)return;
        store.Set("days",days.SelectedIndex switch{0=>"30",2=>"0",_=>"90"});store.Set("theme",AppThemes.All[Math.Max(0,theme.SelectedIndex)].Id);
        try{if(!isolated)SetStartup(startup.IsChecked==true);store.Set("startup",startup.IsChecked==true?"1":"0");}catch{Status.Text="Не удалось изменить автозапуск.";}
        if(wantsClear)store.Clear();ApplyTheme();Prune();Render();
    }
    private async void SeedSmoke()
    {
        if(Environment.GetCommandLineArgs().Contains("--upgrade-self-test")){RunUpgradeChecks();return;}
        if(Environment.GetCommandLineArgs().Contains("--light"))store.Set("theme","White");
        if(Environment.GetCommandLineArgs().Contains("--dark"))store.Set("theme","Dark");
        var themeArg=Environment.GetCommandLineArgs().FirstOrDefault(a=>a.StartsWith("--theme=",StringComparison.Ordinal));if(themeArg is not null)store.Set("theme",themeArg[8..]);ApplyTheme();
        foreach(var clip in store.All())store.Delete(clip.Id);store.Add("text","Тестовая запись ClipShot");store.Add("text","test@example.com");
        store.Add("text","Текст для нового документа…");store.Add("text","https://example.com/project");store.Add("text","Список задач на завтра…");
        using(var stream=new InMemoryRandomAccessStream())
        {
            var encoder=await BitmapEncoder.CreateAsync(BitmapEncoder.PngEncoderId,stream);byte[] pixels=new byte[320*180*4];
            for(int y=0;y<180;y++)for(int x=0;x<320;x++){int i=(y*320+x)*4;pixels[i]=(byte)(80+x/3);pixels[i+1]=(byte)(80+y/2);pixels[i+2]=80;pixels[i+3]=255;}
            encoder.SetPixelData(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Ignore,320,180,96,96,pixels);await encoder.FlushAsync();stream.Seek(0);
            byte[] png=new byte[(int)stream.Size];using var reader=new DataReader(stream);await reader.LoadAsync((uint)png.Length);reader.ReadBytes(png);store.Add("image","",image:png);
        }
        var item=store.All().First(c=>c.Text=="test@example.com");store.Template(item.Id,true);Render();
        if(shellTest){WriteShellReport();return;}
        if(Environment.GetCommandLineArgs().Any(a=>a is "--capture-self-test" or "--capture-demo"))
        {await RunCaptureChecks(Environment.GetCommandLineArgs().Contains("--capture-demo"));return;}
        if(Environment.GetCommandLineArgs().Contains("--demo"))
        {
            if(Environment.GetCommandLineArgs().Contains("--preview"))
            {
                var t=new DispatcherTimer{Interval=TimeSpan.FromSeconds(1)};t.Tick+=(_,_)=>{t.Stop();var row=Environment.GetCommandLineArgs().Contains("--image-preview")?visible.First(v=>v.Clip.Kind=="image"):visible[0];hovered=row.Clip;hoverAnchor=row.Row;ShowPreview();};t.Start();
            }
            return;
        }
        var timer=new DispatcherTimer{Interval=TimeSpan.FromSeconds(3)};
        timer.Tick+=async(_,_)=>
        {
            timer.Stop();Directory.CreateDirectory(dataFolder);
            var p=SettingsButton.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point(0,0));
            bool layout=Search.ActualHeight>=40&&Search.ActualWidth>300&&TitleDragRegion.ActualWidth>200&&p.X+SettingsButton.ActualWidth<=Root.ActualWidth-AppWindow.TitleBar.RightInset+1;
            bool pass=layout&&Root.XamlRoot is not null&&visible.Count==6;
            File.WriteAllText(Path.Combine(dataFolder,"ui-ok.txt"),$"{(pass?"PASS":"FAIL")} WinUI launched; visible rows={visible.Count}; XamlRoot={Root.XamlRoot is not null}; Theme={Root.ActualTheme}; ThemeId={activeTheme.Id}; Acrylic={SystemBackdrop is DesktopAcrylicBackdrop}; Layout={layout}; CaptionInset={AppWindow.TitleBar.RightInset}; SearchWidth={Search.ActualWidth}; SettingsX={p.X}");
            if(Environment.GetCommandLineArgs().Contains("--interaction-self-test"))pass=await RunInteractionChecks()&&pass;
            if(!pass)Environment.ExitCode=1;Quit();
        };timer.Start();
    }
}
