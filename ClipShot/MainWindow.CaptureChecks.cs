using Microsoft.UI.Xaml;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace ClipShot;
public sealed partial class MainWindow
{
    private void WriteShellReport(string result="")=>File.WriteAllText(Path.Combine(dataFolder,"shell-checks.txt"),$"Tray={trayReady}; HistoryKey={historyKeyReady}; ScreenshotKey={screenshotKeyReady}; AltF1Events={historyHotkeyCount}; AltF2Events={screenshotHotkeyCount}; {result}");
    private static ScreenFrame SyntheticFrame()
    {
        byte[] pixels=new byte[800*600*4];for(int y=0;y<600;y++)for(int x=0;x<800;x++){int i=(y*800+x)*4;pixels[i]=(byte)(x%256);pixels[i+1]=(byte)(y%256);pixels[i+2]=100;pixels[i+3]=255;}
        return new(0,0,800,600,pixels);
    }
    // Tests use synthetic pixels only: no desktop reading and no clipboard writes.
    private async Task RunCaptureChecks(bool interactive)
    {
        string report=Path.Combine(dataFolder,"capture-checks.txt");
        try
        {
            byte[] pixels=new byte[800*600*4];for(int y=0;y<600;y++)for(int x=0;x<800;x++){int i=(y*800+x)*4;pixels[i]=(byte)(x%256);pixels[i+1]=(byte)(y%256);pixels[i+2]=100;pixels[i+3]=255;}
            var source=new ScreenFrame(0,0,800,600,pixels);
            nint previousCursor=CaptureSelectionCursor.CurrentHandle;
            var window=new RegionCaptureWindow(source,true);var task=window.SelectAsync();
            if(!window.HasSelectionCursor||!window.SelectionCursorIsCurrent)throw new InvalidOperationException("Capture cursor was not installed before mouse movement");
            if(!interactive)
            {
                await Task.Delay(400);
                if(!window.SelectionCursorIsCurrent)throw new InvalidOperationException("XAML replaced the stationary capture cursor");
                window.PreviewTestSelection(new PixelRegion(25,30,240,160));await Task.Delay(150);
                if(!window.SelectionCursorIsCurrent)throw new InvalidOperationException("Selection rendering replaced the capture cursor");
                window.CompleteTest(new PixelRegion(25,30,240,160));
            }
            var selection=await task;
            if(selection is null)throw new InvalidOperationException("Selection cancelled");
            if(!interactive&&previousCursor!=0&&CaptureSelectionCursor.CurrentHandle!=previousCursor)throw new InvalidOperationException("Completed capture did not restore its previous cursor");
            var png=await DesktopCapture.Png(selection);
            using var stream=new InMemoryRandomAccessStream();using(var writer=new DataWriter(stream.GetOutputStreamAt(0))){writer.WriteBytes(png);await writer.StoreAsync();}stream.Seek(0);var decoder=await BitmapDecoder.CreateAsync(stream);
            if(decoder.PixelWidth!=selection.Width||decoder.PixelHeight!=selection.Height)throw new InvalidOperationException("PNG dimensions mismatch");
            if(!interactive&&(selection.Width!=240||selection.Height!=160))throw new InvalidOperationException("Crop mismatch");
            store.Add("image","",image:png);Render();
            previousCursor=CaptureSelectionCursor.CurrentHandle;
            var cancelWindow=new RegionCaptureWindow(source,true);var cancel=cancelWindow.SelectAsync();await Task.Delay(100);
            if(!cancelWindow.HasSelectionCursor||!cancelWindow.SelectionCursorIsCurrent)throw new InvalidOperationException("Repeated capture did not install its cursor");
            cancelWindow.Cancel();if(await cancel is not null)throw new InvalidOperationException("Cancel mismatch");
            if(previousCursor!=0&&CaptureSelectionCursor.CurrentHandle!=previousCursor)throw new InvalidOperationException("Cancelled capture did not restore its previous cursor");
            File.WriteAllText(report,$"PASS selection {selection.Width}x{selection.Height}; immediate stationary native/XAML crosshair; cursor preserved during drawing; cursor restored after completion and cancel; PNG roundtrip; history; no desktop or clipboard access");
        }
        catch(Exception e){Environment.ExitCode=1;File.WriteAllText(report,$"FAIL {e.GetType().Name}: {e.Message}");}
        Quit();
    }
}
