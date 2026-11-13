using System.Drawing;
using Xunit;
namespace ClipShot;
public class TrayIconTests
{
    private static string IconPath=>Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico");
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public void Clipboard_stays_bright_and_identical_across_microphone_states(int value)
    {
        var state=(MicrophoneState)value;using var reference=TrayIconRenderer.Render(MicrophoneState.Enabled);using var result=TrayIconRenderer.Render(state);
        Assert.Equal(0,result.GetPixel(0,0).A);
        for(int y=2;y<21;y++)for(int x=4;x<29;x++)Assert.Equal(reference.GetPixel(x,y),result.GetPixel(x,y));
        var blue=result.GetPixel(7,15);Assert.True(blue.B>220&&blue.R<40&&blue.A==255);
        var paper=result.GetPixel(15,13);Assert.True(paper.R>230&&paper.G>230&&paper.B>230);
    }
    [Fact]public void Secondary_microphone_markers_are_distinct()
    {
        var colors=new HashSet<int>();
        foreach(var state in Enum.GetValues<MicrophoneState>())
        {using var icon=TrayIconRenderer.Render(state);colors.Add(icon.GetPixel(26,23).ToArgb());}
        Assert.Equal(4,colors.Count);
    }
    [Theory][InlineData("ClipShot.ico")][InlineData("ClipShot.Muted.ico")]
    public void IconContainsSmallTrayAndLargeExplorerSizes(string name)
    {
        using var reader=new BinaryReader(File.OpenRead(Path.Combine(Path.GetDirectoryName(IconPath)!,name)));
        Assert.Equal(0,reader.ReadUInt16());Assert.Equal(1,reader.ReadUInt16());int count=reader.ReadUInt16();var sizes=new HashSet<int>();
        for(int i=0;i<count;i++){int width=reader.ReadByte();reader.ReadBytes(15);sizes.Add(width==0?256:width);}
        foreach(int size in new[]{16,20,24,32,40,48,64,96,128,256})Assert.Contains(size,sizes);
    }
}
