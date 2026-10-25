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
        if(e.Handled||dialogOpen||takingScreenshot||!e.GetCurrentPoint(Root).Properties.IsLeftButtonPressed)return;
        for(var node=e.OriginalSource as DependencyObject;node is not null&&node!=Root;node=VisualTreeHelper.GetParent(node))
            if(node is ButtonBase or TextBox or ScrollBar or Slider||node is Border{Tag:Border})return;
        e.Handled=true;Native.GetCursorPos(out var mouse);Native.ReleaseCapture();
        nint coordinates=(nint)unchecked((int)(((uint)(ushort)mouse.Y<<16)|(ushort)mouse.X));
        Native.SendMessage(hwnd,0x00A1,2,coordinates);
    }
}
