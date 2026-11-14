using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Markup;
using Microsoft.UI.Xaml.Media;
using System.Runtime.CompilerServices;

namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly ConditionalWeakTable<Button,OriginalButtonTemplate> originalButtonTemplates=new();
    private sealed record OriginalButtonTemplate(object LocalValue);
    private ControlTemplate? classicButtonTemplate,xpButtonTemplate;
    private readonly Dictionary<string,SolidColorBrush> themeResourceBrushes=new();
    private void SetThemeResources()
    {
        void Brush(string key,string color)
        {
            // Resource lookup can return a shared, read-only framework brush.
            // Keep ownership explicit so live palette changes mutate only our brushes.
            if(themeResourceBrushes.TryGetValue(key,out var existing))existing.Color=ThemeColor(color);
            else
            {
                var brush=new SolidColorBrush(ThemeColor(color));
                themeResourceBrushes.Add(key,brush);Root.Resources[key]=brush;
            }
        }
        foreach(string control in new[]{"Button","ComboBox","TextControl","MenuFlyoutItem","ToggleMenuFlyoutItem"})
        {
            Brush(control+"Foreground",activeTheme.Primary);Brush(control+"ForegroundDisabled",activeTheme.Secondary);
            Brush(control+"Background",activeTheme.Search);Brush(control+"BackgroundDisabled",activeTheme.Window);
            Brush(control+"BorderBrush",activeTheme.Separator);
            foreach(string state in new[]{"PointerOver","Pressed"})
            {
                Brush(control+"Background"+state,activeTheme.Selection);Brush(control+"Foreground"+state,activeTheme.SelectedText);
                Brush(control+"BorderBrush"+state,activeTheme.Primary);
            }
        }
        foreach(string state in new[]{"","PointerOver","Pressed"})Brush("HyperlinkButtonForeground"+state,activeTheme.Primary);
        Brush("HyperlinkButtonForegroundDisabled",activeTheme.Secondary);
        Brush("TextControlForegroundFocused",activeTheme.Primary);Brush("TextControlBackgroundFocused",activeTheme.Search);
        Root.Resources["ControlCornerRadius"]=new CornerRadius(activeTheme.Corner);
        Root.Resources["OverlayCornerRadius"]=new CornerRadius(activeTheme.Corner);
        WindowHandleMark.CornerRadius=new CornerRadius(activeTheme.Retro?0:2);WindowHandleMark.Opacity=.85;
        Root.BorderThickness=new Thickness(activeTheme.Retro?2:0);
        Root.BorderBrush=new SolidColorBrush(ThemeColor(activeTheme.Classic?"#808080":activeTheme.Id=="WindowsXP"?"#0054E3":activeTheme.Separator));
        uint preference=activeTheme.Corner==0?1u:activeTheme.Retro?3u:2u;
        Native.DwmSetWindowAttribute(hwnd,33,ref preference,4); // Optional on Win10; per-window only.
    }
    private ControlTemplate RetroButtonTemplate(bool xp)
    {
        string background=xp?"""
          <Grid.Background><LinearGradientBrush StartPoint="0,0" EndPoint="0,1"><GradientStop Color="#FFFFFF"/><GradientStop Color="#E1E7F2" Offset="1"/></LinearGradientBrush></Grid.Background>
        """:"";
        string template="""
        <ControlTemplate xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation" xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml" TargetType="Button">
          <Grid Background="{TemplateBinding Background}" CornerRadius="CORNER">
            BACKGROUND
            <VisualStateManager.VisualStateGroups><VisualStateGroup x:Name="CommonStates">
              <VisualState x:Name="Normal"/><VisualState x:Name="PointerOver"/>
              <VisualState x:Name="Pressed"><VisualState.Setters><Setter Target="TopEdge.BorderBrush" Value="#505050"/><Setter Target="BottomEdge.BorderBrush" Value="#FFFFFF"/><Setter Target="ContentShift.X" Value="1"/><Setter Target="ContentShift.Y" Value="1"/></VisualState.Setters></VisualState>
              <VisualState x:Name="Disabled"><VisualState.Setters><Setter Target="Presenter.Foreground" Value="#454545"/></VisualState.Setters></VisualState>
            </VisualStateGroup></VisualStateManager.VisualStateGroups>
            <Border x:Name="TopEdge" BorderBrush="TOP" BorderThickness="2,2,0,0" CornerRadius="CORNER" IsHitTestVisible="False"/>
            <Border x:Name="BottomEdge" BorderBrush="BOTTOM" BorderThickness="0,0,2,2" CornerRadius="CORNER" IsHitTestVisible="False"/>
            <ContentPresenter x:Name="Presenter" Content="{TemplateBinding Content}" ContentTemplate="{TemplateBinding ContentTemplate}" Foreground="{TemplateBinding Foreground}" HorizontalContentAlignment="{TemplateBinding HorizontalContentAlignment}" VerticalContentAlignment="{TemplateBinding VerticalContentAlignment}" Padding="{TemplateBinding Padding}" Margin="2"><ContentPresenter.RenderTransform><TranslateTransform x:Name="ContentShift"/></ContentPresenter.RenderTransform></ContentPresenter>
          </Grid>
        </ControlTemplate>
        """;
        // A template needs only one Background assignment.
        if(xp)template=template.Replace("Background=\"{TemplateBinding Background}\"","");
        return (ControlTemplate)XamlReader.Load(template.Replace("BACKGROUND",background).Replace("CORNER",xp?"3":"0").Replace("TOP",xp?"#739DDE":"#FFFFFF").Replace("BOTTOM",xp?"#2459A8":"#404040"));
    }
    private void ApplyThemedControls(DependencyObject node)
    {
        if(node is Button button)
        {
            var original=originalButtonTemplates.GetValue(button,b=>new(b.ReadLocalValue(Control.TemplateProperty)));
            if(activeTheme.Retro&&!Equals(button.Tag,"clip-image-action"))
                button.Template=activeTheme.Classic?classicButtonTemplate??=RetroButtonTemplate(false):xpButtonTemplate??=RetroButtonTemplate(true);
            else if(original.LocalValue==DependencyProperty.UnsetValue)button.ClearValue(Control.TemplateProperty);
            else button.SetValue(Control.TemplateProperty,original.LocalValue);
            button.CornerRadius=new CornerRadius(activeTheme.Corner);button.FontFamily=new FontFamily(activeTheme.Font);
        }
        foreach(var child in LogicalChildren(node))ApplyThemedControls(child);
    }
    private static IEnumerable<DependencyObject> LogicalChildren(DependencyObject node)
    {
        if(node is Panel panel)return panel.Children.Cast<DependencyObject>();
        if(node is Border{Child:{} borderChild})return [borderChild];
        if(node is ContentControl{Content:DependencyObject content})return [content];
        return [];
    }
    private Border ThemeSectionFrame(Button button)
    {
        if(activeTheme.Classic)return new Border{BorderBrush=new SolidColorBrush(Microsoft.UI.Colors.White),BorderThickness=new Thickness(1,1,0,0),Child=new Border{BorderBrush=new SolidColorBrush(ThemeColor("#404040")),BorderThickness=new Thickness(0,0,1,1),Child=button}};
        return new Border{Child=button,BorderBrush=ClipBrush("ClipSeparatorBrush"),BorderThickness=new Thickness(0,0,0,1)};
    }
    private void SelectTheme(string id)
    {
        if(exiting||!AppThemes.All.Any(theme=>theme.Id==id))return;
        store.Set("theme",id);ApplyTheme();Render();
    }
    private MenuFlyoutSubItem ThemeMenu()
    {
        var menu=new MenuFlyoutSubItem{Text=T("Тема"),Tag="themes"};
        foreach(var theme in AppThemes.All)
        {
            var item=new ToggleMenuFlyoutItem{Text=T(theme.Name),IsChecked=theme.Id==activeTheme.Id,Tag=theme.Id};
            item.Click+=(_,_)=>SelectTheme(theme.Id);menu.Items.Add(item);
        }
        return menu;
    }
    private Style ThemeFlyoutStyle()=>new(){TargetType=typeof(MenuFlyoutPresenter),Setters={new Setter(Control.BackgroundProperty,ClipBrush("ClipSearchBrush")),new Setter(Control.ForegroundProperty,primaryBrush),new Setter(Control.FontFamilyProperty,new FontFamily(activeTheme.Font)),new Setter(Control.CornerRadiusProperty,new CornerRadius(activeTheme.Corner))}};
    private void AppendTrayThemes(nint menu)
    {
        nint themes=Native.CreatePopupMenu();
        for(int i=0;i<AppThemes.All.Count;i++)Native.AppendMenu(themes,AppThemes.All[i].Id==activeTheme.Id?8u:0u,(nuint)(100+i),T(AppThemes.All[i].Name));
        Native.AppendMenu(menu,0x10,(nuint)themes,T("Тема")); // DestroyMenu(parent) owns this submenu.
    }
}
