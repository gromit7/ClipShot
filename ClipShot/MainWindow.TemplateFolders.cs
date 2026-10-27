using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Media;
using System.Globalization;

namespace ClipShot;
public sealed partial class MainWindow
{
    private readonly Dictionary<long,bool> expandedTemplateFolders=[];
    private readonly Dictionary<long,TemplateFolder> renderedTemplateParents=[];
    private readonly HashSet<long> renderedTemplateFolderHeaders=[];
    private readonly Dictionary<long,Button> templateFolderButtons=[];
    private readonly Dictionary<long,Border> templateFolderHeaderRows=[];
    private Border? templateDropIndicator;
    private bool templateDragInProgress;
    private bool templateRenderPending;
    private string? templateDraggedEntry;
    private long templateTapSuppressedUntil;
    private sealed record TemplateFolderButton(long Id);
    private enum TemplateDropZone{Before,Inside,After}
    private static string TemplateClipKey(long id)=>"c:"+id.ToString(CultureInfo.InvariantCulture);
    private static string TemplateFolderKey(long id)=>"f:"+id.ToString(CultureInfo.InvariantCulture);
    private void ResetTemplateRenderState()
    {renderedTemplateParents.Clear();renderedTemplateFolderHeaders.Clear();templateFolderButtons.Clear();templateFolderHeaderRows.Clear();templateDragRows.Clear();}

