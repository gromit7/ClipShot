using Xunit;
namespace ClipShot;

public class UpdateStartupHealthTests
{
    private static Task<bool> Run(Func<double,bool> ready,Func<double,bool> exited)
    {
        double seconds=0;
        return UpdateStartupHealth.WaitAsync(()=>ready(seconds),()=>exited(seconds),()=>TimeSpan.FromSeconds(seconds),()=>{seconds+=0.1;return Task.CompletedTask;});
    }
    [Fact]public async Task ReadyThenDelayedCrashIsNotSuccessfulUpdate()=>Assert.False(await Run(_=>true,s=>s>=1));
    [Fact]public async Task ReadyMustRemainAliveForObservationPeriod()=>Assert.True(await Run(s=>s>=3,_=>false));
    [Fact]public async Task AliveWithoutHandshakeTimesOut()=>Assert.False(await Run(_=>false,_=>false));
    [Fact]public async Task ExitWinsEvenWhenMarkerAlreadyExists()=>Assert.False(await Run(_=>true,_=>true));
}
