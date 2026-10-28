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
    private int sectionHeadingIndex;
    private readonly DispatcherTimer sectionFocusTimer=new(){Interval=TimeSpan.FromMilliseconds(250)};
    private bool sectionFocusTimerReady;
    // Состояние навигации относится к одному показу панели, а не к данным.
    // Временное скрытие HWND для захвата сюда не направляется.
    private void ResetNavigation()
    {
        foreach(var title in expanded.Keys.ToArray())expanded[title]=false;
        expandedTemplateFolders.Clear();
        activeSection=null;sectionInitialized=true;animatedSection=null;
        sectionPopup.IsOpen=false;preview.IsOpen=false;hovered=null;
        hoverTimer.Stop();leaveTimer.Stop();
        preserveExpandedDragPosition=false;preserveCollapsedPosition=false;
        compactGroupCount=-1;
    }
    private void CollapseSection()
    {
        if(!SideSections)return;
        activeSection=null;sectionInitialized=true;sectionPopup.IsOpen=false;preview.IsOpen=false;hoverTimer.Stop();hovered=null;
    }
    private void DismissSectionForExternalFocus(bool external)
    {
        if(!external)return;
        if(DismissSettingsForExternalFocus()||!CanDismissPanel)return;
        BeginPanelFade();
    }
    private bool OwnForeground()
    {
        var foreground=Native.GetForegroundWindow();if(foreground==0)return false;
        Native.GetWindowThreadProcessId(foreground,out uint process);return process==(uint)Environment.ProcessId;
    }
    private bool SideSections=>store.Get("sectionlayout","side")!="inline"&&Search.Text.Trim().Length==0;
    private StackPanel ClipRows=>buildingSection?sectionRows:buildingTemplates?TemplateGroups:Groups;
    private void RenderSideSection(List<ClipGroup> groups)
    {
        if(!SideSections){sectionPopup.IsOpen=false;return;}
        sectionHeadingIndex=groups.Where(g=>g.Clips.Count>0).TakeWhile(g=>g.Title!=activeSection).Count();
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
        if(exiting)return;
        if(InlineLayout){sectionPopup.IsOpen=false;QueueInlinePanelFit();return;}
        if(exiting||windowResizing||dialogOpen||panelFading||!SideSections||sectionBorder is null||activeSection is null||!AppWindow.IsVisible||Root.XamlRoot is null){sectionPopup.IsOpen=false;return;}
        var area=DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea;double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        var origin=new Native.Point();Native.ClientToScreen(hwnd,ref origin);
        double headingY=sectionHeadings.TryGetValue(activeSection,out var heading)?heading.TransformToVisual(Root).TransformPoint(new Windows.Foundation.Point()).Y:sectionHeadingIndex*41;
        var horizontal=SidePanelPlacement.Choose(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height,area.X,area.Y,area.Width,area.Height,(int)(360*scale),1,origin.Y+(int)(headingY*scale),(int)(8*scale));
        sectionPopup.XamlRoot=Root.XamlRoot;
        double borderWidth=sectionBorder.BorderThickness.Left+sectionBorder.BorderThickness.Right;
        sectionRows.Measure(new Windows.Foundation.Size(Math.Max(1,horizontal.Width/scale-borderWidth),double.PositiveInfinity));
        double naturalHeight=sectionRows.DesiredSize.Height;
        sectionRows.InvalidateMeasure();
        if(!double.IsFinite(naturalHeight)||naturalHeight<=0)naturalHeight=sectionRows.Children.Count*62d;
        double contentHeight=Math.Max(160,(naturalHeight+sectionBorder.BorderThickness.Top+sectionBorder.BorderThickness.Bottom))*scale;
        int desiredHeight=SidePanelPlacement.HeightForContent(contentHeight,area.Height,560*scale);
        var bounds=SidePanelPlacement.Choose(AppWindow.Position.X,AppWindow.Position.Y,AppWindow.Size.Width,AppWindow.Size.Height,area.X,area.Y,area.Width,area.Height,(int)(360*scale),desiredHeight,origin.Y+(int)(headingY*scale),(int)(8*scale));
        sectionBorder.Width=bounds.Width/scale;sectionBorder.Height=bounds.Height/scale;sectionPopup.XamlRoot=Root.XamlRoot;
        sectionPopup.HorizontalOffset=(bounds.X-origin.X)/scale;sectionPopup.VerticalOffset=(bounds.Y-origin.Y)/scale;sectionPopup.IsOpen=true;
        WatchPanelFocus();
        if(animatedSection!=activeSection)
        {
            animatedSection=activeSection;
            var storyboard=new Storyboard();var fade=new DoubleAnimation{From=.82,To=1,Duration=new Duration(TimeSpan.FromMilliseconds(150)),EasingFunction=new CubicEase{EasingMode=EasingMode.EaseOut}};
            Storyboard.SetTarget(fade,sectionRows);Storyboard.SetTargetProperty(fade,"Opacity");storyboard.Children.Add(fade);storyboard.Begin();
        }
    }
    private void ToggleSection(string title)
    {
        // Heading activation is the only pointer action that changes a section.
        if(SideSections){activeSection=activeSection==title?null:title;sectionInitialized=true;Render();FocusPanel();}
        else{expanded[title]=!expanded.GetValueOrDefault(title);Render();}
    }
}
