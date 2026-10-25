using Microsoft.UI.Xaml;

namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly DispatcherTimer sectionHoverTimer=new(){Interval=TimeSpan.FromMilliseconds(220)};
    private string? pendingHoverSection,suppressedHoverSection;
    private FrameworkElement? pendingHoverHeading;
    private bool sectionHoverReady;
    private bool CanOpenSectionOnHover=>!exiting&&!dialogOpen&&!takingScreenshot&&!windowResizing&&!panelFading&&editor is null&&openMenus==0&&AppWindow.IsVisible&&Search.Text.Trim().Length==0;

    // Единая задержка на заголовках: быстрое движение не закрывает прежний список.
    // Таймер раскрывает только последний непустой раздел и никогда не копирует клип.
    private void QueueSectionHover(string title,FrameworkElement heading)
    {
        CancelSectionHover();
        if(!CanOpenSectionOnHover||!sectionHeadings.TryGetValue(title,out var current)||!ReferenceEquals(current,heading))return;
        if(suppressedHoverSection==title)return;
        suppressedHoverSection=null;
        if(SideSections?activeSection==title&&sectionPopup.IsOpen:expanded.GetValueOrDefault(title))return;
        pendingHoverSection=title;pendingHoverHeading=heading;
        if(!sectionHoverReady){sectionHoverTimer.Tick+=(_,_)=>OpenPendingHoverSection();sectionHoverReady=true;}
        sectionHoverTimer.Start();
    }
    private void LeaveSectionHover(string title,FrameworkElement heading)
    {
        if(ReferenceEquals(pendingHoverHeading,heading))CancelSectionHover();
        // Удаление старого заголовка при Render не является уходом мыши.
        if(!rendering&&suppressedHoverSection==title&&sectionHeadings.TryGetValue(title,out var current)&&ReferenceEquals(current,heading))suppressedHoverSection=null;
    }
    private void CancelSectionHover()
    {
        sectionHoverTimer.Stop();pendingHoverSection=null;pendingHoverHeading=null;
    }
    private void OpenPendingHoverSection()
    {
        string? title=pendingHoverSection;var heading=pendingHoverHeading;CancelSectionHover();
        if(title is null||!CanOpenSectionOnHover||!sectionHeadings.TryGetValue(title,out var current)||!ReferenceEquals(current,heading))return;
        if(SideSections)
        {
            if(activeSection==title&&sectionPopup.IsOpen)return;
            activeSection=title;sectionInitialized=true;
        }
        else
        {
            if(expanded.GetValueOrDefault(title))return;
            expanded[title]=true;
        }
        Render();
        // Наведение раскрывает список; конкретную запись выбирают отдельно.
        selected=-1;Highlight();
    }
}
