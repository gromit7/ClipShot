using Xunit;

namespace ClipShot;
public sealed class SidePanelHeightTests
{
    [Theory][InlineData(1)][InlineData(1.25)][InlineData(1.5)][InlineData(2)]
    public void Long_content_uses_the_available_monitor_height_at_every_dpi(double scale)
    {
        Assert.Equal(1424,SidePanelPlacement.HeightForContent(900*scale,1440,560*scale));
    }
    [Fact]public void Small_content_keeps_its_natural_height_instead_of_filling_the_monitor()
    {
        Assert.Equal(231,SidePanelPlacement.HeightForContent(230.25,1080,560));
    }
    [Fact]public void Content_beyond_the_compact_threshold_can_exceed_the_old_560_cap()
    {
        Assert.Equal(560,SidePanelPlacement.HeightForContent(560,1080,560));
        Assert.Equal(1064,SidePanelPlacement.HeightForContent(561,1080,560));
    }
    [Fact]public void A_small_work_area_is_the_limit_even_for_otherwise_compact_content()
    {
        Assert.Equal(384,SidePanelPlacement.HeightForContent(500,400,560));
    }
    [Theory][InlineData(-1920,40,1920,1040,1400)][InlineData(0,-1080,1920,1080,50)]
    public void Full_height_preserves_direction_width_and_stays_inside_negative_work_origins(int workX,int workY,int workWidth,int workHeight,int relativeX)
    {
        int desired=SidePanelPlacement.HeightForContent(20000,workHeight,560);
        var small=SidePanelPlacement.Choose(workX+relativeX,workY+200,260,220,workX,workY,workWidth,workHeight,360,200,workY+230);
        var full=SidePanelPlacement.Choose(workX+relativeX,workY+200,260,220,workX,workY,workWidth,workHeight,360,desired,workY+230);
        Assert.Equal(small.OnRight,full.OnRight);Assert.Equal(small.X,full.X);Assert.Equal(small.Width,full.Width);
        Assert.Equal(workY+8,full.Y);Assert.Equal(workHeight-16,full.Height);Assert.Equal(workY+workHeight-8,full.Y+full.Height);
    }
    [Theory][InlineData(double.NaN)][InlineData(double.PositiveInfinity)][InlineData(-1)]
    public void Invalid_measurement_is_not_silently_used_as_a_viewport_size(double content)=>Assert.Throws<ArgumentOutOfRangeException>(()=>SidePanelPlacement.HeightForContent(content,1080,560));
}
