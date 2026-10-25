using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Microsoft.UI.Windowing;
namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly Popup sectionPopup=new(){IsLightDismissEnabled=false,ShouldConstrainToRootBounds=false};
    private readonly StackPanel sectionRows=new();
    private Border? sectionBorder;
    private string? activeSection="Сегодня";
    private bool sectionInitialized,buildingSection;
    private string? animatedSection;
    private bool SideSections=>store.Get("sectionlayout","side")!="inline"&&Search.Text.Trim().Length==0;
    private StackPanel ClipRows=>buildingSection?sectionRows:Groups;
    private void RenderSideSection(List<ClipGroup> groups)
    {
        if(!SideSections){sectionPopup.IsOpen=false;return;}
        if(!sectionInitialized){activeSection=groups.FirstOrDefault(g=>g.Title=="Сегодня"&&g.Clips.Count>0)?.Title??groups.FirstOrDefault(g=>g.Clips.Count>0)?.Title;sectionInitialized=activeSection is not null;}
        var group=groups.FirstOrDefault(g=>g.Title==activeSection&&g.Clips.Count>0);
        if(group is null){sectionPopup.IsOpen=false;return;}
        sectionRows.Children.Clear();buildingSection=true;
        try{AddGroup(group.Title,group.Clips,true);}finally{buildingSection=false;}
        sectionBorder??=new Border{Child=new ScrollViewer{Content=sectionRows,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,VerticalScrollBarVisibility=ScrollBarVisibility.Auto},Background=ClipBrush("ClipWindowBrush"),BorderBrush=ClipBrush("ClipSeparatorBrush"),BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(4),RequestedTheme=Root.RequestedTheme};
        sectionBorder.RequestedTheme=Root.RequestedTheme;sectionBorder.Background=ClipBrush("ClipWindowBrush");
        sectionPopup.Child=sectionBorder;PositionSection();
    }
    private void PositionSection()
    {
        if(exiting||!SideSections||sectionBorder is null||activeSection is null||!AppWindow.IsVisible||Root.XamlRoot is null){sectionPopup.IsOpen=false;return;}
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        var origin=new Native.Point();Native.ClientToScreen(hwnd,ref origin);
        var bounds=SidePanelPlacement.Choose(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height,area.X,area.Y,area.Width,area.Height,(int)(360*scale),(int)(Math.Min(560,Math.Max(160,sectionRows.Children.Count*62))*scale),origin.Y+(int)(126*scale),(int)(8*scale));
        sectionBorder.Width=bounds.Width/scale;sectionBorder.Height=bounds.Height/scale;sectionPopup.XamlRoot=Root.XamlRoot;
        sectionPopup.HorizontalOffset=(bounds.X-origin.X)/scale;sectionPopup.VerticalOffset=(bounds.Y-origin.Y)/scale;sectionPopup.IsOpen=true;
        if(animatedSection!=activeSection)
        {
            animatedSection=activeSection;sectionRows.RenderTransform=new TranslateTransform();
            var storyboard=new Storyboard();var slide=new DoubleAnimation{From=bounds.OnRight?-16:16,To=0,Duration=new Duration(TimeSpan.FromMilliseconds(180)),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut},EnableDependentAnimation=true};
            Storyboard.SetTarget(slide,sectionRows);Storyboard.SetTargetProperty(slide,"(UIElement.RenderTransform).(TranslateTransform.X)");storyboard.Children.Add(slide);storyboard.Begin();
        }
    }
    private void ToggleSection(string title)
    {
        if(SideSections){activeSection=activeSection==title?null:title;sectionInitialized=true;Render();Search.Focus(FocusState.Programmatic);}
        else{expanded[title]=!expanded.GetValueOrDefault(title);Render();}
    }
}
