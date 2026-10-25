using Microsoft.UI.Xaml.Controls;
namespace ClipShot;
public sealed partial class MainWindow
{
    private ContentDialog? activeDialog;
    private async Task<ContentDialogResult> ShowDialogAsync(ContentDialog dialog)
    {
        activeDialog=dialog;dialogOpen=true;sectionPopup.IsOpen=false;
        try{return await dialog.ShowAsync();}
        finally{if(ReferenceEquals(activeDialog,dialog)){activeDialog=null;dialogOpen=false;}}
    }
}
