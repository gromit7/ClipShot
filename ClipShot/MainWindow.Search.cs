using Microsoft.UI.Xaml;

namespace ClipShot;
public sealed partial class MainWindow
{
    // Активация панели никогда не означает намерение вводить текст.
    // Фокус TextBox разрешён только явной командой лупы.
    private void FocusPanel()=>SearchToggle.Focus(FocusState.Programmatic);

    private void OpenSearchClick(object sender,RoutedEventArgs e)
    {

        Search.Visibility=Visibility.Visible;
        SearchClose.Visibility=Visibility.Visible;
        Search.Focus(FocusState.Programmatic);
    }

    private void CloseSearchClick(object sender,RoutedEventArgs e)=>CollapseSearch();

    private void CollapseSearch(bool focus=true)
    {
        Search.Text="";
        Search.Visibility=Visibility.Collapsed;
        SearchClose.Visibility=Visibility.Collapsed;
        if(focus)FocusPanel();
    }
}
