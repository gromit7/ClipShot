using ClipShot;
using Microsoft.Data.Sqlite;
using Xunit;

namespace ClipShot.Checks;
public sealed class StoreTests : IDisposable
{
    private readonly string folder=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));
    private string Database=>Path.Combine(folder,"test;name.db");
    private Store Open()=>new(Database);
    public void Dispose(){if(Directory.Exists(folder))Directory.Delete(folder,true);}
    private void Age(string text,int days)
    {
        using var db=new SqliteConnection(new SqliteConnectionStringBuilder{DataSource=Database,Pooling=false}.ToString());db.Open();
        using var command=db.CreateCommand();command.CommandText="UPDATE clips SET seen=$date WHERE text=$text";
        command.Parameters.AddWithValue("$text",text);command.Parameters.AddWithValue("$date",DateTime.UtcNow.AddDays(-days).ToString("O"));command.ExecuteNonQuery();
    }
    [Theory][InlineData("ИНН 1234567890")][InlineData("O'Reilly; DROP TABLE clips;--")][InlineData("😀\nстрока\r\n日本語")][InlineData("  whitespace  ")]
    public void Text_roundtrips_exactly(string text){using var db=Open();db.Add("text",text,"<b>html</b>",@"{\rtf1 rtf}");var clip=Assert.Single(db.All());Assert.Equal(text,clip.Text);Assert.Equal("<b>html</b>",clip.Html);Assert.Equal(@"{\rtf1 rtf}",clip.Rtf);}
    [Fact] public void Long_text_is_not_truncated(){using var db=Open();string text=new('Ж',150_000);db.Add("text",text);Assert.Equal(text,Assert.Single(db.All()).Text);}
    [Fact] public void Duplicate_moves_to_top_without_losing_identity_template_or_name(){using var db=Open();db.Add("text","one");long id=Assert.Single(db.All()).Id;db.Template(id,true);db.Edit(id,"name");Age("one",5);db.Add("text","two");db.Add("text","one","new html","new rtf");Assert.Equal(2,db.All().Count);var first=db.All()[0];Assert.Equal(id,first.Id);Assert.True(first.Template);Assert.Equal("name",first.Name);Assert.Equal("new html",first.Html);Assert.Equal("new rtf",first.Rtf);}
    [Fact] public void Image_bytes_roundtrip_and_deduplicate(){using var db=Open();byte[] image=[0,1,2,255];db.Add("image","",image:image);db.Add("image","",image:image);Assert.Equal(image,Assert.Single(db.All()).Image);db.Add("image","",image:[0,1,3,255]);Assert.Equal(2,db.All().Count);}
    [Fact] public void Text_and_image_hashes_have_separate_namespaces(){using var db=Open();db.Add("text","abc");db.Add("image","",image:System.Text.Encoding.UTF8.GetBytes("abc"));Assert.Equal(2,db.All().Count);}
    [Fact] public void Clear_keeps_templates_and_settings(){using var db=Open();db.Add("text","template");db.Template(db.All()[0].Id,true);db.Add("text","history");db.Set("theme","Dark");db.Clear();db.Clear();Assert.True(Assert.Single(db.All()).Template);Assert.Equal("Dark",db.Get("theme",""));}
    [Fact] public void Delete_only_removes_selected_record(){using var db=Open();db.Add("text","one");long id=db.All()[0].Id;db.Add("text","two");db.Delete(id);db.Delete(id);Assert.Equal("two",Assert.Single(db.All()).Text);}
    [Fact] public void Removing_template_flag_allows_clear(){using var db=Open();db.Add("text","one");long id=db.All()[0].Id;db.Template(id,true);db.Template(id,false);db.Clear();Assert.Empty(db.All());}
    [Fact] public void Editing_template_clears_stale_formatting_and_updates_deduplication(){using var db=Open();db.Add("text","old","html","rtf");long id=db.All()[0].Id;db.Template(id,true);db.EditText(id,"new name","new");db.Add("text","new");var clip=Assert.Single(db.All());Assert.Equal(id,clip.Id);Assert.True(clip.Template);Assert.Equal("new name",clip.Name);Assert.Empty(clip.Html);Assert.Empty(clip.Rtf);}
    [Fact] public void Editing_to_duplicate_is_atomic(){using var db=Open();db.Add("text","one","html","rtf");long id=db.All()[0].Id;db.Edit(id,"original");db.Add("text","two");Assert.Throws<SqliteException>(()=>db.EditText(id,"changed","two"));var unchanged=db.All().Single(c=>c.Id==id);Assert.Equal("one",unchanged.Text);Assert.Equal("original",unchanged.Name);Assert.Equal("html",unchanged.Html);}
    [Fact] public void Settings_are_upserted_and_parameterized(){using var db=Open();Assert.Equal("default",db.Get("missing","default"));db.Set("key';--","first");db.Set("key';--","значение ' ;");Assert.Equal("значение ' ;",db.Get("key';--",""));db.Set("empty","");Assert.Equal("",db.Get("empty","fallback"));}
    [Fact] public void Settings_batch_commits_together(){using var db=Open();db.SetMany(new Dictionary<string,string>{{"key","112"},{"shotkey","113"}});Assert.Equal("112",db.Get("key",""));Assert.Equal("113",db.Get("shotkey",""));}
    [Fact] public void Settings_batch_rolls_back_on_failure(){using var db=Open();db.Set("key","old");IEnumerable<KeyValuePair<string,string>> Broken(){yield return new("key","new");throw new InvalidOperationException("test");}Assert.Throws<InvalidOperationException>(()=>db.SetMany(Broken()));Assert.Equal("old",db.Get("key",""));}
    [Theory][InlineData(30)][InlineData(90)] public void Expiry_preserves_templates_and_recent_history(int days){using var db=Open();db.Add("text","old");db.Add("text","recent");db.Add("text","template");db.Template(db.All().Single(c=>c.Text=="template").Id,true);Age("old",days+1);Age("recent",days-1);Age("template",1000);db.Prune(days);Assert.Equal(2,db.All().Count);Assert.Contains(db.All(),c=>c.Template);Assert.Contains(db.All(),c=>c.Text=="recent");}
    [Theory][InlineData(0)][InlineData(-1)] public void Unlimited_retention_keeps_old_records(int days){using var db=Open();db.Add("text","old");Age("old",1000);db.Prune(days);Assert.Single(db.All());}
    [Fact] public void Reopening_is_idempotent_and_preserves_all_user_data(){long id;using(var db=Open()){db.Add("text","survive","html","rtf");id=db.All()[0].Id;db.Template(id,true);db.Edit(id,"template name");db.Set("templateorder",id.ToString());db.Set("theme","Dark");}for(int i=0;i<3;i++){using var db=Open();var clip=Assert.Single(db.All());Assert.Equal(id,clip.Id);Assert.Equal("survive",clip.Text);Assert.True(clip.Template);Assert.Equal("template name",clip.Name);Assert.Equal("html",clip.Html);Assert.Equal("Dark",db.Get("theme",""));Assert.Equal(id.ToString(),db.Get("templateorder",""));}}
    [Fact] public void Corrupt_database_is_not_silently_replaced(){Directory.CreateDirectory(folder);byte[] original=System.Text.Encoding.UTF8.GetBytes("not a SQLite database");File.WriteAllBytes(Database,original);Assert.Throws<SqliteException>(()=>{using var db=Open();});Assert.Equal(original,File.ReadAllBytes(Database));}
    [Fact] public void Thousands_of_clips_remain_unique_and_readable(){using var db=Open();for(int i=0;i<1200;i++)db.Add("text","record "+i);db.Add("text","record 50");Assert.Equal(1200,db.All().Count);Assert.Equal("record 50",db.All()[0].Text);}
    [Fact] public void Label_prefers_name_and_flattens_line_breaks(){var clip=new Clip(1,"text","a\r\nb","","",null,false,"",DateTime.Now);Assert.Equal("a  b",clip.Label);Assert.Equal("custom",(clip with{Name="custom"}).Label);Assert.Equal("Изображение",(clip with{Kind="image"}).Label);}
}
