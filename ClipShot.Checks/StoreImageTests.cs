using Microsoft.Data.Sqlite;
using System.Security.Cryptography;
using Xunit;
namespace ClipShot.Checks;

public sealed class StoreImageTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests","StoreImage-"+Guid.NewGuid().ToString("N"));
    private string Database=>Path.Combine(folder,"images.db");
    private Store Open()=>new(Database);
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
    private (string Hash,string Seen) State(long id)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText="SELECT hash,seen FROM clips WHERE id=$id";command.Parameters.AddWithValue("$id",id);
        using var reader=command.ExecuteReader();Assert.True(reader.Read());return(reader.GetString(0),reader.GetString(1));
    }
    private void Age(long id)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText="UPDATE clips SET seen=$seen WHERE id=$id";
        command.Parameters.AddWithValue("$seen",DateTime.UtcNow.AddDays(-2).ToString("O"));command.Parameters.AddWithValue("$id",id);command.ExecuteNonQuery();
    }
    private static void AssertSameClip(Clip before,Clip after)
    {
        Assert.Equal(before with{Image=null},after with{Image=null});Assert.Equal(before.Image,after.Image);
    }

    [Fact]public void Edited_image_keeps_id_and_updates_blob_hash_and_seen()
    {
        using var store=Open();store.Add("image","",image:[1,2,3]);long id=Assert.Single(store.All()).Id;Age(id);
        var old=State(id);byte[] edited=[3,4,5,255];var before=DateTime.UtcNow;
        store.EditImage(id,edited);var after=DateTime.UtcNow;var clip=Assert.Single(store.All());var state=State(id);
        Assert.Equal(id,clip.Id);Assert.Equal("image",clip.Kind);Assert.Equal(edited,clip.Image);
        Assert.NotEqual(old.Hash,state.Hash);Assert.Equal("image"+Convert.ToHexString(SHA256.HashData(edited)),state.Hash);
        Assert.NotEqual(old.Seen,state.Seen);Assert.InRange(clip.Seen.ToUniversalTime(),before,after);
    }
    [Fact]public void Edited_history_image_keeps_its_name_without_becoming_a_template()
    {
        using var store=Open();store.Add("image","",image:[1,2,3]);long id=Assert.Single(store.All()).Id;
        store.Template(id,false);store.Edit(id,"Инструкция для саппорта");store.EditImage(id,[3,4,5]);
        var clip=Assert.Single(store.All());Assert.Equal(id,clip.Id);Assert.False(clip.Template);Assert.Equal("Инструкция для саппорта",clip.Name);
    }
    [Fact]public void Copying_edited_image_deduplicates_into_the_same_named_template()
    {
        using var store=Open();byte[] original=[1,2,3],edited=[3,4,5];store.Add("image","",image:original);long id=Assert.Single(store.All()).Id;
        store.Template(id,true);store.Edit(id,"Шаблон");store.EditImage(id,edited);store.Add("image","",image:edited);
        var clip=Assert.Single(store.All());Assert.Equal(id,clip.Id);Assert.True(clip.Template);Assert.Equal("Шаблон",clip.Name);Assert.Equal(edited,clip.Image);
        // Старый хеш больше не занят исходной записью: возвращение исходной картинки создаёт другой клип.
        store.Add("image","",image:original);Assert.Equal(2,store.All().Count);Assert.NotEqual(id,store.All().Single(c=>c.Image!.SequenceEqual(original)).Id);
    }
    [Fact]
    public void Duplicate_hash_conflict_preserves_both_images_names_templates_and_timestamps()
    {
        using var store=Open();byte[] first=[1,2,3],second=[4,5,6];store.Add("image","",image:first);long firstId=Assert.Single(store.All()).Id;
        store.Add("image","",image:second);long secondId=store.All().Single(c=>c.Id!=firstId).Id;
        store.Template(firstId,true);store.Template(secondId,false);store.Edit(firstId,"Первое имя");store.Edit(secondId,"Второе имя");Age(firstId);Age(secondId);
        var before=store.All().OrderBy(c=>c.Id).ToArray();var firstState=State(firstId);var secondState=State(secondId);
        var error=Assert.Throws<SqliteException>(()=>store.EditImage(firstId,second));Assert.Equal(19,error.SqliteErrorCode);
        var after=store.All().OrderBy(c=>c.Id).ToArray();Assert.Equal(2,after.Length);
        AssertSameClip(before[0],after[0]);AssertSameClip(before[1],after[1]);Assert.Equal(firstState,State(firstId));Assert.Equal(secondState,State(secondId));
    }
    [Fact]public void Missing_id_does_not_create_a_clip()
    {
        using var store=Open();store.Add("image","",image:[1,2,3]);var before=Assert.Single(store.All());
        Assert.Throws<InvalidOperationException>(()=>store.EditImage(long.MaxValue,[4,5,6]));AssertSameClip(before,Assert.Single(store.All()));
    }
    [Fact]public void Text_clip_is_not_converted_to_an_image()
    {
        using var store=Open();store.Add("text","Шаблон текста","<b>текст</b>","rtf");long id=Assert.Single(store.All()).Id;store.Template(id,true);store.Edit(id,"Имя");
        var before=Assert.Single(store.All());var state=State(id);
        Assert.Throws<InvalidOperationException>(()=>store.EditImage(id,[1,2,3]));AssertSameClip(before,Assert.Single(store.All()));Assert.Equal(state,State(id));
    }
    [Fact]public void Null_image_is_rejected_without_changing_the_clip()
    {
        using var store=Open();store.Add("image","",image:[1,2,3]);var before=Assert.Single(store.All());var state=State(before.Id);
        Assert.Throws<ArgumentNullException>(()=>store.EditImage(before.Id,null!));AssertSameClip(before,Assert.Single(store.All()));Assert.Equal(state,State(before.Id));
    }
    [Fact]public void Empty_image_is_rejected_without_changing_the_clip()
    {
        using var store=Open();store.Add("image","",image:[1,2,3]);var before=Assert.Single(store.All());var state=State(before.Id);
        Assert.Throws<ArgumentException>(()=>store.EditImage(before.Id,[]));AssertSameClip(before,Assert.Single(store.All()));Assert.Equal(state,State(before.Id));
    }
    [Fact]public void Saving_unchanged_image_is_valid_and_refreshes_seen()
    {
        using var store=Open();byte[] image=[1,2,3];store.Add("image","",image:image);long id=Assert.Single(store.All()).Id;Age(id);var old=State(id);
        store.EditImage(id,image);var clip=Assert.Single(store.All());var state=State(id);
        Assert.Equal(id,clip.Id);Assert.Equal(image,clip.Image);Assert.Equal(old.Hash,state.Hash);Assert.NotEqual(old.Seen,state.Seen);
    }
}
