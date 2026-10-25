using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Graphics;
namespace ClipShot;
public sealed partial class MainWindow
{
    private bool buildingTemplates,compactResizing;
    private int compactGroupCount=-1,openMenus;
    private readonly Dictionary<string,FrameworkElement> sectionHeadings=[];
    private readonly DispatcherTimer panelFadeTimer=new(){Interval=TimeSpan.FromMilliseconds(16)};
    private bool panelFading,panelFadeReady;
    private long panelFadeStarted;
    private nint panelOriginalStyle;
    private void FitCompactPanel(List<ClipGroup> groups)
    {
        int count=groups.Count(g=>g.Clips.Count>0);
        if(!SideSections||compactGroupCount==count||dialogOpen)return;
        compactGroupCount=count;
        if(store.Get("panelautofit","1")!="1")return;
        double scale=Math.Max(1,Native.GetDpiForWindow(hwnd)/96d);
        int height=(int)(Math.Max(100,count*41+64)*scale);
        compactResizing=true;
        try{AppWindow.Resize(new SizeInt32(Math.Max(AppWindow.Size.Width,(int)(220*scale)),height));}
        finally{compactResizing=false;}
        DispatcherQueue.TryEnqueue(PositionSection);
    }
    private void TrackMenu(MenuFlyout menu)
    {
        menu.Opened+=(_,_)=>{openMenus++;CancelPanelFade();};
        menu.Closed+=(_,_)=>openMenus=Math.Max(0,openMenus-1);
    }
    private bool CanDismissPanel=>!exiting&&AppWindow.IsVisible&&!dialogOpen&&!takingScreenshot&&!windowResizing&&editor is null&&openMenus==0;
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
        Native.SetWindowLongPtr(hwnd,-20,panelOriginalStyle);panelFading=false;
    }
}
