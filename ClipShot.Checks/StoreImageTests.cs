using Microsoft.Data.Sqlite;
using Xunit;
namespace ClipShot.Checks;

public sealed class StoreImageTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests","StoreImage-"+Guid.NewGuid().ToString("N"));
    private string Database=>Path.Combine(folder,"images.db");
    private Store Open()=>new(Database);
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
    private long Add(Store store,byte[] bytes){store.Add("image","",image:bytes);return store.All().Single(c=>c.Image is not null&&c.Image.SequenceEqual(bytes)).Id;}
    private static void Same(Clip before,Clip after){Assert.Equal(before with{Image=null},after with{Image=null});Assert.Equal(before.Image,after.Image);}

    [Fact]public void Edited_copy_survives_reopen_without_changing_original_metadata()
    {
        Clip original;long edited;
        using(var store=Open())
        {
            long id=Add(store,[1,2,3]);store.Edit(id,"Исходник");store.Template(id,true);original=store.All().Single();
            edited=store.AddEditedImage(id,[4,5,6]);Assert.NotEqual(id,edited);
            Same(original,store.All().Single(c=>c.Id==id));
        }
        using var reopened=Open();Assert.Equal(2,reopened.All().Count);Same(original,reopened.All().Single(c=>c.Id==original.Id));
        var copy=reopened.All().Single(c=>c.Id==edited);Assert.Equal(new byte[]{4,5,6},copy.Image);Assert.Equal(original.Id,copy.ImageSourceId);Assert.False(copy.Template);
    }
    [Fact]public void Unchanged_and_repeated_results_do_not_create_duplicates_or_modify_original()
    {
        using var store=Open();long id=Add(store,[1,2,3]);var original=store.All().Single();
        Assert.Equal(id,store.AddEditedImage(id,[1,2,3]));Assert.Single(store.All());Same(original,store.All().Single());
        long edited=store.AddEditedImage(id,[4,5,6]);Assert.Equal(edited,store.AddEditedImage(id,[4,5,6]));
        Assert.Equal(2,store.All().Count);Same(original,store.All().Single(c=>c.Id==id));
        store.Add("image","",image:[4,5,6]);Assert.Equal(2,store.All().Count);
    }
    [Fact]public void Existing_result_keeps_its_name_template_and_relationship()
    {
        using var store=Open();long original=Add(store,[1,2,3]),existing=Add(store,[4,5,6]);store.Template(existing,true);store.Edit(existing,"Существующий шаблон");
        var before=store.All().OrderBy(c=>c.Id).ToArray();Assert.Equal(existing,store.AddEditedImage(original,[4,5,6]));
        var after=store.All().OrderBy(c=>c.Id).ToArray();Assert.Equal(2,after.Length);for(int i=0;i<2;i++)Same(before[i],after[i]);
    }
    [Fact]public void Editing_an_edited_copy_keeps_the_same_root_original()
    {
        using var store=Open();long source=Add(store,[1,2,3]);long first=store.AddEditedImage(source,[4,5,6]);long second=store.AddEditedImage(first,[7,8,9]);
        Assert.Equal(source,store.All().Single(c=>c.Id==second).ImageSourceId);Assert.Equal(3,store.All().Count);
        var group=HistoryView.Groups(store.All(),"","",DateTime.Today).Single(g=>g.Clips.Any(c=>c.Id==source)).Clips;
        Assert.Equal(1,Math.Abs(group.FindIndex(c=>c.Id==source)-group.FindIndex(c=>c.Id==second)));
    }
    [Theory][InlineData(false)][InlineData(true)]public void Invalid_source_never_creates_an_image(bool text)
    {
        using var store=Open();store.Add("text","original","html","rtf");var before=store.All().Single();
        Assert.Throws<InvalidOperationException>(()=>store.AddEditedImage(text?before.Id:long.MaxValue,[4,5,6]));Same(before,store.All().Single());
    }
    [Fact]public void Invalid_bytes_do_not_change_existing_data()
    {
        using var store=Open();long id=Add(store,[1,2,3]);var before=store.All().Single();
        Assert.Throws<ArgumentNullException>(()=>store.AddEditedImage(id,null!));Assert.Throws<ArgumentException>(()=>store.AddEditedImage(id,[]));Same(before,store.All().Single());
    }
    [Fact]public void Deleting_original_keeps_the_copy_independent()
    {
        using var store=Open();long source=Add(store,[1,2,3]);long edited=store.AddEditedImage(source,[4,5,6]);
        store.Delete(source);var remaining=store.All().Single();Assert.Equal(edited,remaining.Id);Assert.Null(remaining.ImageSourceId);
        Assert.Single(HistoryView.Groups(store.All(),"","",DateTime.Today).SelectMany(g=>g.Clips));
    }
    [Fact]public void Deleting_edited_image_cleans_relationship_before_id_reuse()
    {
        using var store=Open();long source=Add(store,[1,2,3]);long edited=store.AddEditedImage(source,[4,5,6]);
        store.Delete(edited);long reused=Add(store,[7,8,9]);Assert.Equal(edited,reused);
        Assert.Null(store.All().Single(c=>c.Id==reused).ImageSourceId);
    }
    [Fact]public void Legacy_schema_gains_relationship_table_without_rewriting_clips()
    {
        Clip original;using(var store=Open()){Add(store,[1,2,3]);original=store.All().Single();}
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString()))
        {db.Open();using var legacy=db.CreateCommand();legacy.CommandText="DROP TRIGGER cleanup_image_edits; DROP TABLE image_edits";legacy.ExecuteNonQuery();}
        using var reopened=Open();Same(original,reopened.All().Single());
        long copy=reopened.AddEditedImage(original.Id,[4,5,6]);Assert.Equal(original.Id,reopened.All().Single(c=>c.Id==copy).ImageSourceId);
    }
    [Fact]public void Yesterday_original_and_today_copy_stay_adjacent_without_rewriting_original_date()
    {
        using var store=Open();long source=Add(store,[1,2,3]);
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString()))
        {
            db.Open();using var age=db.CreateCommand();age.CommandText="UPDATE clips SET seen=$seen WHERE id=$id";age.Parameters.AddWithValue("$seen",DateTime.UtcNow.AddDays(-1).ToString("O"));age.Parameters.AddWithValue("$id",source);age.ExecuteNonQuery();
        }
        var original=store.All().Single();store.Add("text","unrelated");long copy=store.AddEditedImage(source,[4,5,6]);
        var groups=HistoryView.Groups(store.All(),"","",DateTime.Today);var today=groups.Single(g=>g.Title=="Сегодня").Clips;
        Assert.Equal(1,Math.Abs(today.FindIndex(c=>c.Id==source)-today.FindIndex(c=>c.Id==copy)));Same(original,store.All().Single(c=>c.Id==source));
        Assert.DoesNotContain(groups.Single(g=>g.Title=="Вчера").Clips,c=>c.Id==source);
    }
    [Fact]public void Transaction_failure_preserves_original_and_does_not_leave_a_half_saved_copy()
    {
        using var store=Open();long source=Add(store,[1,2,3]);var original=store.All().Single();
        using(var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString()))
        {db.Open();using var fail=db.CreateCommand();fail.CommandText="CREATE TRIGGER reject_edit BEFORE INSERT ON image_edits BEGIN SELECT RAISE(ABORT,'synthetic'); END";fail.ExecuteNonQuery();}
        Assert.Throws<SqliteException>(()=>store.AddEditedImage(source,[4,5,6]));Same(original,store.All().Single());
    }
}
