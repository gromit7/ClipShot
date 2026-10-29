namespace ClipShot;
public sealed partial class MainWindow
{
    private async Task RunEditorChecks(bool interactive)
    {
        try
        {
            var fixture=new ScreenFrame(0,0,16000,600,Enumerable.Repeat((byte)255,16000*600*4).ToArray());
            // High-frequency synthetic detail catches whole-image resampling;
            // no real screenshot or clipboard data participates in this test.
            for(int y=400;y<fixture.Height;y++)for(int x=0;x<fixture.Width;x++)
            {int i=(y*fixture.Width+x)*4;byte v=(byte)((x+y)%2==0?0:255);fixture.Pixels[i]=fixture.Pixels[i+1]=fixture.Pixels[i+2]=v;}
            fixture=AnnotationRenderer.Render(fixture,[new(MarkTool.Text,new(24,180),new(24,180),"Синтетический текст · 0123456789 · тонкие детали",Color:"#111111",Width:1)]);
            var fixturePng=await DesktopCapture.Png(fixture);store.Add("image","",image:fixturePng);
            var image=store.All().Single(c=>c.Image is not null&&c.Image.SequenceEqual(fixturePng));
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
            var saved=store.All();var edited=saved.SingleOrDefault(c=>c.Id!=image.Id&&c.Image is not null&&c.Image.SequenceEqual(png!));
            checks.Add(saved.Count==initialCount+1&&saved.Single(c=>c.Id==image.Id).Image!.SequenceEqual(image.Image!)&&edited?.ImageSourceId==image.Id?"PASS applying edits preserves the original and saves a separate linked screenshot":"FAIL original was changed or edited copy is missing");
            var pair=HistoryView.Groups(saved,"","",DateTime.Today).Single(g=>g.Clips.Any(c=>c.Id==image.Id)).Clips;
            checks.Add(edited is not null&&Math.Abs(pair.FindIndex(c=>c.Id==image.Id)-pair.FindIndex(c=>c.Id==edited.Id))==1?"PASS edited screenshot is adjacent to its original in history":"FAIL edited screenshot is separated from its original");
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
