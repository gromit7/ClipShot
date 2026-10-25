namespace ClipShot;

// Values can come from an older release or a manually edited database.
public static class AppSettings
{
    public static int WindowWidth(string value) => Number(value, 520, 400, 4096);
    public static int WindowHeight(string value) => Number(value, 680, 400, 4096);
    public static int RetentionDays(string value) => value switch { "0" => 0, "30" => 30, _ => 90 };
    public static uint FunctionKey(string value, uint fallback) => uint.TryParse(value, out var key) && key is >= 112 and <= 123 ? key : fallback;
    private static int Number(string value, int fallback, int min, int max) => int.TryParse(value, out int number) ? Math.Clamp(number, min, max) : fallback;
}
