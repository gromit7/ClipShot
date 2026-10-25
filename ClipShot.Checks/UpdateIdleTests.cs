using Xunit;
namespace ClipShot;
public class UpdateIdleTests
{
    [Theory][InlineData(299,false)][InlineData(300,true)][InlineData(86400,true)]
    public void AutomaticInstallRequiresFiveMinutesIdle(int seconds,bool expected)=>Assert.Equal(expected,UpdateIdlePolicy.CanInstall(false,true,false,false,TimeSpan.FromSeconds(seconds)));
    [Theory][InlineData(true,true,false,false)][InlineData(false,false,false,false)][InlineData(false,true,true,false)][InlineData(false,true,false,true)]
    public void IsolationSettingsWindowAndOperationsBlockInstall(bool isolated,bool enabled,bool visible,bool busy)=>Assert.False(UpdateIdlePolicy.CanInstall(isolated,enabled,visible,busy,TimeSpan.FromHours(1)));
    [Fact]public void UnknownInputStateBlocksInstall()=>Assert.False(UpdateIdlePolicy.CanInstall(false,true,false,false,null));
    [Fact]public void NewInputCancelsIdlePermission()
    {
        Assert.True(UpdateIdlePolicy.CanInstall(false,true,false,false,UpdateIdlePolicy.Elapsed(900000,0)));
        Assert.False(UpdateIdlePolicy.CanInstall(false,true,false,false,UpdateIdlePolicy.Elapsed(900000,899999)));
    }
    [Fact]public void TickWrapDoesNotInventLongInactivity()=>Assert.Equal(TimeSpan.FromMilliseconds(32),UpdateIdlePolicy.Elapsed(16,uint.MaxValue-15));
    [Fact]public void FutureInputTimestampFailsClosed()=>Assert.Null(UpdateIdlePolicy.Elapsed(100,101));
    [Theory][InlineData(100,5100,true)][InlineData(5101,5100,false)][InlineData(100,5101,false)][InlineData(4294967290,100,true)]
    public void DelayedQuitCannotCloseAppAfterUpdaterHasLeft(uint now,uint deadline,bool expected)=>Assert.Equal(expected,UpdateIdlePolicy.QuitRequestValid(now,deadline));
}
