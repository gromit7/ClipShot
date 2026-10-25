namespace ClipShot;
public static class Win32Hotkey
{
    public const uint Alt=0x0001,Control=0x0002,Shift=0x0004,NoRepeat=0x4000,PrintScreen=0x2c;
    public static uint Modifiers(bool control)=>NoRepeat|Alt|(control?Control:0);
    public static string Describe(uint key,bool control)=>(control?"Ctrl+Alt+":"Alt+")+"F"+(key-111);
}
