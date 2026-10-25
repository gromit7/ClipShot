using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipShot;
internal sealed partial class ScreenshotEditor
{
    internal async Task<List<string>> CheckAsync(Func<Task>? capture=null)
    {
        if(!TestEnvironment.IsIsolated(Environment.GetCommandLineArgs()))throw new InvalidOperationException("Isolated test only.");
        var report=new List<string>();void Check(bool ok,string name){report.Add($"{(ok?"PASS":"FAIL")} {name}");}
        byte[] original=source.Pixels.ToArray();
        Check(tool==MarkTool.Arrow,"arrow is selected by default");
        Check(copy.Label==T("Копировать в буфер")&&toolbar.PrimaryCommands.Count==9&&toolbar.SecondaryCommands.Count==0,"all nine commands are on one row and copy explicitly names clipboard");
        start=new(20,30);end=new(150,30.5);dragging=dirty=true;await Task.Delay(180);
        var commandBounds=toolbar.PrimaryCommands.OfType<Microsoft.UI.Xaml.FrameworkElement>().Select(b=>new {Y=b.TransformToVisual(root).TransformPoint(new Windows.Foundation.Point()).Y,Width=b.ActualWidth}).ToArray();
        Check(commandBounds.All(b=>b.Width>0)&&commandBounds.Max(b=>b.Y)-commandBounds.Min(b=>b.Y)<1,"all editor commands occupy one visible horizontal row");
        Check(guides.Children.Count==2,"arrow preview is rendered on a composition frame");
        var arrow=CurrentMark();Check(Math.Abs(arrow.End.Y-arrow.Start.Y)<.001,"near horizontal drag is aligned while its tip remains at the pointer");Check(arrow.End==end,"arrow target is never moved by automatic alignment");
        Check(tools[MarkTool.Blur].Visibility==Microsoft.UI.Xaml.Visibility.Visible&&tools[MarkTool.Redact].Visibility==Microsoft.UI.Xaml.Visibility.Visible,"pixelate and hide tools are on the visible toolbar");CommitMark(arrow);CancelDrag();
        CommitMark(new(MarkTool.Rectangle,new(40,50),new(170,120),Width:Stroke));await RefreshAsync();
        Check(history.Marks.Count==2&&undo.IsEnabled,"arrow and rectangle are committed");
        if(capture is not null){await capture();Check(!closed&&history.Marks.Count==2,"a new capture preserves the open editor and its unsaved annotations");}
        history.Undo();history.Undo();await RefreshAsync();Check(history.Marks.Count==0&&redo.IsEnabled,"undo preserves original image and offers redo");
        history.Redo();history.Redo();await RefreshAsync();
        var png=await ExportAsync();using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}stream.Seek(0);
        var decoder=await BitmapDecoder.CreateAsync(stream);Check(decoder.PixelWidth==source.Width&&decoder.PixelHeight==source.Height,"export preserves original pixel dimensions");
        var decoded=await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8,BitmapAlphaMode.Straight,new BitmapTransform(),ExifOrientationMode.IgnoreExifOrientation,ColorManagementMode.DoNotColorManage);
        var expected=AnnotationRenderer.Render(source,history.Marks);
        Check(decoded.DetachPixelData().SequenceEqual(expected.Pixels),"exported PNG contains exact annotated pixels");
        Check(source.Pixels.SequenceEqual(original),"original screenshot is unchanged");
        await CopyAsync();Check(closed&&completion.Task.IsCompletedSuccessfully&&completion.Task.Result is not null,"copy action returns edited PNG and closes editor");
        return report;
    }
}
