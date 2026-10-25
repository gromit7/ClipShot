using System.Text;
using System.Text.Json;
namespace ClipShot;

public static class UpdateFeed
{
    public const int MaximumBytes=32*1024;
    public const string Url="https://raw.githubusercontent.com/gromit7/ClipShot/main/update-feed.json";

    // Один документ связывает стабильный выпуск и аварийный откат. Установка всё равно
    // доверяет только проверенному адресу установщика, его размеру и SHA-256.
    public static AppUpdate? Parse(string json,Version current,int? osBuild=null)
    {
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Update feed is too large");
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(root.GetProperty("schema").GetInt32()!=1)throw new InvalidDataException("Unknown update feed schema");
        int build=osBuild??Environment.OSVersion.Version.Build;
        string? flavor=UpdatePackages.FlavorForBuild(build);
        if(flavor is null)return null;
        var stable=root.GetProperty("stable");var rollback=root.GetProperty("rollback");
        if(rollback.ValueKind!=JsonValueKind.Null)
        {
            if(rollback.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid rollback entry");
            var policy=RollbackPolicy.Parse(rollback.GetProperty("policy").GetRawText(),current);
            if(policy is not null)
            {
                var candidate=ParseRelease(rollback,"release",current,flavor,build,allowRollback:true);
                if(candidate is null||candidate.Version!=policy.Target)throw new InvalidDataException("Rollback release mismatch");
                return candidate with{IsRollback=true,Reason=policy.Reason};
            }
        }
        // stable:null отзывает предложение целиком. При сетевой ошибке или отзыве
        // не обращаемся к другой ленте: это могло бы снова предложить плохую версию.
        if(stable.ValueKind==JsonValueKind.Null)return null;
        if(stable.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid stable release");
        return ParseRelease(root,"stable",current,flavor,build);
    }
    private static AppUpdate? ParseRelease(JsonElement container,string legacyProperty,Version current,string flavor,int build,bool allowRollback=false)
    {
        var release=container.GetProperty(legacyProperty);string packageFlavor="legacy";
        if(container.TryGetProperty("variants",out var variants))
        {
            if(variants.ValueKind!=JsonValueKind.Object||!variants.TryGetProperty(flavor,out var selected))throw new InvalidDataException("Missing Windows package variant");
            // null отзывает только этот вариант. Не подменяем его legacy или соседней ОС.
            if(selected.ValueKind==JsonValueKind.Null)return null;
            if(selected.ValueKind!=JsonValueKind.Object||release.ValueKind!=JsonValueKind.Object||
                selected.GetProperty("tag_name").GetString()!=release.GetProperty("tag_name").GetString())throw new InvalidDataException("Package variant release mismatch");
            release=selected;packageFlavor=flavor;
        }
        return UpdateProtocol.Parse(release.GetRawText(),current,allowRollback,packageFlavor,build);
    }
}
