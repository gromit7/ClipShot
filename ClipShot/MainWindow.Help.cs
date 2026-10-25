using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ClipShot;
public sealed partial class MainWindow
{
    private string HelpText=>string.Join("\n\n",new[]{
        "• "+Win32Hotkey.Describe(AppSettings.FunctionKey(store.Get("key","112"),112),store.Get("ctrl","0")=="1")+" — "+T("история рядом с курсором; поиск по тексту, выбор мышью или клавишами."),
        "• "+T("Текст и картинки: история по дням, без дублей, увеличенный просмотр при наведении."),
        "• "+T("Шаблоны: Ctrl+S сохраняет выбранный клип; изменение текста и порядка. Очистка истории их не удаляет."),
        "• "+T("Выбор клипа копирует его и оставляет окно открытым. Автовставка в предыдущее приложение — в настройках."),
        "• "+Win32Hotkey.Describe(AppSettings.FunctionKey(store.Get("shotkey","113"),113),store.Get("shotctrl","0")=="1")+(store.Get("shiftprint","1")=="1"?" / Shift+PrintScreen":"")+" — "+T("снимок выделенной области в буфер и историю; Esc отменяет."),
        "• "+T("Редактор: стрелки, рамки, текст, мозаика, скрытие, отмена и повтор. «Копировать в буфер» применяет правки к тому же клипу и буферу. «Экспорт» сохраняет отдельный PNG или JPEG."),
        "• "+T("Микрофон: переключение клавиатурой или боковой кнопкой мыши; основной, все или выбранные устройства. Состояние — на значке в трее и короткой плашке."),
        "• "+T("Разделы раскрываются в сторону свободного места; обычный список можно вернуть в настройках. Пустые разделы скрыты."),
        "• "+T("Семь тем: градиентная, прозрачная, белая, чёрная, тёмная, Windows 98 и Windows XP."),
        "• "+T("Хранение истории: 30 дней, 90 дней или бессрочно; ручная очистка. Данные остаются на этом ПК."),
        "• "+T("Компактная панель: даты сверху, шаблоны снизу, поиск под лупой. Щелчок вне программы плавно скрывает панель. Значок трея и Alt+F1 возвращают её. Правая кнопка на пустом месте — настройки и справка; нижний угол меняет размер."),
        "• "+T("Обновления с GitHub: две фоновые проверки в рабочий день. Скачивание в фоне, установка во время перерыва с сохранением данных. После — только плашка «Программа обновлена». Ручная проверка — в меню трея, одним нажатием."),
        "• "+T("Русский язык по умолчанию; English и ссылка на открытый исходный код — в настройках.")});
    private ContentDialog CreateHelpDialog()=>new()
    {
        Title=T("Возможности ClipShot"),Content=new ScrollViewer{MaxHeight=460,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,Content=new TextBlock{Text=HelpText,TextWrapping=TextWrapping.Wrap,MaxWidth=440,FontSize=14}},
        CloseButtonText=T("Понятно"),DefaultButton=ContentDialogButton.Close,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme
    };
    private async void HelpClick(object sender,RoutedEventArgs e)
    {
        if(dialogOpen||exiting)return;
        preview.IsOpen=false;hoverTimer.Stop();await ShowDialogAsync(CreateHelpDialog());
    }
}
