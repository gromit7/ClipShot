using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ClipShot;
public sealed partial class MainWindow
{
    private AppTheme activeTheme=AppThemes.Resolve("Dark");
    private SolidColorBrush primaryBrush=new(Microsoft.UI.Colors.White),selectedTextBrush=new(Microsoft.UI.Colors.White),titleBrush=new(Microsoft.UI.Colors.White);
    private void ApplyTheme()
    {
        string id=store.Get("theme","Default");
        bool systemDark=new Windows.UI.ViewManagement.UISettings().GetColorValue(Windows.UI.ViewManagement.UIColorType.Background).R<128;
        activeTheme=AppThemes.Resolve(id,systemDark);
        Root.RequestedTheme=activeTheme.Dark?ElementTheme.Dark:ElementTheme.Light;
        primaryBrush.Color=ThemeColor(activeTheme.Primary);selectedTextBrush.Color=ThemeColor(activeTheme.SelectedText);titleBrush.Color=ThemeColor(activeTheme.Title);
        foreach(ResourceDictionary dictionary in Root.Resources.ThemeDictionaries.Values)
        {
            void Set(string key,string color)=>((SolidColorBrush)dictionary[key]).Color=ThemeColor(color);
            Set("ClipWindowBrush",activeTheme.Window);Set("ClipHeaderBrush",activeTheme.Header);Set("ClipSearchBrush",activeTheme.Search);
            Set("ClipSeparatorBrush",activeTheme.Separator);Set("ClipSelectionBrush",activeTheme.Selection);Set("ClipSecondaryBrush",activeTheme.Secondary);Set("ClipIndicatorBrush",activeTheme.SelectedText);
        }
        SystemBackdrop=activeTheme.Acrylic?new DesktopAcrylicBackdrop():null;
        Search.Foreground=primaryBrush;Search.FontFamily=new FontFamily(activeTheme.Font);Search.CornerRadius=new CornerRadius(activeTheme.Corner);
        Search.BorderThickness=new Thickness(0);
        TitleText.FontFamily=new FontFamily(activeTheme.Font);TitleText.Foreground=titleBrush;SettingsButton.Foreground=titleBrush;HelpButton.Foreground=titleBrush;
        PaintText(FooterSurface,primaryBrush);
        StyleTitleBar();
        ApplyCatTheme();
    }
    private void StyleTitleBar()
    {
        if(exiting)return;
        Root.Background=activeTheme.Gradient?new LinearGradientBrush{StartPoint=new(0,0),EndPoint=new(1,1),GradientStops=new(){new(){Color=ThemeColor("#1E293B"),Offset=0},new(){Color=ThemeColor("#35253D"),Offset=.55},new(){Color=ThemeColor("#19313D"),Offset=1}}}:ClipBrush("ClipWindowBrush");
        HeaderSurface.Background=ClipBrush("ClipHeaderBrush");
        FooterSurface.Background=new SolidColorBrush(ThemeColor(activeTheme.Acrylic?"#B0202125":activeTheme.Window));
        var bg=ThemeColor(activeTheme.Header);var fg=ThemeColor(activeTheme.Title);
        var bar=AppWindow.TitleBar;
        CaptionSpace.Width=new GridLength(Math.Max(96,bar.RightInset)+8);
        bar.BackgroundColor=bg;bar.ForegroundColor=fg;bar.ButtonBackgroundColor=bg;bar.ButtonForegroundColor=fg;
        bar.InactiveBackgroundColor=bg;bar.ButtonInactiveBackgroundColor=bg;bar.ButtonInactiveForegroundColor=fg;
    }
    private void PaintText(DependencyObject root,Brush brush)
    {
        if(root is TextBlock text){if(text.ReadLocalValue(TextBlock.ForegroundProperty)==DependencyProperty.UnsetValue||ReferenceEquals(text.Foreground,primaryBrush))text.Foreground=brush;text.FontFamily=new FontFamily(activeTheme.Font);}
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)PaintText(VisualTreeHelper.GetChild(root,i),brush);
    }
    private void StyleRow(DependencyObject root,bool selectedRow)
    {
        if(root is TextBlock text){text.Tag??=text.ReadLocalValue(TextBlock.ForegroundProperty)==DependencyProperty.UnsetValue?primaryBrush:text.Foreground;text.Foreground=selectedRow?selectedTextBrush:(Brush)text.Tag;text.FontFamily=new FontFamily(activeTheme.Font);}
        if(root is FontIcon icon){icon.Tag??=icon.Foreground;icon.Foreground=selectedRow?selectedTextBrush:(Brush)icon.Tag;}
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)StyleRow(VisualTreeHelper.GetChild(root,i),selectedRow);
    }
    private static Color ThemeColor(string value)
    {
        string hex=value.TrimStart('#');uint n=Convert.ToUInt32(hex,16);
        return Color.FromArgb(hex.Length==8?(byte)(n>>24):(byte)255,(byte)(n>>16),(byte)(n>>8),(byte)n);
    }
}
