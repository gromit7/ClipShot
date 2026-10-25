using ClipShot;
using Xunit;
namespace ClipShot.Checks;
public sealed class HistoryTests
{
    private static readonly DateTime Today=new(2026,10,25);
    private static Clip Clip(long id,string text="text",int age=0,bool template=false,string name="",string kind="text")=>new(id,kind,text,"","",null,template,name,Today.AddDays(-age));
    [Theory][InlineData("EXAMPLE")][InlineData("example")][InlineData("  ExAmPlE  ")]
    public void Search_matches_history_and_templates_ignoring_case_and_outer_spaces(string query)
    {var groups=HistoryView.Groups([Clip(1,"test@example.com",template:true),Clip(2,"https://example.com"),Clip(3,"other")],query,"",Today);Assert.Equal(new long[]{1,2},groups.SelectMany(g=>g.Clips).Select(c=>c.Id));}
    [Fact] public void Search_matches_optional_name(){Assert.Single(HistoryView.Groups([Clip(1,name:"РЕКВИЗИТЫ")],"реквизиты","",Today).SelectMany(g=>g.Clips));}
    [Fact] public void Images_are_not_searched_even_by_name(){Assert.Empty(HistoryView.Groups([Clip(1,name:"example",kind:"image")],"example","",Today).SelectMany(g=>g.Clips));}
    [Fact] public void Whitespace_search_keeps_images(){Assert.Single(HistoryView.Groups([Clip(1,kind:"image")],"  ","",Today).SelectMany(g=>g.Clips));}
    [Fact] public void No_match_returns_empty_groups(){Assert.All(HistoryView.Groups([Clip(1)],"missing","",Today),g=>Assert.Empty(g.Clips));}
    [Theory][InlineData(2026,10,19)][InlineData(2026,10,20)][InlineData(2026,10,25)][InlineData(2027,1,1)]
    public void Every_clip_occurs_in_exactly_one_date_group(int year,int month,int day)
    {var date=new DateTime(year,month,day);var clips=Enumerable.Range(-1,20).Select(i=>Clip(i+2) with{Seen=date.AddDays(-i)}).ToList();var groups=HistoryView.Groups(clips,"","",date);Assert.Equal(clips.Count,groups.Sum(g=>g.Clips.Count));Assert.Equal(clips.Count,groups.SelectMany(g=>g.Clips).Select(c=>c.Id).Distinct().Count());Assert.Contains(groups[1].Clips,c=>c.Seen.Date>date);Assert.Single(groups[2].Clips);}
    [Fact] public void Dates_use_local_calendar_days(){var groups=HistoryView.Groups([Clip(1) with{Seen=Today.AddTicks(-1)},Clip(2) with{Seen=Today.AddHours(23)}],"","",Today);Assert.Equal(2,Assert.Single(groups[1].Clips).Id);Assert.Equal(1,Assert.Single(groups[2].Clips).Id);}
    [Fact] public void Monday_yesterday_is_not_duplicated(){var monday=new DateTime(2026,10,19);var groups=HistoryView.Groups([Clip(1) with{Seen=monday.AddDays(-1)},Clip(2) with{Seen=monday.AddDays(-2)}],"","",monday);Assert.Equal(1,Assert.Single(groups[2].Clips).Id);Assert.Empty(groups[3].Clips);Assert.Equal(2,Assert.Single(groups[4].Clips).Id);}
    [Fact] public void Templates_are_first_and_excluded_from_dates(){var groups=HistoryView.Groups([Clip(1,age:500,template:true),Clip(2)],"","",Today);Assert.Equal("Шаблоны",groups[0].Title);Assert.Equal(1,Assert.Single(groups[0].Clips).Id);Assert.DoesNotContain(groups.Skip(1).SelectMany(g=>g.Clips),c=>c.Template);}
    [Fact] public void Template_order_ignores_stale_invalid_and_duplicate_ids(){var groups=HistoryView.Groups([Clip(1,template:true),Clip(2,template:true),Clip(3,template:true)],"","2,2,999,broken,1",Today);Assert.Equal(new long[]{2,1,3},groups[0].Clips.Select(c=>c.Id));}
    [Fact] public void Month_and_older_never_lose_or_duplicate_clips(){var groups=HistoryView.Groups([Clip(1,age:10),Clip(2,age:40)],"","",Today);Assert.Equal("Месяц",groups[4].Title);Assert.Equal(1,Assert.Single(groups[4].Clips).Id);Assert.Equal("Ранее",groups[5].Title);Assert.Equal(2,Assert.Single(groups[5].Clips).Id);}
    [Fact] public void Empty_history_keeps_six_empty_sections(){Assert.Equal(6,HistoryView.Groups([],"","",Today).Count);Assert.All(HistoryView.Groups([],"","",Today),g=>Assert.Empty(g.Clips));}
}