    private IEnumerable<Clip> TemplateRenderClips(string title,List<Clip> clips,bool searching)
    {
        if(title!="Шаблоны")return clips;
        ResetTemplateRenderState();
        if(searching)return clips;
        var layout=store.GetTemplateLayout();var available=clips.ToDictionary(clip=>clip.Id);var ordered=new List<Clip>();
        var folders=layout.Folders.ToDictionary(folder=>TemplateFolderKey(folder.Id));
        foreach(string key in layout.RootOrder)
        {
            if(folders.TryGetValue(key,out var folder))
            {
                foreach(long id in folder.ClipIds)
                    if(available.Remove(id,out var child)){renderedTemplateParents[id]=folder;ordered.Add(child);}
            }
            else if(key.StartsWith("c:",StringComparison.Ordinal)&&long.TryParse(key.AsSpan(2),out long id)&&available.Remove(id,out var clip))ordered.Add(clip);
        }
        ordered.AddRange(clips.Where(clip=>available.ContainsKey(clip.Id)));return ordered;
    }
    private bool PrepareTemplateFolderRow(string title,Clip clip,bool searching)
    {
        if(title!="Шаблоны"||searching||!renderedTemplateParents.TryGetValue(clip.Id,out var folder))return true;
        if(renderedTemplateFolderHeaders.Add(folder.Id))AddTemplateFolderHeader(folder);
        return expandedTemplateFolders.GetValueOrDefault(folder.Id);
    }
    private void AddTemplateFolderHeader(TemplateFolder folder)
    {
        bool open=expandedTemplateFolders.GetValueOrDefault(folder.Id);
        var label=new Grid{ColumnSpacing=8};label.ColumnDefinitions.Add(new(){Width=new GridLength(12)});label.ColumnDefinitions.Add(new(){Width=new GridLength(18)});label.ColumnDefinitions.Add(new(){Width=new GridLength(1,GridUnitType.Star)});label.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
        label.Children.Add(new FontIcon{Glyph=open?"\uE70D":"\uE76C",FontSize=10});var icon=new FontIcon{Glyph="\uE8B7",FontSize=16};Grid.SetColumn(icon,1);label.Children.Add(icon);
        var name=new TextBlock{Text=folder.Name,FontSize=13,FontWeight=Microsoft.UI.Text.FontWeights.SemiBold,TextTrimming=TextTrimming.CharacterEllipsis,VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(name,2);label.Children.Add(name);
        var count=new TextBlock{Text=folder.ClipIds.Count.ToString(),FontSize=11,Foreground=ClipBrush("ClipSecondaryBrush"),VerticalAlignment=VerticalAlignment.Center};Grid.SetColumn(count,3);label.Children.Add(count);
        var button=new Button{Content=label,Tag=new TemplateFolderButton(folder.Id),HorizontalAlignment=HorizontalAlignment.Stretch,HorizontalContentAlignment=HorizontalAlignment.Stretch,
            Padding=new Thickness(20,9,12,9),CornerRadius=new CornerRadius(0),BorderThickness=new Thickness(0),Background=ClipBrush("ClipHeaderBrush")};
        button.Click+=(_,_)=>{if(!SuppressTemplateTap())ToggleTemplateFolder(folder.Id);};
        var border=new Border{Child=button,BorderBrush=ClipBrush("ClipSeparatorBrush"),BorderThickness=new Thickness(0,0,0,1)};
        var menu=new MenuFlyout();TrackMenu(menu);
        void Item(string text,Action action){var item=new MenuFlyoutItem{Text=T(text)};item.Click+=(_,_)=>action();menu.Items.Add(item);}
        Item("Переименовать папку…",()=>_=RenameTemplateFolder(folder));
        Item("Переместить выше",()=>MoveTemplateEntryByOffset(TemplateFolderKey(folder.Id),-1));
        Item("Переместить ниже",()=>MoveTemplateEntryByOffset(TemplateFolderKey(folder.Id),1));
        Item("Разобрать папку",()=>UngroupTemplateFolder(folder.Id));button.ContextFlyout=menu;
        AttachTemplateDrag(border,TemplateFolderKey(folder.Id));ClipRows.Children.Add(border);templateFolderButtons[folder.Id]=button;templateFolderHeaderRows[folder.Id]=border;
    }
    private static bool IsTemplateFolderKeySource(DependencyObject? source)
    {
        for(var node=source;node is not null;node=VisualTreeHelper.GetParent(node))if(node is ButtonBase{Tag:TemplateFolderButton})return true;
        return false;
    }
    private bool SuppressTemplateTap()=>templateDragInProgress||Environment.TickCount64<templateTapSuppressedUntil;
    private void ToggleTemplateFolder(long id){expandedTemplateFolders[id]=!expandedTemplateFolders.GetValueOrDefault(id);Render();}
    private void UngroupTemplateFolder(long id){store.UngroupTemplateFolder(id);expandedTemplateFolders.Remove(id);Render();}
    private void AttachTemplateClipDrag(Clip clip,Border row,bool searching)
    {
        if(!clip.Template||searching)return;
        if(renderedTemplateParents.ContainsKey(clip.Id))row.Margin=new Thickness(12,0,0,0);
        AttachTemplateDrag(row,TemplateClipKey(clip.Id));
    }
    private static bool IsTemplateEntryKey(string key)=>key.Length>=3&&key[0] is 'c' or 'f'&&key[1]==':'&&
        long.TryParse(key.AsSpan(2),NumberStyles.None,CultureInfo.InvariantCulture,out long id)&&id>0&&key[2..]==id.ToString(CultureInfo.InvariantCulture);
    private void EndTemplateDrag()
    {
        StopTemplatePointerTracking();
        if(templateDropIndicator is not null)templateDropIndicator.Visibility=Visibility.Collapsed;templateDropIndicator=null;
        templateDragInProgress=false;templateDraggedEntry=null;templateTapSuppressedUntil=Environment.TickCount64+350;
        if(templateRenderPending&&!exiting)DispatcherQueue.TryEnqueue(()=>{if(templateRenderPending&&!exiting&&!templateDragInProgress)Render();});
    }
    private static TemplateDropZone TemplateZone(double y,double height)
    {double strip=Math.Min(12,height*.25);return y<strip?TemplateDropZone.Before:y>height-strip?TemplateDropZone.After:TemplateDropZone.Inside;}
    private void AttachTemplateDrag(Border row,string targetKey)
    {
        row.CanDrag=false;row.AllowDrop=false;
        // Overlay markers do not change row measurement while the pointer moves.
        var layer=row.Child as Grid;
        if(layer is null){var child=row.Child;row.Child=null;layer=new Grid();if(child is not null)layer.Children.Add(child);row.Child=layer;}
        var marker=new Border{IsHitTestVisible=false,Visibility=Visibility.Collapsed,BorderBrush=ClipBrush("ClipIndicatorBrush")};layer.Children.Add(marker);
        RegisterTemplatePointerRow(row,targetKey,marker);
    }
    private async Task<bool> ApplyTemplateDrop(string sourceKey,string targetKey,TemplateDropZone zone,Func<Task<string?>>? nameProvider=null)
    {
        if(exiting||Search.Text.Trim().Length!=0||!IsTemplateEntryKey(sourceKey)||!IsTemplateEntryKey(targetKey)||sourceKey==targetKey)return false;
        var layout=store.GetTemplateLayout();bool sourceFolder=sourceKey[0]=='f',targetFolder=targetKey[0]=='f';
        long sourceId=long.Parse(sourceKey.AsSpan(2),CultureInfo.InvariantCulture),targetId=long.Parse(targetKey.AsSpan(2),CultureInfo.InvariantCulture);
        long? parent=targetFolder?null:layout.Folders.FirstOrDefault(folder=>folder.ClipIds.Contains(targetId))?.Id;
        if(zone!=TemplateDropZone.Inside)
        {
            if(sourceFolder&&parent is not null)return false;
            store.MoveTemplateEntry(sourceKey,parent,targetKey,zone==TemplateDropZone.After);
        }
        else if(targetFolder||parent is not null)
        {
            if(sourceFolder)return false;
            long destination=targetFolder?targetId:parent!.Value;store.MoveTemplateEntry(sourceKey,destination);
            expandedTemplateFolders[destination]=true;
        }
        else
        {
            if(sourceFolder)return false;
            string? name=nameProvider is null?await AskTemplateFolderName():await nameProvider();
            if(name is null||exiting)return false;
            long folder=store.CreateTemplateFolder(name,targetId,sourceId);expandedTemplateFolders[folder]=true;
        }
        Render();return true;
    }
    private async Task<string?> AskTemplateFolderName(string initial="")
    {
        var input=new TextBox{Text=initial,MaxLength=80,PlaceholderText=T("Например, IP-адреса"),MinWidth=260};
        input.Loaded+=(_,_)=>{input.Focus(FocusState.Programmatic);input.SelectAll();};
        var error=new TextBlock{TextWrapping=TextWrapping.Wrap};var panel=new StackPanel{Spacing=8};panel.Children.Add(input);panel.Children.Add(error);
        var dialog=new ContentDialog{Title=T(initial.Length==0?"Новая папка шаблонов":"Переименовать папку"),Content=panel,PrimaryButtonText=T("Сохранить"),CloseButtonText=T("Отмена"),DefaultButton=ContentDialogButton.Primary,XamlRoot=Root.XamlRoot,RequestedTheme=Root.RequestedTheme};
        dialog.PrimaryButtonClick+=(_,args)=>{if(string.IsNullOrWhiteSpace(input.Text)||input.Text.Any(char.IsControl)){error.Text=T("Введите имя папки от 1 до 80 символов.");args.Cancel=true;}};
        return await ShowDialogAsync(dialog)==ContentDialogResult.Primary?input.Text.Trim():null;
    }
    private async Task RenameTemplateFolder(TemplateFolder folder)
    {string? name=await AskTemplateFolderName(folder.Name);if(name is not null&&!exiting){store.RenameTemplateFolder(folder.Id,name);Render();}}
    private void MoveTemplateEntryByOffset(string key,int direction)
    {
        var layout=store.GetTemplateLayout();long? parent=key[0]=='c'?layout.Folders.FirstOrDefault(folder=>folder.ClipIds.Any(id=>TemplateClipKey(id)==key))?.Id:null;
        var siblings=parent is long id?layout.Folders.Single(folder=>folder.Id==id).ClipIds.Select(TemplateClipKey).ToList():layout.RootOrder.ToList();
        int index=siblings.IndexOf(key),next=index+Math.Sign(direction);if(index<0||next<0||next>=siblings.Count)return;
        store.MoveTemplateEntry(key,parent,siblings[next],direction>0);Render();
    }
}
