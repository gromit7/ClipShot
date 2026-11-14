using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
namespace ClipShot;
public sealed partial class MainWindow
{
    private void CheckThemeSurfaces()
    {
        static double Contrast(Windows.UI.Color a,Windows.UI.Color b)
        {
            double L(Windows.UI.Color c){double V(byte n){double x=n/255d;return x<=.04045?x/12.92:Math.Pow((x+.055)/1.055,2.4);}return .2126*V(c.R)+.7152*V(c.G)+.0722*V(c.B);}
            double x=L(a),y=L(b);return(Math.Max(x,y)+.05)/(Math.Min(x,y)+.05);
        }
        int checkedText=0;
        void CheckHeading(DependencyObject node)
        {
            Brush? foreground=node switch{TextBlock text=>text.Foreground,FontIcon icon=>icon.Foreground,_=>null};
            if(foreground is SolidColorBrush brush)
            {
                if(Contrast(brush.Color,ThemeColor(activeTheme.Header))<4.5)throw new InvalidOperationException("Unreadable rendered heading: "+activeTheme.Id);
                checkedText++;
            }
            foreach(var child in LogicalChildren(node))CheckHeading(child);
        }
        foreach(var heading in sectionHeadings.Values)CheckHeading(heading);
        if(checkedText<3)throw new InvalidOperationException("Theme test did not inspect actual heading text/icons/counts");
        void CheckLink(DependencyObject node)
        {
            Brush? foreground=node switch{TextBlock text=>text.Foreground,ContentPresenter presenter=>presenter.Foreground,_=>null};
            if(foreground is SolidColorBrush brush&&Contrast(brush.Color,ThemeColor(activeTheme.Window))<4.5)
                throw new InvalidOperationException("Unreadable rendered update link: "+activeTheme.Id);
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)CheckLink(VisualTreeHelper.GetChild(node,i));
        }
        CheckLink(ManualUpdateLink);
        if(Root.ContextFlyout is not MenuFlyout context||context.Items.OfType<MenuFlyoutSubItem>().SingleOrDefault(x=>Equals(x.Tag,"themes")) is not {} menu||menu.Items.Count!=AppThemes.All.Count)
            throw new InvalidOperationException("Context menu is missing theme choices");
        if(menu.Text!=T("Тема"))throw new InvalidOperationException("Theme menu label is corrupted");
        var selected=menu.Items.OfType<ToggleMenuFlyoutItem>().Where(item=>item.IsChecked).ToArray();
        if(selected.Length!=1||!Equals(selected[0].Tag,activeTheme.Id)||AppThemes.Resolve(store.Get("theme","Default"),activeTheme.Dark).Id!=activeTheme.Id)
            throw new InvalidOperationException("Theme selection and persisted menu state disagree");
        if(Search.CornerRadius.TopLeft!=activeTheme.Corner||Search.FontFamily.Source!=activeTheme.Font)
            throw new InvalidOperationException("Theme geometry/font was not applied");
        if(activeTheme.Retro&&sectionHeadings.Values.OfType<Button>().Any(button=>button.Template is null))throw new InvalidOperationException("Retro button template missing");
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Assets","Shutter.wav"));
        if(bytes.Length<44||System.Text.Encoding.ASCII.GetString(bytes,0,4)!="RIFF"||System.Text.Encoding.ASCII.GetString(bytes,8,4)!="WAVE"||bytes.Length>40000)
            throw new InvalidOperationException("Short shutter audio is missing or invalid");
    }
    private async Task CheckAllThemes(Action<bool,string> check)
    {
        string original=activeTheme.Id;
        try
        {
            foreach(var theme in AppThemes.All)
            {
                SelectTheme(theme.Id);await Task.Delay(100);CheckThemeSurfaces();
                check(true,"rendered heading contrast, fonts, geometry and checked context choice: "+theme.Id);
            }
        }
        finally{SelectTheme(original);}
    }
    private async Task RunThemePreviewCycle()
    {
        string start=Path.Combine(dataFolder,"start-theme-preview.flag");
        while(!File.Exists(start)&&!exiting)await Task.Delay(200);
        foreach(var theme in AppThemes.All)
        {
            if(exiting)return;
            SelectTheme(theme.Id);Title="ClipShot PREVIEW:"+theme.Id;
            await Task.Delay(3500);
        }
        if(Environment.GetCommandLineArgs().Contains("--notice-preview"))
            installationNotice=new MicrophoneNotice("Обновлений нет","Текущая версия: 0.7.14",MicrophoneState.Enabled,preview:true,appNotice:true);
    }
}
