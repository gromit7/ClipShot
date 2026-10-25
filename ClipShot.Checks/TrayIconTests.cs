using System.Drawing;
using Xunit;
namespace ClipShot;
public class TrayIconTests
{
    private static string IconPath=>Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico");
    [Theory][InlineData(1,"ClipShot.ico")][InlineData(2,"ClipShot.Muted.ico")]
    public void SuppliedStateIconIsUsedWithoutRecolouring(int state,string name)
    {
        using var icon=new Icon(Path.Combine(Path.GetDirectoryName(IconPath)!,name),32,32);using var original=icon.ToBitmap();using var result=TrayIconRenderer.Render(IconPath,(MicrophoneState)state);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)Assert.Equal(original.GetPixel(x,y),result.GetPixel(x,y));
    }
    [Fact]public void UnavailableIsGreyAndKeepsTransparency()
    {
        using var icon=new Icon(IconPath,32,32);using var original=icon.ToBitmap();using var result=TrayIconRenderer.Render(IconPath,MicrophoneState.Unavailable);
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){var colour=result.GetPixel(x,y);Assert.Equal(original.GetPixel(x,y).A,colour.A);Assert.Equal(colour.R,colour.G);Assert.Equal(colour.G,colour.B);}
    }
    [Fact]public void MixedAddsAmberBadgeButKeepsMainArtwork()
    {
        using var icon=new Icon(IconPath,32,32);using var original=icon.ToBitmap();using var result=TrayIconRenderer.Render(IconPath,MicrophoneState.Mixed);
        Assert.Equal(original.GetPixel(16,12),result.GetPixel(16,12));var badge=result.GetPixel(25,25);Assert.True(badge.R>230&&badge.G>170&&badge.B<110);
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
