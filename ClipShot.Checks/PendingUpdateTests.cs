using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace ClipShot;
public class PendingUpdateTests
{
    private static (string Root,string File,AppUpdate Update) Package(string version="0.7.9",bool rollback=false)
    {
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        string file=Path.Combine(root,$"ClipShot-Setup-{version}.exe");byte[] bytes=new byte[2048];Random.Shared.NextBytes(bytes);File.WriteAllBytes(file,bytes);
        return(root,file,new(Version.Parse(version),new($"https://github.com/gromit7/ClipShot/releases/download/v{version}/ClipShot-Setup-{version}.exe"),bytes.Length,Convert.ToHexString(SHA256.HashData(bytes)),rollback));
    }
    [Fact]public void VerifiedPackageSurvivesSerializationAndRestart(){var p=Package();var saved=new PendingPackage(p.File,p.Update);Assert.Equal(saved,PendingUpdate.Read(JsonSerializer.Serialize(saved),p.Root,new(0,7,3,0)));}
    [Fact]public void AlreadyInstalledPackageIsCleared(){var p=Package();Assert.Null(PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(p.File,p.Update)),p.Root,new(0,7,9,0)));}
    [Fact]public void NormalOlderPackageNeverDowngrades(){var p=Package();Assert.Null(PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(p.File,p.Update)),p.Root,new(0,8,0)));}
    [Fact]public void CachedRollbackNeedsFreshPolicy(){var p=Package("0.7.2",true);Assert.Null(PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(p.File,p.Update)),p.Root,new(0,7,3)));}
    [Fact]public void ChangedPackageNeverResumes(){var p=Package();File.WriteAllBytes(p.File,new byte[2048]);Assert.Throws<InvalidDataException>(()=>PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(p.File,p.Update)),p.Root,new(0,7,3)));}
    [Fact]public void PackageOutsideUpdateDirectoryNeverResumes(){var p=Package();Assert.Throws<InvalidDataException>(()=>PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(p.File,p.Update)),Path.Combine(p.Root,"other"),new(0,7,3)));}
    [Fact]public void ForeignUrlNeverResumes(){var p=Package();Assert.Throws<InvalidDataException>(()=>PendingUpdate.Read(JsonSerializer.Serialize(new PendingPackage(p.File,p.Update with{Download=new("https://evil.test/setup.exe")})),p.Root,new(0,7,3)));}
    [Fact]public void OversizedMetadataNeverResumes()=>Assert.Throws<InvalidDataException>(()=>PendingUpdate.Read(new string(' ',16385),Path.GetTempPath(),new(0,7,3)));
}
