namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunNavigationResetChecks(List<string> results)
    {
        if(!isolated)throw new InvalidOperationException("Navigation checks require isolated data.");
        string layout=store.Get("sectionlayout","side");
        var savedExpanded=expanded.ToArray();var savedFolders=expandedTemplateFolders.ToArray();
        string? section=activeSection;bool initialized=sectionInitialized;
        try
        {
            foreach(string mode in new[]{"side","inline"})
            {
                store.Set("sectionlayout",mode);expanded["Сегодня"]=true;expanded["Шаблоны"]=true;
                expandedTemplateFolders[long.MaxValue]=true;activeSection="Сегодня";sectionInitialized=true;Render();
                AppWindow.Resize(new Windows.Graphics.SizeInt32(AppWindow.Size.Width,600));
                Hide();
                bool closed=expanded.Values.All(value=>!value)&&expandedTemplateFolders.Count==0&&activeSection is null&&!sectionPopup.IsOpen&&!preview.IsOpen;
                results.Add($"{(closed?"PASS":"FAIL")} {mode} hide clears all date sections, template folders and preview");
                ShowNearCursor();await Task.Delay(140);Root.UpdateLayout();
                bool compact=AllSectionsCollapsed&&!sectionPopup.IsOpen&&visible.Count==0&&AppWindow.Size.Height<400;
                results.Add($"{(compact?"PASS":"FAIL")} {mode} reopening preserves compact collapsed navigation");
                ToggleSection("Сегодня");await Task.Delay(140);
                bool reopened=mode=="side"?activeSection=="Сегодня"&&sectionPopup.IsOpen:expanded.GetValueOrDefault("Сегодня")&&visible.Count>0;
                results.Add($"{(reopened?"PASS":"FAIL")} {mode} explicit heading click opens normally after reset");
            }
        }
        finally
        {
            expanded.Clear();foreach(var pair in savedExpanded)expanded[pair.Key]=pair.Value;
            expandedTemplateFolders.Clear();foreach(var pair in savedFolders)expandedTemplateFolders[pair.Key]=pair.Value;
            store.Set("sectionlayout",layout);activeSection=section;sectionInitialized=initialized;Render();
        }
    }
}
