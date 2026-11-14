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
            // Check the running published app, not merely the manifest source.
            // An unaware capture thread virtualizes pixels on scaled displays.
            if(!Native.AreDpiAwarenessContextsEqual(Native.GetThreadDpiAwarenessContext(),(nint)(-4)))
                throw new InvalidOperationException("Capture UI thread is not PerMonitorV2 DPI aware");
            int testX=Native.GetSystemMetrics(76),testY=Native.GetSystemMetrics(77),testWidth=Native.GetSystemMetrics(78),testHeight=Native.GetSystemMetrics(79);
            int testBytes=checked(testWidth*testHeight*4);
            if(testWidth<265||testHeight<190||testBytes>512*1024*1024)throw new InvalidOperationException("Unsupported synthetic desktop geometry");
            byte[] pixels=new byte[testBytes];for(int y=0;y<testHeight;y++)for(int x=0;x<testWidth;x++){int i=(y*testWidth+x)*4;pixels[i]=(byte)(x%256);pixels[i+1]=(byte)(y%256);pixels[i+2]=100;pixels[i+3]=255;}
            // SetCursor is thread-local until this window owns mouse input. A
            // fixed (0,0) fixture could leave the stationary hardware pointer over
            // another application and incorrectly fail the global cursor check.
            // Cover the virtual desktop with synthetic pixels, as production does,
            // so normal pointer movement cannot escape an arbitrary 800x600 fixture.
            // Never move the pointer or read any screen pixels.
            if(!Native.GetCursorPos(out var pointer))throw new InvalidOperationException("Capture test requires an accessible input desktop");
            var source=new ScreenFrame(testX,testY,testWidth,testHeight,pixels);
            void CursorCheck(RegionCaptureWindow capture,string message)
            {
                if(!capture.TestPointerInsideClient)throw new InvalidOperationException("Capture test pointer is outside its actual native client area; "+capture.TestCursorDiagnostics);
                if(!capture.TestOwnsPointer)throw new InvalidOperationException("Capture test pointer is covered by another native window; "+capture.TestCursorDiagnostics);
                if(!capture.HasSelectionCursor||!capture.SelectionCursorIsCurrent)throw new InvalidOperationException(message+"; "+capture.TestCursorDiagnostics);
            }
            nint previousCursor=CaptureSelectionCursor.CurrentHandle;
            var window=new RegionCaptureWindow(source,true,ignoreHardwareInput:!interactive);var task=window.SelectAsync();
            // Check the first loaded surface, not an HWND whose XAML input site
            // has not been created yet. No pointer movement or cursor test repair.
            await window.TestSurfaceReady.WaitAsync(TimeSpan.FromSeconds(2));
            window.CheckClientEdges();
            CursorCheck(window,"Capture cursor was not installed before mouse movement");
            if(!interactive)
            {
                await Task.Delay(400);
                CursorCheck(window,"XAML replaced the stationary capture cursor");
                var previewRegion=new PixelRegion(25,30,240,160);window.PreviewTestSelection(previewRegion);
                var renderDeadline=DateTime.UtcNow.AddSeconds(2);
                while(!window.TestSelectionRendered(previewRegion)&&DateTime.UtcNow<renderDeadline)await Task.Delay(20);
                CursorCheck(window,"Selection rendering replaced the capture cursor");
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
            var cancelWindow=new RegionCaptureWindow(source,true,ignoreHardwareInput:true);var cancel=cancelWindow.SelectAsync();
            await cancelWindow.TestSurfaceReady.WaitAsync(TimeSpan.FromSeconds(2));await Task.Delay(100);
            CursorCheck(cancelWindow,"Repeated capture did not install its cursor");
            cancelWindow.CheckClientEdges();cancelWindow.Cancel();if(await cancel is not null)throw new InvalidOperationException("Cancel mismatch");
            if(previousCursor!=0&&CaptureSelectionCursor.CurrentHandle!=previousCursor)throw new InvalidOperationException("Cancelled capture did not restore its previous cursor");
            File.WriteAllText(report,$"PASS full client area and eight native edges; PerMonitorV2 capture thread; window DPI={Native.GetDpiForWindow(hwnd)}; selection {selection.Width}x{selection.Height}; immediate stationary native/XAML crosshair; cursor preserved during drawing; cursor restored after completion and cancel; PNG roundtrip; history; no desktop or clipboard access");
        }
        catch(Exception e){Environment.ExitCode=1;File.WriteAllText(report,$"FAIL {e.GetType().Name}: {e.Message}");}
        Quit();
    }
}
