using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System.Diagnostics;
using System.Text.Json;

namespace ClipShot;
public sealed partial class MainWindow
{
    // Used unchanged in baseline and candidate builds. Never reads user data.
    private async Task RunMemoryBenchmark()
    {
        var samples=new List<object>();
        uint clipboard=Native.GetClipboardSequenceNumber();
        void Sample(string stage)
        {
            using var process=Process.GetCurrentProcess();process.Refresh();
            var bitmaps=visible.Where(v=>v.Clip.Kind=="image").Select(v=>FindBitmap(v.Row)).Where(b=>b is not null).ToArray();
            samples.Add(new{Stage=stage,WorkingSet=process.WorkingSet64,PrivateBytes=process.PrivateMemorySize64,
                ManagedBytes=GC.GetTotalMemory(false),DecodedImages=bitmaps.Length,
                ReportedSourcePixels=bitmaps.Sum(b=>(long)b!.PixelWidth*b.PixelHeight)});
        }
        static BitmapImage? FindBitmap(DependencyObject node)
        {
            if(node is Image image)return image.Source as BitmapImage;
            for(int i=0;i<VisualTreeHelper.GetChildrenCount(node);i++)
                if(FindBitmap(VisualTreeHelper.GetChild(node,i)) is {} bitmap)return bitmap;
            return null;
        }
        async Task Ready()
        {
            for(int i=0;i<120;i++)
            {
                await Task.Delay(250);
                if(visible.Count(v=>v.Clip.Kind=="image"&&FindBitmap(v.Row) is not null)==32){await Task.Delay(1000);return;}
            }
            throw new TimeoutException("Synthetic thumbnails did not finish");
        }
        async Task Scroll()
        {
            if(HistoryScroll.ScrollableHeight<=0)throw new InvalidOperationException("Synthetic history is not scrollable");
            foreach(double fraction in new[]{.5,1,.5,0})
            {
                HistoryScroll.ChangeView(null,HistoryScroll.ScrollableHeight*fraction,null,true);
                await Task.Delay(250);
                if(Math.Abs(HistoryScroll.VerticalOffset-HistoryScroll.ScrollableHeight*fraction)>2)
                    throw new InvalidOperationException("Synthetic scroll did not reach its target");
            }
        }
        try
        {
            if(!isolated)throw new InvalidOperationException("Benchmark requires isolated mode");
            foreach(var clip in store.All())store.Delete(clip.Id);
            ResetNavigation();Render();await Task.Delay(1000);Sample("empty");
            await SeedMemoryImages();
            store.Set("sectionlayout","inline");ResetNavigation();Render();await Task.Delay(1500);Sample("collapsed");
            expanded["РЎРµРіРѕРґРЅСЏ"]=true;Render();await Ready();Sample("open");
            await Scroll();Sample("scrolled-0");
            hovered=visible.First(v=>v.Clip.Kind=="image").Clip;hoverAnchor=visible.First(v=>v.Clip.Id==hovered.Id).Row;
            ShowPreview();await Task.Delay(1500);if(!preview.IsOpen)throw new InvalidOperationException("Synthetic preview did not open");Sample("preview");
            for(int cycle=1;cycle<=5;cycle++)
            {
                Hide();await Task.Delay(500);Sample("hidden-"+cycle);
                ShowNearCursor();expanded["РЎРµРіРѕРґРЅСЏ"]=true;Render();await Ready();await Scroll();Sample("reopen-"+cycle);
            }
            Hide();await Task.Delay(2000);Sample("idle-hidden");
            // Separate diagnostic sample; headline comparison uses unforced samples above.
            GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();await Task.Delay(1000);Sample("hidden-after-gc");
            if(clipboard!=Native.GetClipboardSequenceNumber())throw new InvalidOperationException("System clipboard changed during benchmark");
            File.WriteAllText(Path.Combine(dataFolder,"memory-checks.json"),JsonSerializer.Serialize(new{Passed=true,Application="Full ClipShot WinUI executable",Version=typeof(App).Assembly.GetName().Version!.ToString(),Images=32,Width=1920,Height=1080,Openings=6,Scrolls=24,Hides=6,Previews=1,Samples=samples},new JsonSerializerOptions{WriteIndented=true}));
        }
        catch(Exception error)
        {
            Environment.ExitCode=1;
            File.WriteAllText(Path.Combine(dataFolder,"memory-checks.json"),JsonSerializer.Serialize(new{Passed=false,Error=error.GetType().Name+": "+error.Message,Samples=samples}));
        }
        finally{Quit();}
    }
    private async Task SeedMemoryImages()
    {
        for(int index=0;index<32;index++)
        {
            byte[] pixels=new byte[1920*1080*4];var random=new Random(712+index);
            for(int y=0;y<1080;y++)for(int x=0;x<1920;x++)
            {
                int offset=(y*1920+x)*4;
                byte value=(byte)(y<160?random.Next(256):(x/12+y/12+index)%256);
                pixels[offset]=value;pixels[offset+1]=(byte)(value^index);pixels[offset+2]=(byte)(value^64);pixels[offset+3]=255;
            }
            var png=await DesktopCapture.Png(new ScreenFrame(0,0,1920,1080,pixels));
            store.Add("image","",image:png);
        }
    }
}
