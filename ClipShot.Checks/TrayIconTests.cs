using System.Drawing;
using Xunit;
namespace ClipShot;
public class TrayIconTests
{
    private static string IconPath=>Path.Combine(AppContext.BaseDirectory,"Assets","ClipShot.ico");
    [Theory][InlineData(0)][InlineData(1)][InlineData(2)][InlineData(3)]
    public void RecolourPreservesClipboardAndTransparency(int value)
    {
        using var icon=new Icon(IconPath,32,32);using var original=icon.ToBitmap();using var result=TrayIconRenderer.Render(IconPath,(MicrophoneState)value);
        int white=0,changed=0;
        for(int y=0;y<original.Height;y++)for(int x=0;x<original.Width;x++)
        {
            var before=original.GetPixel(x,y);var after=result.GetPixel(x,y);Assert.Equal(before.A,after.A);
            if(before.A>240&&before.R==255&&before.G==255&&before.B==255){white++;Assert.Equal(before,after);}
            if(before.ToArgb()!=after.ToArgb())changed++;
        }
        Assert.True(white>20,"The white clipboard must remain recognisable.");Assert.True(changed>50,"The coloured background must indicate the state.");
    }
    [Fact]public void EachStateHasADistinctColour()
    {
        var colours=new HashSet<int>();foreach(var state in Enum.GetValues<MicrophoneState>()){using var bitmap=TrayIconRenderer.Render(IconPath,state);colours.Add(bitmap.GetPixel(4,16).ToArgb());}
        Assert.Equal(4,colours.Count);
    }
}
