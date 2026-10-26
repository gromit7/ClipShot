using System.Globalization;
using System.Text;
using Microsoft.Data.Sqlite;
using Xunit;
namespace ClipShot.Checks;

public sealed class ImportStoreTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests","ImportStore-"+Guid.NewGuid().ToString("N"));
    private string Database=>Path.Combine(folder,"synthetic.db");
    private Store Open()=>new(Database);
    private static DateTime Yesterday=>DateTime.Today.AddDays(-1).AddHours(23).AddMinutes(47);
    private static ClipdiaryImportResult Text(long id,string text,DateTime? seen=null,bool template=false,string name="")=>new(id,new("text",text,null,template,name,seen??Yesterday),null);
    private sealed class InlineProgress(Action<int> report):IProgress<int>{public void Report(int value)=>report(value);}
    private void Sql(string text,params (string Name,object Value)[] parameters)
    {
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false,DefaultTimeout=1}.ToString());connection.Open();
        using var command=connection.CreateCommand();command.CommandText=text;
        foreach(var parameter in parameters)command.Parameters.AddWithValue(parameter.Name,parameter.Value);command.ExecuteNonQuery();
    }
    private DateTime RawSeen()
    {
        using var connection=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString());connection.Open();
        using var command=connection.CreateCommand();command.CommandText="SELECT seen FROM clips LIMIT 1";
        return DateTime.Parse((string)command.ExecuteScalar()!,CultureInfo.InvariantCulture,DateTimeStyles.RoundtripKind);
    }
    [Fact]public void PreviousLocalDayAndRepeatedImportKeepTheOriginalTimestamp()
    {
        using var store=Open();var input=new[]{Text(1,"synthetic dated")};
        Assert.Equal(new ImportSummary(1,0,0),store.Import(input,false,TestContext.Current.CancellationToken));var first=Assert.Single(store.All());
        Assert.Equal(Yesterday,first.Seen);Assert.Equal(Yesterday.ToUniversalTime(),RawSeen());Assert.Equal(DateTimeKind.Utc,RawSeen().Kind);
        Assert.Equal(new ImportSummary(0,1,0),store.Import(input,false,TestContext.Current.CancellationToken));var repeated=Assert.Single(store.All());
        Assert.Equal(first.Id,repeated.Id);Assert.Equal(first.Seen,repeated.Seen);Assert.Equal(DateTime.Today.AddDays(-1),repeated.Seen.Date);
    }
    [Fact]public void ExistingContentFormatsNameAndNewerDateSurviveTemplatePromotion()
    {
        using var store=Open();store.Add("text","synthetic duplicate","<b>original</b>","original rtf");var original=Assert.Single(store.All());store.Edit(original.Id,"existing name");
        var result=store.Import([Text(1,original.Text,template:true,name:"imported name")],false,TestContext.Current.CancellationToken);
        var clip=Assert.Single(store.All());Assert.Equal(new ImportSummary(0,1,0),result);Assert.Equal(original.Id,clip.Id);
        Assert.Equal(original.Text,clip.Text);Assert.Equal(original.Html,clip.Html);Assert.Equal(original.Rtf,clip.Rtf);Assert.Equal("existing name",clip.Name);
        Assert.True(clip.Template);Assert.Equal(original.Seen,clip.Seen);
        store.Import([Text(2,original.Text)],false,TestContext.Current.CancellationToken);Assert.True(Assert.Single(store.All()).Template);
    }
    [Fact]public void SourceDuplicatesFillAnEmptyNameAndCompareRealUtcDates()
    {
        using var store=Open();store.Add("text","synthetic","html","rtf");var original=Assert.Single(store.All());
        var old=DateTime.Today.AddDays(-4).AddHours(23);string oldWithOffset=new DateTimeOffset(old).ToString("O");Sql("UPDATE clips SET seen=$seen",("$seen",oldWithOffset));
        var result=store.Import([Text(1,"synthetic",Yesterday.AddHours(-1),true,"first name"),Text(2,"synthetic",Yesterday,false,"other name"),Text(3,"synthetic",old,false)],false,TestContext.Current.CancellationToken);
        var clip=Assert.Single(store.All());Assert.Equal(new ImportSummary(0,3,0),result);Assert.Equal(original.Id,clip.Id);Assert.True(clip.Template);
        Assert.Equal("first name",clip.Name);Assert.Equal(Yesterday,clip.Seen);Assert.Equal("html",clip.Html);Assert.Equal("rtf",clip.Rtf);Assert.Equal(DateTimeKind.Utc,RawSeen().Kind);
    }
    [Fact]public void TextAndImageHashesStaySeparateAndSkippedItemsAreCounted()
    {
        using var store=Open();byte[] image=Encoding.UTF8.GetBytes("synthetic same bytes");
        ClipdiaryImportResult[] input=[Text(1,"synthetic same bytes"),new(2,new("image","",image,false,"",Yesterday),null),new(3,null,ClipdiaryImportSkipReason.Unsupported),Text(4,"synthetic same bytes")];
        Assert.Equal(new ImportSummary(2,1,1),store.Import(input,false,TestContext.Current.CancellationToken));Assert.Equal(2,store.All().Count);
        Assert.Equal(image,store.All().Single(clip=>clip.Kind=="image").Image);
        Assert.Equal(new ImportSummary(0,3,1),store.Import(input,false,TestContext.Current.CancellationToken));
    }
    [Fact]public void UndatedTemplatesNeverMakeDatedHistoryNewerInEitherOrderOrOnReimport()
    {
        var dated=new DateTime(2020,3,4,17,23,45,DateTimeKind.Local);
        var history=Text(1,"synthetic original date",dated);
        var template=new ClipdiaryImportResult(2,new("text","synthetic original date",null,true,"template name",null),null);
        foreach(bool templateFirst in new[]{false,true})
        {
            using var store=new Store(Path.Combine(folder,templateFirst?"template-first.db":"history-first.db"));
            ClipdiaryImportResult[] batch=templateFirst?[template,history]:[history,template];
            Assert.Equal(new ImportSummary(1,1,0),store.Import(batch,false,TestContext.Current.CancellationToken));
            var first=Assert.Single(store.All());Assert.Equal(dated,first.Seen);Assert.True(first.Template);Assert.Equal("template name",first.Name);
            Assert.Equal(new ImportSummary(0,2,0),store.Import(batch,false,TestContext.Current.CancellationToken));
            Assert.Equal(dated,Assert.Single(store.All()).Seen);
            // Отдельный импорт snippets не содержит исходной даты истории и не
            // вправе заменять её временем запуска импорта.
            Assert.Equal(new ImportSummary(0,1,0),store.Import([template],false,TestContext.Current.CancellationToken));
            var repeated=Assert.Single(store.All());Assert.Equal(first.Id,repeated.Id);Assert.Equal(dated,repeated.Seen);
        }
        using var existingStore=new Store(Path.Combine(folder,"existing-dated.db"));
        existingStore.Import([history],false,TestContext.Current.CancellationToken);var existing=Assert.Single(existingStore.All());Assert.False(existing.Template);
        existingStore.Import([template],false,TestContext.Current.CancellationToken);var promoted=Assert.Single(existingStore.All());
        Assert.Equal(existing.Id,promoted.Id);Assert.Equal(dated,promoted.Seen);Assert.True(promoted.Template);
    }
    [Theory][InlineData(false,"90")][InlineData(true,"0")]
    public void OnlyExplicitKeepAllHistoryChangesRetentionAndUndatedTemplatesUseUtcNow(bool keep,string expected)
    {
        using var store=Open();store.Set("days","90");DateTime before=DateTime.UtcNow;
        store.Import([new(1,new("text","synthetic undated template",null,true,"template",null),null)],keep,TestContext.Current.CancellationToken);DateTime after=DateTime.UtcNow;
        Assert.Equal(expected,store.Get("days","missing"));var clip=Assert.Single(store.All());Assert.True(clip.Template);Assert.InRange(clip.Seen.ToUniversalTime(),before,after);
    }
    [Fact]public void DecoderRunsWithoutHoldingTheTargetWriterLockAndFailureChangesNothing()
    {
        using var store=Open();store.Set("days","90");store.Add("text","existing");var original=Assert.Single(store.All());bool written=false;
        IEnumerable<ClipdiaryImportResult> Broken(){yield return Text(1,"not committed");throw new InvalidDataException("synthetic source failure");}
        var progress=new InlineProgress(_=>{Sql("INSERT INTO settings(key,value) VALUES('during-staging','written')");written=true;});
        Assert.Throws<InvalidDataException>(()=>store.Import(Broken(),true,TestContext.Current.CancellationToken,progress));Assert.True(written);
        Assert.Equal(original,Assert.Single(store.All()));Assert.Equal("90",store.Get("days",""));Assert.Equal("written",store.Get("during-staging",""));
    }
    [Fact]public void CancellationAfterMergeBeforeCommitRollsBackRowsMetadataAndRetention()
    {
        using var store=Open();store.Set("days","90");store.Add("text","existing");var original=Assert.Single(store.All());using var cancellation=CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        var progress=new InlineProgress(value=>{if(value==2)cancellation.Cancel();});
        Assert.Throws<OperationCanceledException>(()=>store.Import([Text(1,"existing",template:true,name:"not committed"),Text(2,"new")],true,cancellation.Token,progress));
        Assert.Equal(original,Assert.Single(store.All()));Assert.Equal("90",store.Get("days",""));
    }
    [Fact]public void TargetMergeErrorRollsBackTheWholeBatch()
    {
        using var store=Open();store.Set("days","90");store.Add("text","existing");var original=Assert.Single(store.All());
        Sql("CREATE TRIGGER reject_synthetic BEFORE INSERT ON clips WHEN NEW.text='blocked' BEGIN SELECT RAISE(ABORT,'synthetic rejection'); END");
        Assert.Throws<SqliteException>(()=>store.Import([Text(1,"new first"),Text(2,"blocked")],true,TestContext.Current.CancellationToken));
        Assert.Equal(original,Assert.Single(store.All()));Assert.Equal("90",store.Get("days",""));
    }
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
}
