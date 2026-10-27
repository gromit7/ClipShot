using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Windowing;
using Windows.Graphics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private bool buildingTemplates,compactResizing,preserveCollapsedPosition,preserveExpandedDragPosition;
    private int compactGroupCount=-1,openMenus;
    private readonly Dictionary<string,FrameworkElement> sectionHeadings=[];
    private readonly DispatcherTimer panelFadeTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private bool panelFading,panelFadeReady;
    private long panelFadeStarted;
    private nint panelOriginalStyle;
    private bool inlineSizingReady,inlineFitQueued,inlineFitting;
    private bool InlineLayout=>store.Get("sectionlayout","side")=="inline";
    private bool AllSectionsCollapsed=>Search.Visibility==Visibility.Collapsed&&Search.Text.Trim().Length==0&&
        (InlineLayout?!sectionHeadings.Keys.Any(title=>expanded.GetValueOrDefault(title)):sectionInitialized&&activeSection is null);
    private void DockCollapsedPanel()
    {
        if(exiting||!AppWindow.IsVisible||dialogOpen||windowResizing||takingScreenshot||templateDragInProgress||editor is not null||preserveCollapsedPosition||!AllSectionsCollapsed)return;
        var area=DisplayArea.Primary.WorkArea;
        if(area.Width<=0||area.Height<=0)return;
        int width=Math.Min(AppWindow.Size.Width,area.Width),height=Math.Min(AppWindow.Size.Height,area.Height);
        int x=area.X+area.Width-width,y=area.Y+area.Height-height;
        if(AppWindow.Position.X==x&&AppWindow.Position.Y==y&&AppWindow.Size.Width==width&&AppWindow.Size.Height==height)return;
        compactResizing=true;
        try{AppWindow.MoveAndResize(new RectInt32(x,y,width,height));}
        finally{compactResizing=false;}
    }
    private bool AlwaysOnTop=>store.Get("alwaysontop","0")=="1";
    private void ApplyAlwaysOnTop()
    {
        // Сначала снимаем анимационный WS_EX_LAYERED: восстановление старого
        // extended style после смены presenter могло бы отменить TOPMOST.
        CancelPanelFade();
        bool top=AlwaysOnTop;
        if(AppWindow.Presenter is OverlappedPresenter presenter)presenter.IsAlwaysOnTop=top;
        // Keep the actual HWND in the same Z-order group as the saved preference.
        // Presenter state alone can remain true while a native style update has
        // already removed WS_EX_TOPMOST. Do not activate or move the window.
        Native.SetWindowPos(hwnd,top?(nint)(-1):(nint)(-2),0,0,0,0,0x13);
    }
    private void AlwaysOnTopClick(object sender,RoutedEventArgs e)
    {
        if(exiting||sender is not ToggleMenuFlyoutItem item)return;
        store.Set("alwaysontop",item.IsChecked?"1":"0");ApplyAlwaysOnTop();
    }
    private void ToggleAlwaysOnTop()
    {
        if(exiting)return;
        store.Set("alwaysontop",AlwaysOnTop?"0":"1");ApplyAlwaysOnTop();ConfigureWindowContextMenu();
    }
    private void FitCompactPanel(List<ClipGroup> groups)
    {
        // Сворачивание завершает ручное размещение раскрытой панели и возвращает
        // обычную привязку. Сам auto-fit не должен отменять законченный drag.
        if(AllSectionsCollapsed)preserveExpandedDragPosition=false;
        EnsureInlineSizing();
        if(InlineLayout){compactGroupCount=-1;QueueInlinePanelFit();return;}
        int count=groups.Count(g=>g.Clips.Count>0);
        if(!SideSections||compactGroupCount==count||dialogOpen||windowResizing)return;
        compactGroupCount=count;
        if(store.Get("panelautofit","1")!="1")return;
        double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        int height=(int)(Math.Max(100,count*41+82)*scale);
        compactResizing=true;
        try{AppWindow.Resize(new SizeInt32(Math.Max(AppWindow.Size.Width,(int)(220*scale)),height));}
        finally{compactResizing=false;}
        DispatcherQueue.TryEnqueue(PositionSection);
    }
    private void EnsureInlineSizing()
    {
        if(inlineSizingReady)return;inlineSizingReady=true;
        Root.Loaded+=(_,_)=>QueueInlinePanelFit();
        Root.SizeChanged+=(_,_)=>QueueInlinePanelFit();
        Groups.SizeChanged+=(_,_)=>QueueInlinePanelFit();
        FooterSurface.SizeChanged+=(_,_)=>QueueInlinePanelFit();
    }
    private void QueueInlinePanelFit()
    {
        // Отложенный layout может прийти уже после Quit/store.Dispose.
        // InlineLayout читает настройки, поэтому exiting проверяется первым.
        if(exiting||!InlineLayout||inlineFitQueued||inlineFitting||dialogOpen||windowResizing||compactResizing||takingScreenshot||editor is not null||!AppWindow.IsVisible)return;
        inlineFitQueued=true;
        if(!DispatcherQueue.TryEnqueue(Microsoft.UI.Dispatching.DispatcherQueuePriority.Low,()=>{inlineFitQueued=false;FitInlinePanel();}))inlineFitQueued=false;
    }
    private void FitInlinePanel()
    {
        if(exiting||!InlineLayout||inlineFitting||dialogOpen||windowResizing||takingScreenshot||editor is not null||!AppWindow.IsVisible||Root.XamlRoot is null)return;
        inlineFitting=true;
        try
        {
            Root.UpdateLayout();
            bool manualPosition=preserveExpandedDragPosition||(preserveCollapsedPosition&&AllSectionsCollapsed);
            var area=manualPosition?DisplayArea.GetFromWindowId(AppWindow.Id,DisplayAreaFallback.Nearest).WorkArea:DisplayArea.Primary.WorkArea;
            if(area.Width<=0||area.Height<=0)return;
            double scale=Root.XamlRoot.RasterizationScale;
            if(!double.IsFinite(scale)||scale<=0)return;
            int chromeHeight=(int)Math.Max(0,Math.Round(AppWindow.Size.Height-Root.ActualHeight*scale));
            int chromeWidth=(int)Math.Max(0,Math.Round(AppWindow.Size.Width-Root.ActualWidth*scale));
            int width=Math.Min(area.Width,Math.Max(AppWindow.Size.Width,(int)Math.Ceiling(Root.MinWidth*scale)+chromeWidth));
            // В обычном layout звёздная строка ограничивает viewport. Для уменьшения
            // окна тоже нужен естественный размер содержимого, без этой границы.
            Root.Measure(new Windows.Foundation.Size(Math.Max(1,(width-chromeWidth)/scale),double.PositiveInfinity));
            double contentHeight=Root.DesiredSize.Height;
            Root.InvalidateMeasure();
            if(!double.IsFinite(contentHeight))return;
            var bounds=InlinePanelPlacement.Choose(width,AppWindow.Position.Y,area.X,area.Y,area.Width,area.Height,contentHeight,scale,chromeHeight);
            if(manualPosition)
                bounds=bounds with{X=Math.Clamp(AppWindow.Position.X,area.X,area.X+area.Width-bounds.Width)};
            if(AppWindow.Position.X==bounds.X&&AppWindow.Position.Y==bounds.Y&&AppWindow.Size.Width==bounds.Width&&AppWindow.Size.Height==bounds.Height)return;
            compactResizing=true;
            try{AppWindow.MoveAndResize(new RectInt32(bounds.X,bounds.Y,bounds.Width,bounds.Height));}
            finally{compactResizing=false;}
        }
        finally{inlineFitting=false;DockCollapsedPanel();}
    }
    private void TrackMenu(MenuFlyout menu)
    {
        menu.Opened+=(_,_)=>{openMenus++;CancelPanelFade();};
        menu.Closed+=(_,_)=>openMenus=Math.Max(0,openMenus-1);
    }
    private bool CanDismissPanel=>!exiting&&!AlwaysOnTop&&AppWindow.IsVisible&&!dialogOpen&&!takingScreenshot&&!templateDragInProgress&&!windowResizing&&editor is null&&openMenus==0;
    private bool ExternalForeground()
    {
        nint foreground=Native.GetForegroundWindow();
        if(foreground==0)return false;
        Native.GetWindowThreadProcessId(foreground,out uint process);
        return process!=0&&process!=(uint)Environment.ProcessId;
    }
    private void WatchPanelFocus()
    {
        if(isolated||exiting||!AppWindow.IsVisible)return;
        if(!sectionFocusTimerReady)
        {
            sectionFocusTimer.Tick+=(_,_)=>
            {
                if(exiting||!AppWindow.IsVisible){sectionFocusTimer.Stop();return;}
                DismissSectionForExternalFocus(ExternalForeground());
            };
            sectionFocusTimerReady=true;
        }
        sectionFocusTimer.Start();
    }
    private void BeginPanelFade()
    {
        if(panelFading||!CanDismissPanel)return;
        CollapseSection();preview.IsOpen=false;hoverTimer.Stop();leaveTimer.Stop();
        panelOriginalStyle=Native.GetWindowLongPtr(hwnd,-20);
        Native.SetWindowLongPtr(hwnd,-20,(nint)((long)panelOriginalStyle|0x80000L));
        Native.SetLayeredWindowAttributes(hwnd,0,255,2);
        panelFading=true;panelFadeStarted=Environment.TickCount64;
        if(!panelFadeReady)
        {
            panelFadeTimer.Tick+=(_,_)=>
            {
                if(!CanDismissPanel||(!isolated&&!ExternalForeground())){CancelPanelFade();return;}
                double progress=Math.Clamp((Environment.TickCount64-panelFadeStarted)/180d,0,1);
                Native.SetLayeredWindowAttributes(hwnd,0,(byte)(255*(1-progress)),2);
                if(progress>=1)Hide();
            };
            panelFadeReady=true;
        }
        panelFadeTimer.Start();
    }
    private void CancelPanelFade()
    {
        panelFadeTimer.Stop();
        if(!panelFading)return;
        Native.SetLayeredWindowAttributes(hwnd,0,255,2);
        // Restore only the flag owned by this animation. Restoring an entire old
        // style snapshot can undo a later presenter/topmost change.
        long current=(long)Native.GetWindowLongPtr(hwnd,-20);
        Native.SetWindowLongPtr(hwnd,-20,(nint)((current&~0x80000L)|((long)panelOriginalStyle&0x80000L)));panelFading=false;
    }
}
