namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunEditorChecks(bool interactive)
    {
        try
        {
            var image=store.All().First(c=>c.Image is not null);
            editor=await ScreenshotEditor.CreateAsync(image.Image!,words,Root.RequestedTheme);Hide();var result=editor.ShowAsync();
            if(interactive){await result;editor=null;if(!exiting)ShowNearCursor();return;}
            uint sequence=Native.GetClipboardSequenceNumber();var checks=await editor.CheckAsync();var png=await result;
            checks.Add(sequence==Native.GetClipboardSequenceNumber()?"PASS editor test does not change system clipboard":"FAIL system clipboard changed");
            checks.Add(png is not null?"PASS edited image is available to the calling window":"FAIL missing result");
            File.WriteAllLines(Path.Combine(dataFolder,"editor-checks.txt"),checks);
            if(checks.Any(c=>c.StartsWith("FAIL ",StringComparison.Ordinal)))Environment.ExitCode=1;
        }
        catch(Exception e){Environment.ExitCode=1;File.WriteAllText(Path.Combine(dataFolder,"editor-checks.txt"),"FAIL "+e.GetType().Name+": "+e.Message);}
        finally{if(!interactive&&editor is not null&&!editor.IsClosed)editor.Close();if(!interactive)editor=null;}
        if(!interactive)Quit();
    }
}
