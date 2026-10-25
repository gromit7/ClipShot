using System.Runtime.InteropServices;

namespace ClipShot;
public sealed partial class MainWindow
{
    private nint previousTarget;
    private void RememberPasteTarget()
    {
        if(isolated)return;
        nint target=Native.GetForegroundWindow();if(target==0)return;
        Native.GetWindowThreadProcessId(target,out uint process);
        if(process!=(uint)Environment.ProcessId)previousTarget=Native.GetAncestor(target,2);
    }
    private bool HasTargetCaret(nint target)
    {
        if(target==0||!Native.IsWindow(target)||Native.IsIconic(target))return false;
        uint thread=Native.GetWindowThreadProcessId(target,out uint process);
        if(process==(uint)Environment.ProcessId)return false;
        var info=new Native.GuiThreadInfo{Size=(uint)Marshal.SizeOf<Native.GuiThreadInfo>()};
        return Native.GetGUIThreadInfo(thread,ref info)&&info.Caret!=0&&info.Focus!=0&&Native.GetAncestor(info.Caret,2)==target&&Native.GetAncestor(info.Focus,2)==target;
    }
    private async Task TryPastePrevious()
    {
        if(isolated||exiting)return;
        nint target=previousTarget;
        if(!HasTargetCaret(target)||!Native.SetForegroundWindow(target))return;
        await Task.Delay(60);
        if(exiting||Native.GetAncestor(Native.GetForegroundWindow(),2)!=target||!HasTargetCaret(target))return;
        if(new[]{0x10,0x11,0x12}.Any(key=>(Native.GetAsyncKeyState(key)&0x8000)!=0))return;
        var inputs=new[]{Native.KeyInput(0x11,false),Native.KeyInput(0x56,false),Native.KeyInput(0x56,true),Native.KeyInput(0x11,true)};
        if(Native.SendInput((uint)inputs.Length,inputs,Marshal.SizeOf<Native.KeyboardInput>())!=inputs.Length)
            Native.SendInput(2,[Native.KeyInput(0x56,true),Native.KeyInput(0x11,true)],Marshal.SizeOf<Native.KeyboardInput>());
    }
}
