using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipShot;
public sealed partial class MainWindow
{
    private ContentDialog CreateHelpDialog()=>new()
    {
        Title="Справка",Content=new TextBlock{Text="Я верю в тебя, ты разберёшься сам.",TextWrapping=TextWrapping.Wrap,MaxWidth=340},
        CloseButtonText="Понятно",DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme
    };
    private async void HelpClick(object sender,RoutedEventArgs e)
    {
        if(dialogOpen||exiting)return;
        preview.IsOpen=false;hoverTimer.Stop();dialogOpen=true;
        try{await CreateHelpDialog().ShowAsync();}finally{dialogOpen=false;}
    }
}
