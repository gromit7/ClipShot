using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
namespace ClipShot;
public sealed partial class MainWindow
{
    private void ConfigureWindowContextMenu()
    {
        var menu=new MenuFlyout();TrackMenu(menu);
        void Item(string text,Action action){var item=new MenuFlyoutItem{Text=T(text)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        var alwaysOnTop=new ToggleMenuFlyoutItem{Text=T("Всегда поверх окон"),IsChecked=AlwaysOnTop,Tag="alwaysontop"};
        alwaysOnTop.Click+=AlwaysOnTopClick;menu.Items.Add(alwaysOnTop);menu.Items.Add(new MenuFlyoutSeparator());
        Item("Настройки",()=>SettingsClick(this,new()));Item("Справка",()=>HelpClick(this,new()));menu.Items.Add(new MenuFlyoutSeparator());
        Item("Скрыть",Hide);Item("Выход",Quit);Root.ContextFlyout=menu;
    }
    private void DragEmptySpace(object sender,PointerRoutedEventArgs e)
    {
        if(e.Handled||!IsWindowDragSource(e.OriginalSource as DependencyObject))return;
        TryStartWindowPointerGesture(WindowPointerGesture.Move,e);
    }
    private bool IsWindowDragSource(DependencyObject? source)
    {
        for(var node=source;node is not null;node=VisualTreeHelper.GetParent(node))
        {
            if(ReferenceEquals(node,Root))return true;
            if(ReferenceEquals(node,HeaderSurface)||node is ButtonBase or TextBox or ScrollBar or Slider||node is Border{Tag:Border})return false;
        }
        return false;
    }
}
