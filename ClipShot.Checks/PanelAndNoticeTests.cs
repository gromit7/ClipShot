using Xunit;
namespace ClipShot;
public class PanelAndNoticeTests
{
    [Fact]public void MoreSpaceOnRightChoosesRight(){var p=SidePanelPlacement.Choose(50,100,520,680,0,0,1920,1080,360,500,150);Assert.True(p.OnRight);Assert.True(p.X>=570);}
    [Fact]public void MoreSpaceOnLeftChoosesLeft(){var p=SidePanelPlacement.Choose(1300,100,520,680,0,0,1920,1080,360,500,150);Assert.False(p.OnRight);Assert.True(p.X+p.Width<=1300);}
    [Theory][InlineData(-1920,0,1920,1080)][InlineData(0,0,800,600)][InlineData(0,-1080,1920,1080)]
    public void PanelStaysInsideWorkArea(int x,int y,int width,int height){var p=SidePanelPlacement.Choose(x+50,y+200,520,680,x,y,width,height,360,560,y+500);Assert.True(p.X>=x&&p.Y>=y&&p.X+p.Width<=x+width&&p.Y+p.Height<=y+height);}
    [Fact]public void NoticeRemainsVisibleWhileUserIsAway(){var notice=new NoticeLifetime();Assert.Equal(255,notice.Opacity(TimeSpan.FromHours(12),false));}
    [Fact]public void NoticeWaitsForReadingAndThenFadesAfterReturn(){var notice=new NoticeLifetime();Assert.Equal(255,notice.Opacity(TimeSpan.FromHours(1),true));Assert.Equal(255,notice.Opacity(TimeSpan.FromHours(1)+TimeSpan.FromSeconds(1),false));byte halfway=notice.Opacity(TimeSpan.FromHours(1)+TimeSpan.FromMilliseconds(1950),false);Assert.InRange(halfway,(byte)120,(byte)135);Assert.Equal(0,notice.Opacity(TimeSpan.FromHours(1)+TimeSpan.FromSeconds(3),false));}
    [Theory][InlineData(20,20,900,24)][InlineData(100,100,102,400)][InlineData(10,80,200,250)]
    public void ArrowTipIsExactlyTheMouseTarget(double x1,double y1,double x2,double y2){var result=AnnotationGeometry.AimArrow(new(x1,y1),new(x2,y2));Assert.Equal(new ImagePoint(x2,y2),result.End);var forced=AnnotationGeometry.AimArrow(new(x1,y1),new(x2,y2),true);Assert.Equal(new ImagePoint(x2,y2),forced.End);}
}
