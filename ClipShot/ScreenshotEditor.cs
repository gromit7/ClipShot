using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Shapes;
using Microsoft.UI.Windowing;
using System.Runtime.InteropServices.WindowsRuntime;
using Windows.Graphics;
using Windows.Graphics.Imaging;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;
using Windows.System;

namespace ClipShot;
internal sealed partial class ScreenshotEditor : Window
{
    private readonly ScreenFrame source;
    private readonly UiText words;
    private readonly AnnotationHistory history=new();
    private readonly Grid root=new();
    private sealed class AimCanvas:Canvas {internal AimCanvas(){ProtectedCursor=Microsoft.UI.Input.InputSystemCursor.Create(Microsoft.UI.Input.InputSystemCursorShape.Cross);}}
    private readonly AimCanvas surface=new();
    private readonly Image image=new(){Stretch=Stretch.Fill};
    private readonly Canvas guides=new(){IsHitTestVisible=false};
    private readonly TextBlock status=new(){Margin=new Thickness(16,8,16,10),TextWrapping=TextWrapping.Wrap,Opacity=.75,FontSize=12};
    private readonly TaskCompletionSource<byte[]?> completion=new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly Dictionary<MarkTool,AppBarToggleButton> tools=new();
    private readonly AppBarButton undo=new(),redo=new();
    private readonly CommandBar toolbar=new(){DefaultLabelPosition=CommandBarDefaultLabelPosition.Bottom,IsDynamicOverflowEnabled=false,OverflowButtonVisibility=CommandBarOverflowButtonVisibility.Collapsed,Width=1000};
    private readonly AppBarButton copy=new();
    private MarkTool tool=MarkTool.Arrow;
    private ImagePoint start,end;
    private bool dragging,dirty,closed,busy,dialog;
    private string color="#E53935";
    private int revision;
    private float Stroke=>Math.Clamp(Math.Min(source.Width,source.Height)/150f,3,10);
    private string T(string text)=>words.Translate(text);
    private ScreenshotEditor(ScreenFrame source,UiText words,ElementTheme theme)
    {
        this.source=source;this.words=words;
        Title=T("Разметка снимка — ClipShot");root.RequestedTheme=theme;
        root.Background=new SolidColorBrush(theme==ElementTheme.Dark?Microsoft.UI.ColorHelper.FromArgb(255,32,33,37):Microsoft.UI.Colors.WhiteSmoke);
        root.RowDefinitions.Add(new(){Height=GridLength.Auto});root.RowDefinitions.Add(new(){Height=new(1,GridUnitType.Star)});root.RowDefinitions.Add(new(){Height=GridLength.Auto});Content=root;
        var bar=toolbar;
        void Tool(MarkTool kind,string label,string glyph){var b=new AppBarToggleButton{Label=T(label),Icon=new FontIcon{Glyph=glyph},IsChecked=kind==MarkTool.Arrow,Width=kind==MarkTool.Redact?106:76,FontSize=12};ToolTipService.SetToolTip(b,T(label));b.Click+=(_,_)=>{tool=kind;foreach(var pair in tools)pair.Value.IsChecked=pair.Key==kind;Hint();};tools[kind]=b;bar.PrimaryCommands.Add(b);}
        Tool(MarkTool.Arrow,"Стрелка","\uE72A");Tool(MarkTool.Rectangle,"Рамка","\uE739");Tool(MarkTool.Text,"Подпись","\uE8D2");
        Tool(MarkTool.Blur,"Мозаика","\uE7A7");Tool(MarkTool.Redact,"Скрыть область","\uE72E");
        undo.Label=T("Отменить");undo.Width=76;undo.FontSize=12;undo.Icon=new SymbolIcon(Symbol.Undo);undo.Click+=(_,_)=>{if(busy)return;history.Undo();_=RefreshAsync();};bar.PrimaryCommands.Add(undo);
        redo.Label=T("Повторить");redo.Width=76;redo.FontSize=12;redo.Icon=new SymbolIcon(Symbol.Redo);redo.Click+=(_,_)=>{if(busy)return;history.Redo();_=RefreshAsync();};bar.PrimaryCommands.Add(redo);
        copy.Label=T("Копировать в буфер");copy.Width=150;copy.FontSize=12;copy.Icon=new SymbolIcon(Symbol.Copy);ToolTipService.SetToolTip(copy,T("Копировать в буфер · Enter"));copy.Click+=async(_,_)=>await CopyAsync();bar.PrimaryCommands.Add(copy);
        var save=new AppBarButton{Label=T("Экспорт…"),Width=108,FontSize=12,Icon=new SymbolIcon(Symbol.Save)};ToolTipService.SetToolTip(save,T("Экспорт PNG или JPEG · Ctrl+S"));save.Click+=async(_,_)=>await SaveAsync();bar.PrimaryCommands.Add(save);
        var colors=new ComboBox{Width=116,Margin=new Thickness(8,0,8,0)};
        foreach(string name in new[]{"Красный","Синий","Чёрный","Белый"})colors.Items.Add(T(name));
        colors.SelectedIndex=0;
        colors.SelectionChanged+=(_,_)=>color=new[]{"#E53935","#1565C0","#111111","#FFFFFF"}[Math.Max(0,colors.SelectedIndex)];bar.Content=colors;
        root.Children.Add(new Viewbox{Child=bar,Stretch=Stretch.Uniform,StretchDirection=StretchDirection.DownOnly,HorizontalAlignment=HorizontalAlignment.Stretch});
        surface.Width=source.Width;surface.Height=source.Height;surface.Background=new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        image.Width=source.Width;image.Height=source.Height;surface.Children.Add(image);surface.Children.Add(guides);SetImage(source);
        var viewbox=new Viewbox{Child=surface,Stretch=Stretch.Uniform,Margin=new Thickness(12)};Grid.SetRow(viewbox,1);root.Children.Add(viewbox);
        Grid.SetRow(status,2);root.Children.Add(status);Hint();undo.IsEnabled=redo.IsEnabled=false;
        surface.PointerPressed+=Pressed;surface.PointerMoved+=Moved;surface.PointerReleased+=Released;surface.PointerCaptureLost+=(_,_)=>CancelDrag();
        root.AddHandler(UIElement.KeyDownEvent,new KeyEventHandler(KeyDown),true);CompositionTarget.Rendering+=DrawPending;
        Closed+=(_,_)=>{closed=true;CompositionTarget.Rendering-=DrawPending;completion.TrySetResult(null);};
        AppWindow.SetIcon(System.IO.Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico"));
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;
        AppWindow.MoveAndResize(new RectInt32(area.X+Math.Max(0,(area.Width-1100)/2),area.Y+30,Math.Min(1100,area.Width-40),Math.Min(800,area.Height-60)));
    }
    internal static async Task<ScreenshotEditor> CreateAsync(byte[] png,UiText words,ElementTheme theme)
    {
        using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream);
        if((long)decoder.PixelWidth*decoder.PixelHeight*4>512L*1024*1024)throw new InvalidOperationException("Image too large.");
        var data=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        return new(new ScreenFrame(0,0,(int)decoder.PixelWidth,(int)decoder.PixelHeight,data.DetachPixelData()),words,theme);
    }
    internal Task<byte[]?> ShowAsync(){Activate();return completion.Task;}
    internal bool IsClosed=>closed;
    internal bool CanCapture=>!closed&&!busy&&!dialog&&!dragging;
    private void SetImage(ScreenFrame frame){var bitmap=new WriteableBitmap(frame.Width,frame.Height);using(var stream=bitmap.PixelBuffer.AsStream())stream.Write(frame.Pixels);bitmap.Invalidate();image.Source=bitmap;}
    private ImagePoint Point(PointerRoutedEventArgs e){var p=e.GetCurrentPoint(surface).Position;return new ImagePoint(p.X,p.Y).Clamp(source.Width,source.Height);}
    private Annotation CurrentMark()
    {
        var a=start;var b=end;
        if(tool==MarkTool.Arrow)
        {
            var transform=surface.TransformToVisual(root);double zoom=Math.Abs(transform.TransformPoint(new Windows.Foundation.Point(1,0)).X-transform.TransformPoint(new Windows.Foundation.Point(0,0)).X);
            (a,b)=AnnotationGeometry.AimArrow(start,end,Shift(),3/Math.Max(.001,zoom));a=a.Clamp(source.Width,source.Height);
        }
        return new(tool,a,b,Color:color,Width:Stroke);
    }
    private static bool Shift()=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Shift)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
    private void Pressed(object sender,PointerRoutedEventArgs e)
    {
        if(busy||dialog)return;var p=e.GetCurrentPoint(surface);
        if(p.Properties.IsRightButtonPressed){CancelDrag();return;}if(!p.Properties.IsLeftButtonPressed)return;
        start=end=Point(e);dragging=dirty=true;surface.CapturePointer(e.Pointer);e.Handled=true;
    }
    private void Moved(object sender,PointerRoutedEventArgs e){if(dragging){end=Point(e);dirty=true;}}
    private async void Released(object sender,PointerRoutedEventArgs e)
    {
        if(!dragging)return;end=Point(e);var mark=CurrentMark();dragging=false;surface.ReleasePointerCaptures();guides.Children.Clear();
        if(tool==MarkTool.Text)
        {
            dialog=true;var text=new TextBox{PlaceholderText=T("Текст подписи"),AcceptsReturn=true,MaxLength=2000,MinWidth=280};
            var prompt=new ContentDialog{Title=T("Подпись"),Content=text,PrimaryButtonText=T("Добавить"),CloseButtonText=T("Отмена"),DefaultButton=ContentDialogButton.Primary,XamlRoot=root.XamlRoot,RequestedTheme=root.RequestedTheme};
            try{prompt.Opened+=(_,_)=>text.Focus(FocusState.Programmatic);if(await prompt.ShowAsync()==ContentDialogResult.Primary&&!string.IsNullOrWhiteSpace(text.Text))history.Add(mark with{Text=text.Text});}finally{dialog=false;}
        }
        else CommitMark(mark);
        await RefreshAsync();
    }
    private void CommitMark(Annotation mark){if(Math.Abs(mark.End.X-mark.Start.X)+Math.Abs(mark.End.Y-mark.Start.Y)>=3)history.Add(mark);}
    private void CancelDrag(){bool captured=dragging;dragging=dirty=false;if(captured)surface.ReleasePointerCaptures();guides.Children.Clear();}
    private void DrawPending(object? sender,object e)
    {
        if(!dragging||!dirty||closed)return;dirty=false;guides.Children.Clear();var mark=CurrentMark();
        uint n=Convert.ToUInt32(color.TrimStart('#'),16);var brush=new SolidColorBrush(Windows.UI.Color.FromArgb(255,(byte)(n>>16),(byte)(n>>8),(byte)n));
        if(tool==MarkTool.Arrow)
        {
            guides.Children.Add(new Line{X1=mark.Start.X,Y1=mark.Start.Y,X2=mark.End.X,Y2=mark.End.Y,Stroke=brush,StrokeThickness=Stroke});
            var head=new Polygon{Fill=brush};foreach(var p in AnnotationGeometry.ArrowHead(mark))head.Points.Add(new Windows.Foundation.Point(p.X,p.Y));guides.Children.Add(head);
        }
        else {var rect=new Rectangle{Width=Math.Abs(end.X-start.X),Height=Math.Abs(end.Y-start.Y),Stroke=brush,StrokeThickness=Stroke};Canvas.SetLeft(rect,Math.Min(start.X,end.X));Canvas.SetTop(rect,Math.Min(start.Y,end.Y));guides.Children.Add(rect);}
    }
    private async Task RefreshAsync()
    {
        if(closed)return;
        int current=++revision;var marks=history.Marks.ToArray();undo.IsEnabled=history.CanUndo;redo.IsEnabled=history.CanRedo;
        try{var frame=await Task.Run(()=>AnnotationRenderer.Render(source,marks));if(!closed&&current==revision)SetImage(frame);}catch{if(!closed)status.Text=T("Не удалось обновить разметку.");}
    }
    private async Task<byte[]> ExportAsync(bool jpeg=false){var marks=history.Marks.ToArray();var frame=await Task.Run(()=>AnnotationRenderer.Render(source,marks));return jpeg?await DesktopCapture.Jpeg(frame):await DesktopCapture.Png(frame,BitmapAlphaMode.Straight);}
    private async Task CopyAsync()
    {
        if(busy||dialog||closed)return;busy=true;
        try{byte[] png=await ExportAsync();if(!closed){completion.TrySetResult(png);Close();}}
        catch{if(!closed)status.Text=T("Не удалось подготовить снимок.");}finally{busy=false;}
    }
    private async Task SaveAsync()
    {
        if(busy||dialog||closed)return;busy=true;
        try
        {
            var picker=new FileSavePicker{SuggestedFileName="ClipShot-"+DateTime.Now.ToString("yyyy-MM-dd-HHmmss"),DefaultFileExtension=".png"};picker.FileTypeChoices.Add("PNG",new List<string>{".png"});picker.FileTypeChoices.Add("JPEG",new List<string>{".jpg",".jpeg"});WinRT.Interop.InitializeWithWindow.Initialize(picker,WinRT.Interop.WindowNative.GetWindowHandle(this));
            var file=await picker.PickSaveFileAsync();if(file is not null&&!closed){await FileIO.WriteBytesAsync(file,await ExportAsync(DesktopCapture.IsJpeg(file.Path)));status.Text=T("Снимок сохранён.");}
        }
        catch{if(!closed)status.Text=T("Не удалось сохранить снимок.");}finally{busy=false;}
    }
    private async void KeyDown(object sender,KeyRoutedEventArgs e)
    {
        if(dialog)return;bool ctrl=(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(VirtualKey.Control)&Windows.UI.Core.CoreVirtualKeyStates.Down)!=0;
        if(e.Key==VirtualKey.Escape){if(dragging)CancelDrag();else Close();e.Handled=true;}
        else if(e.Key==VirtualKey.Enter){await CopyAsync();e.Handled=true;}
        else if(ctrl&&e.Key==VirtualKey.S){await SaveAsync();e.Handled=true;}
        else if(ctrl&&e.Key==VirtualKey.Z){if(!busy){if(Shift())history.Redo();else history.Undo();await RefreshAsync();}e.Handled=true;}
        else if(!ctrl&&e.Key is VirtualKey.A or VirtualKey.R or VirtualKey.T){tool=e.Key==VirtualKey.A?MarkTool.Arrow:e.Key==VirtualKey.R?MarkTool.Rectangle:MarkTool.Text;foreach(var pair in tools)pair.Value.IsChecked=pair.Key==tool;Hint();e.Handled=true;}
    }
    private void Hint()=>status.Text=T("Протяните стрелку или рамку · Shift — выравнивание · Ctrl+Z — отменить · Enter — копировать и закрыть");
}
