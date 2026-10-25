using System.Security.Cryptography;
using System.Text.Json;
namespace ClipShot;
public static class InstallationIntegrity
{
    public static bool Verify(string directory)
    {
        try
        {
            string root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar;
            string manifest=Path.Combine(root,"FileHashes.json");
            if(new FileInfo(manifest).Length>1048576)return false;
            var hashes=JsonSerializer.Deserialize<Dictionary<string,string>>(File.ReadAllText(manifest));
            if(hashes is null||!hashes.ContainsKey("ClipShot.exe")||!hashes.ContainsKey("ClipShot.dll")||!hashes.ContainsKey("ClipShot.pri"))return false;
            foreach(var entry in hashes)
            {
                string path=Path.GetFullPath(Path.Combine(root,entry.Key));
                if(!path.StartsWith(root,StringComparison.OrdinalIgnoreCase)||!File.Exists(path))return false;
                using var file=File.OpenRead(path);
                if(!Convert.ToHexString(SHA256.HashData(file)).Equals(entry.Value,StringComparison.OrdinalIgnoreCase))return false;
            }
            return true;
        }
        catch{return false;}
    }
}
