using ClipShot;
using Xunit;

namespace ClipShot.Checks;
public sealed class InlinePanelPlacementTests
{
    [Fact]public void Preserves_manual_width_and_docks_at_the_work_area_right_edge()
    {
        var p=InlinePanelPlacement.Choose(377,150,0,40,1920,1000,280,1,16);
        Assert.Equal(377,p.Width);Assert.Equal(1543,p.X);Assert.Equal(150,p.Y);Assert.Equal(296,p.Height);Assert.False(p.Overflow);
    }
    [Theory]
    [InlineData(1,116)][InlineData(1.25,141)][InlineData(1.5,166)][InlineData(2,216)]
    public void Dpi_scales_content_once_and_does_not_rescale_manual_pixel_width(double scale,int height)
    {
        var p=InlinePanelPlacement.Choose(450,0,0,0,1920,1080,100,scale,16);
        Assert.Equal(450,p.Width);Assert.Equal(height,p.Height);Assert.Equal(1920,p.X+p.Width);
    }
    [Theory]
    [InlineData(-1920,-120,1920,1080)]
    [InlineData(240,-1080,1600,900)]
    [InlineData(-800,50,600,700)]
    public void Honors_work_area_origin_and_taskbar_insets(int x,int y,int width,int height)
    {
        var p=InlinePanelPlacement.Choose(320,int.MaxValue,x,y,width,height,300,1.5,12);
        Assert.Equal(x+width,p.X+p.Width);Assert.True(p.Y>=y);Assert.Equal(y+height,p.Y+p.Height);
    }
    [Fact]public void Expanding_and_collapsing_change_height_without_changing_width_or_right_anchor()
    {
        var collapsed=InlinePanelPlacement.Choose(350,200,0,0,1920,1080,120,1.25,16);
        var expanded=InlinePanelPlacement.Choose(350,200,0,0,1920,1080,600,1.25,16);
        var again=InlinePanelPlacement.Choose(350,200,0,0,1920,1080,120,1.25,16);
        Assert.True(expanded.Height>collapsed.Height);Assert.Equal(collapsed,again);Assert.Equal(collapsed.X,expanded.X);Assert.Equal(collapsed.Width,expanded.Width);
    }
    [Fact]public void Overflow_fills_only_the_work_area_and_requests_scrolling()
    {
        var p=InlinePanelPlacement.Choose(400,500,-1600,40,1600,900,12000,1.5,20);
        Assert.True(p.Overflow);Assert.Equal(40,p.Y);Assert.Equal(900,p.Height);Assert.Equal(0,p.X+p.Width);
    }
    [Fact]public void Oversized_width_is_clipped_to_the_primary_work_area()
    {
        var p=InlinePanelPlacement.Choose(2400,-500,80,60,1280,720,100,1,16);
        Assert.Equal(80,p.X);Assert.Equal(60,p.Y);Assert.Equal(1280,p.Width);Assert.Equal(116,p.Height);
    }
    [Fact]public void Fractional_content_rounds_up_to_a_complete_physical_pixel()
    {
        var p=InlinePanelPlacement.Choose(260,0,0,0,1920,1080,100.1,1.25,16);
        Assert.Equal(142,p.Height);
    }
    [Theory][InlineData(0)][InlineData(-1)][InlineData(double.NaN)][InlineData(double.PositiveInfinity)]
    public void Invalid_scale_cannot_produce_a_placement(double scale)=>Assert.Throws<ArgumentOutOfRangeException>(()=>InlinePanelPlacement.Choose(260,0,0,0,1920,1080,100,scale));
}
