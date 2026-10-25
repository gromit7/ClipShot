using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Windows.ApplicationModel.DataTransfer;
using Windows.Storage;
using Windows.Storage.Pickers;
using Windows.Storage.Streams;

namespace ClipShot;
public sealed partial class MainWindow
{
    private ScreenshotEditor? editor;
    private static bool IsButtonSource(object source)
    {
        for(var current=source as DependencyObject;current is not null;current=VisualTreeHelper.GetParent(current))if(current is ButtonBase)return true;
        return false;
    }
    private async Task EditImage(Clip clip)
    {
        if(dialogOpen||exiting||clip.Image is null)return;
        dialogOpen=true;
        try
        {
            editor=await ScreenshotEditor.CreateAsync(clip.Image,words,Root.RequestedTheme);
            if(exiting){editor.Close();return;}
            Hide();byte[]? png=await editor.ShowAsync();editor=null;
            if(exiting)return;
            if(png is null){ShowNearCursor();return;}
            if(!isolated)
            {
                using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}stream.Seek(0);
                var data=new DataPackage{RequestedOperation=DataPackageOperation.Copy};data.SetBitmap(RandomAccessStreamReference.CreateFromStream(stream));Clipboard.SetContent(data);Clipboard.Flush();ownSequence=Native.GetClipboardSequenceNumber();
            }
            if(!exiting){store.Add("image","",image:png);Render();}
        }
        catch{editor?.Close();editor=null;if(!exiting){ShowNearCursor();Status.Text=T("Не удалось открыть редактор снимка.");}}
        finally{editor=null;dialogOpen=false;}
    }
    private async Task SaveImage(Clip clip)
    {
        if(dialogOpen||exiting||clip.Image is null||isolated)return;
        dialogOpen=true;
        try
        {
            var picker=new FileSavePicker{SuggestedFileName="ClipShot-"+DateTime.Now.ToString("yyyy-MM-dd-HHmmss"),DefaultFileExtension=".png"};picker.FileTypeChoices.Add("PNG",new List<string>{".png"});WinRT.Interop.InitializeWithWindow.Initialize(picker,hwnd);
            var file=await picker.PickSaveFileAsync();if(file is not null&&!exiting)await FileIO.WriteBytesAsync(file,clip.Image);
        }
        catch{if(!exiting)Status.Text=T("Не удалось сохранить снимок.");}
        finally{dialogOpen=false;}
    }
}
