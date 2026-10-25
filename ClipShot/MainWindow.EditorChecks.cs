namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunEditorChecks(bool interactive)
    {
        try
        {
            var image=store.All().First(c=>c.Image is not null);
            var editing=EditImage(image);for(int attempt=0;attempt<30&&editor is null;attempt++)await Task.Delay(100);
            var testedEditor=editor??throw new InvalidOperationException("Editor did not open");var result=testedEditor.ShowAsync();
            if(interactive){await editing;if(!exiting)ShowNearCursor();return;}
            int initialCount=store.All().Count;uint sequence=Native.GetClipboardSequenceNumber();var checks=await testedEditor.CheckAsync(async()=>
            {
                if(dialogOpen)throw new InvalidOperationException("Editor incorrectly blocks global capture");
                ToggleVisibility();if(testedEditor.AppWindow.IsVisible)throw new InvalidOperationException("Tray did not hide editor");
                ToggleVisibility();if(!testedEditor.AppWindow.IsVisible)throw new InvalidOperationException("Tray did not restore editor");
                TakeScreenshot();await Task.Delay(400);if(captureWindow is null)throw new InvalidOperationException("Capture did not open over editor");
                captureWindow.Cancel();await Task.Delay(150);
                if(!ReferenceEquals(editor,testedEditor)||testedEditor.IsClosed)throw new InvalidOperationException("Capture replaced or closed editor");
            });var png=await result;await editing;
            checks.Add(store.All().Count==initialCount&&store.All().Single(c=>c.Id==image.Id).Image!.SequenceEqual(png!)?"PASS applying edits updates the same clip without creating a duplicate":"FAIL edit created a second clip or lost its result");
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
