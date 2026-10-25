namespace ClipShot;

internal enum MicrophoneState { Unavailable, Enabled, Muted, Mixed }
internal record MicrophoneDevice(string Id,string Name,bool Muted,bool IsDefault);
internal interface IMicrophoneBackend
{
    IReadOnlyList<MicrophoneDevice> Read();
    void SetMute(string id,bool muted);
}
internal record MicrophoneResult(MicrophoneState State,IReadOnlyList<MicrophoneDevice> Devices,bool Success);
internal sealed class MicrophoneController(IMicrophoneBackend backend)
{
    internal static IReadOnlyList<MicrophoneDevice> Select(IReadOnlyList<MicrophoneDevice> devices,string mode,IEnumerable<string> selected)
    {
        var ids=selected.ToHashSet(StringComparer.Ordinal);
        return devices.Where(d=>mode=="all" || (mode=="selected"?ids.Contains(d.Id):d.IsDefault)).ToArray();
    }
    internal static MicrophoneState State(IReadOnlyList<MicrophoneDevice> devices)=>devices.Count==0?MicrophoneState.Unavailable:
        devices.All(d=>d.Muted)?MicrophoneState.Muted:devices.All(d=>!d.Muted)?MicrophoneState.Enabled:MicrophoneState.Mixed;
    internal MicrophoneResult Read(string mode,IEnumerable<string> selected)
    {
        try {var devices=Select(backend.Read(),mode,selected);return new(State(devices),devices,true);}
        catch{return new(MicrophoneState.Unavailable,[],false);}
    }
    internal MicrophoneResult Toggle(string mode,IEnumerable<string> selected)
    {
        var ids=selected.ToArray();var before=Read(mode,ids);
        if(before.Devices.Count==0)return before with {Success=false};
        bool mute=before.State!=MicrophoneState.Muted,ok=true;
        foreach(var device in before.Devices)
            try{backend.SetMute(device.Id,mute);}catch{ok=false;}
        // Verify each original endpoint, including one disconnected during the operation.
        var after=Read(mode,ids);
        ok&=after.Success && before.Devices.All(d=>after.Devices.Any(a=>a.Id==d.Id && a.Muted==mute));
        return after with {Success=ok};
    }
}
