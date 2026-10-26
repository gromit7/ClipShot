using Xunit;
namespace ClipShot;

public class UpdateScheduleTests
{
    private static readonly TimeSpan Idle=TimeSpan.FromMinutes(5);
    private static DateTime Monday(int hour)=>new(2026,10,26,hour,0,0);
    [Theory][InlineData(0)][InlineData(8)][InlineData(9)][InlineData(15)][InlineData(23)]
    public void DailyCheckHasNoFixedHour(int hour)=>Assert.Equal("20261026",UpdateSchedule.GetDueSlot(Monday(hour),null,Idle));
    [Theory][InlineData(24)][InlineData(25)]
    public void WeekendAlsoChecksWhenIdle(int day)=>Assert.Equal("202610"+day,UpdateSchedule.GetDueSlot(new(2026,10,day),"20261023-15",Idle));
    [Fact]public void UnknownInactivityDefersCheck()=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(12),null,null));
    [Fact]public void ActiveUserDefersCheck()=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(12),null,TimeSpan.FromMinutes(5)-TimeSpan.FromMilliseconds(1)));
    [Theory][InlineData("20261026")][InlineData("20261026-09")][InlineData("20261026-15")]
    public void SuccessfulCheckTodayPreventsSecondBackgroundRequest(string previous)=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(23),previous,Idle));
    [Fact]public void MissedDaysProduceOneCurrentCheck()=>Assert.Equal("20261026",UpdateSchedule.GetDueSlot(Monday(12),"20261020",Idle));
    [Fact]public void NextDayIsDue()=>Assert.Equal("20261027",UpdateSchedule.GetDueSlot(new(2026,10,27),"20261026",Idle));
    [Theory][InlineData("20261027")][InlineData("20261027-09")]
    public void ClockBackDoesNotRepeatOlderDay(string previous)=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(12),previous,Idle));
    [Theory][InlineData("")][InlineData("broken")][InlineData("99999999")][InlineData("20261026-12")]
    public void InvalidSavedValueCannotDisableChecks(string previous)=>Assert.Equal("20261026",UpdateSchedule.GetDueSlot(Monday(12),previous,Idle));
}
