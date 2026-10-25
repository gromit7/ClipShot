using System.Text;
using System.Text.Json;
namespace ClipShot;

public static class UpdateFeed
{
    public const int MaximumBytes=32*1024;
    public const string Url="https://raw.githubusercontent.com/gromit7/ClipShot/main/update-feed.json";

    // Один документ связывает стабильный выпуск и аварийный откат. Установка всё равно
    // доверяет только проверенному адресу установщика, его размеру и SHA-256.
    public static AppUpdate? Parse(string json,Version current)
    {
        if(Encoding.UTF8.GetByteCount(json)>MaximumBytes)throw new InvalidDataException("Update feed is too large");
        using var document=JsonDocument.Parse(json);var root=document.RootElement;
        if(root.GetProperty("schema").GetInt32()!=1)throw new InvalidDataException("Unknown update feed schema");
        var stable=root.GetProperty("stable");var rollback=root.GetProperty("rollback");
        if(rollback.ValueKind!=JsonValueKind.Null)
        {
            if(rollback.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid rollback entry");
            var policy=RollbackPolicy.Parse(rollback.GetProperty("policy").GetRawText(),current);
            if(policy is not null)
            {
                var candidate=UpdateProtocol.Parse(rollback.GetProperty("release").GetRawText(),current,allowRollback:true);
                if(candidate is null||candidate.Version!=policy.Target)throw new InvalidDataException("Rollback release mismatch");
                return candidate with{IsRollback=true,Reason=policy.Reason};
            }
        }
        // stable:null отзывает предложение целиком. При сетевой ошибке или отзыве
        // не обращаемся к другой ленте: это могло бы снова предложить плохую версию.
        if(stable.ValueKind==JsonValueKind.Null)return null;
        if(stable.ValueKind!=JsonValueKind.Object)throw new InvalidDataException("Invalid stable release");
        return UpdateProtocol.Parse(stable.GetRawText(),current);
    }
}
