using System.Text.Json;
using Xunit;
namespace ClipShot;

public class ReleaseHistoryTests : TempWorkspaceOwner
{
    private static string Document(params string[] versions)=>JsonSerializer.Serialize(new{schema=1,releases=versions.Select(version=>new{version,changes=new[]{"Изменение интерфейса","Улучшена установка","Сохранены настройки"}})});
    [Fact]public void CurrentAndFourPreviousReleasesAreShownNewestFirst()
    {
        var history=ReleaseHistory.Parse(Document("0.7.7","0.7.2","0.7.6","0.7.3","0.7.0","0.7.5","0.7.4"),new(0,7,6,0));
        Assert.Equal(new[]{"0.7.6","0.7.5","0.7.4","0.7.3","0.7.2"},history.Select(entry=>entry.Version.ToString()));
        Assert.All(history,entry=>Assert.Equal(3,entry.Changes.Count));
    }
    [Theory][InlineData("{\"schema\":2,\"releases\":[]}")][InlineData("not json")]
    [InlineData("{\"schema\":1,\"releases\":[{\"version\":\"0.7.6.0\",\"changes\":[\"Изменение\"]}]}")]
    [InlineData("{\"schema\":1,\"releases\":[{\"version\":\"0.7.6\",\"changes\":[\"1\",\"2\",\"3\",\"4\"]}]}")]
    public void InvalidHistoryCannotReachTheNotice(string json)=>Assert.Throws<InvalidDataException>(()=>ReleaseHistory.Parse(json,new(0,7,6)));
    [Fact]public void DuplicateVersionsAndOversizedTextAreRejected()
    {
        Assert.Throws<InvalidDataException>(()=>ReleaseHistory.Parse(Document("0.7.6","0.7.6"),new(0,7,6)));
        string json=JsonSerializer.Serialize(new{schema=1,releases=new[]{new{version="0.7.6",changes=new[]{new string('я',161)}}}});
        Assert.Throws<InvalidDataException>(()=>ReleaseHistory.Parse(json,new(0,7,6)));
        Assert.Throws<InvalidDataException>(()=>ReleaseHistory.Parse(new string('я',17000),new(0,7,6)));
    }
    [Fact]public void MissingOrDamagedFileUsesTheQuietFallback()
    {
        string root=NewTempRoot();Directory.CreateDirectory(root);string file=Path.Combine(root,"history.json");
        try
        {
            Assert.Empty(ReleaseHistory.Load(file,new(0,7,6)));
            File.WriteAllText(file,"not json");Assert.Empty(ReleaseHistory.Load(file,new(0,7,6)));
            File.WriteAllText(file,Document("0.7.6"));Assert.Single(ReleaseHistory.Load(file,new(0,7,6)));
        }
        finally{File.Delete(file);DeleteTempRoot(root);}
    }
    [Theory][InlineData(1080,1)][InlineData(768,1)][InlineData(1024,2)][InlineData(180,2)]
    public void NoticeFitsTheWorkAreaAtDifferentDpi(int height,double scale)
    {
        double actual=ReleaseHistoryLayout.HeightForWorkArea(height,scale);
        Assert.InRange(actual,1,392);Assert.True(actual*scale<=height-24);
    }
}
