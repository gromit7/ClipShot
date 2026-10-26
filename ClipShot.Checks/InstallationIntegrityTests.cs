using System.Security.Cryptography;
using System.Text.Json;
using Xunit;
namespace ClipShot;
public class InstallationIntegrityTests
{
    private static string Fixture()
    {
        string root=Path.Combine(Path.GetTempPath(),"ClipShot.Tests",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
        var hashes=new Dictionary<string,string>();
        foreach(string name in new[]{"ClipShot.exe","ClipShot.dll","ClipShot.pri"}){byte[] bytes=System.Text.Encoding.UTF8.GetBytes("synthetic "+name);File.WriteAllBytes(Path.Combine(root,name),bytes);hashes[name]=Convert.ToHexString(SHA256.HashData(bytes));}
        File.WriteAllText(Path.Combine(root,"FileHashes.json"),JsonSerializer.Serialize(hashes));return root;
    }
    [Fact]public void CompleteConsistentInstallationPasses()=>Assert.True(InstallationIntegrity.Verify(Fixture()));
    [Fact]public void OldDllBesideNewExeAndPriFails(){string root=Fixture();File.WriteAllText(Path.Combine(root,"ClipShot.dll"),"synthetic previous release");Assert.False(InstallationIntegrity.Verify(root));}
    [Fact]public void MissingResourceFails(){string root=Fixture();File.Delete(Path.Combine(root,"ClipShot.pri"));Assert.False(InstallationIntegrity.Verify(root));}
    [Fact]public void ManifestCannotSkipCriticalFiles(){string root=Fixture();File.WriteAllText(Path.Combine(root,"FileHashes.json"),"{}");Assert.False(InstallationIntegrity.Verify(root));}
    [Fact]public void ManifestCannotOmitOnlyTheDllWhileOtherHashesRemainValid(){string root=Fixture();string path=Path.Combine(root,"FileHashes.json");var hashes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(path))!;hashes.Remove("ClipShot.dll");File.WriteAllText(path,JsonSerializer.Serialize(hashes));Assert.False(InstallationIntegrity.Verify(root));}
    [Fact]public void InvalidManifestFails(){string root=Fixture();File.WriteAllText(Path.Combine(root,"FileHashes.json"),"invalid");Assert.False(InstallationIntegrity.Verify(root));}
}
