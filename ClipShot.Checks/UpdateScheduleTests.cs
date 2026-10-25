using Xunit;
namespace ClipShot;

public class UpdateScheduleTests
{
    private static DateTime Monday(int hour,int minute=0)=>new(2026,10,26,hour,minute,0);
    [Theory][InlineData(0,0)][InlineData(8,59)]
    public void BeforeMorningSlotWaitsUntilNine(int hour,int minute)=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(hour,minute),"20261023-15"));
    [Theory][InlineData(9,0)][InlineData(14,59)]
    public void MorningSlotIsDueFromNine(int hour,int minute)=>Assert.Equal("20261026-09",UpdateSchedule.GetDueSlot(Monday(hour,minute),null));
    [Theory][InlineData(15,0)][InlineData(23,59)]
    public void MissedChecksCollapseIntoLatestAfternoonSlot(int hour,int minute)=>Assert.Equal("20261026-15",UpdateSchedule.GetDueSlot(Monday(hour,minute),"20261022-09"));
    [Fact]public void RestartDoesNotRepeatMorningSlot()=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(14),"20261026-09"));
    [Fact]public void AfternoonSlotStillRunsAfterMorning()=>Assert.Equal("20261026-15",UpdateSchedule.GetDueSlot(Monday(15),"20261026-09"));
    [Fact]public void RestartDoesNotRepeatAfternoonSlot()=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(20),"20261026-15"));
    [Theory][InlineData(24)][InlineData(25)]
    public void WeekendNeverChecksEvenAfterMissedWeekday(int day)=>Assert.Null(UpdateSchedule.GetDueSlot(new(2026,10,day,16,0,0),"20261023-09"));
    [Fact]public void NewWeekdayGetsNewSlot()=>Assert.Equal("20261027-09",UpdateSchedule.GetDueSlot(new(2026,10,27,9,0,0),"20261026-15"));
    [Fact]public void MovingClockBackWithinDayDoesNotRepeatMorning()=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(10),"20261026-15"));
    [Fact]public void MovingClockBackAcrossDaysDoesNotRepeatOlderSlots()=>Assert.Null(UpdateSchedule.GetDueSlot(Monday(16),"20261027-09"));
    [Fact]public void CalendarCatchingUpResumesAtNextUnseenSlot()=>Assert.Equal("20261027-15",UpdateSchedule.GetDueSlot(new(2026,10,27,15,0,0),"20261027-09"));
    [Theory][InlineData("")][InlineData("broken")][InlineData("99999999-15")][InlineData("20261026-12")]
    public void InvalidPersistedValueCannotDisableFutureChecks(string value)=>Assert.Equal("20261026-09",UpdateSchedule.GetDueSlot(Monday(9),value));
}
