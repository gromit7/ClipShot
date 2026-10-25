using Xunit;
namespace ClipShot;
public class MicrophoneTests
{
    private sealed class Backend : IMicrophoneBackend
    {
        internal List<MicrophoneDevice> Devices=[new("a","Built-in",false,true),new("b","USB",true,false)];
        internal List<(string,bool)> Writes=[];internal bool FailRead,FailWrite,IgnoreWrite,Disconnect;
        public IReadOnlyList<MicrophoneDevice> Read()=>FailRead?throw new InvalidOperationException():Devices.ToArray();
        public void SetMute(string id,bool mute)
        {
            Writes.Add((id,mute));if(FailWrite)throw new InvalidOperationException();
            if(Disconnect){Devices.RemoveAll(d=>d.Id==id);return;}
            if(!IgnoreWrite)Devices=Devices.Select(d=>d.Id==id?d with{Muted=mute}:d).ToList();
        }
    }
    [Fact]public void ReadNeverChangesMute(){var b=new Backend();var r=new MicrophoneController(b).Read("default",[]);Assert.Equal(MicrophoneState.Enabled,r.State);Assert.Empty(b.Writes);Assert.Single(r.Devices);}
    [Fact]public void DefaultOnlyChangesDefault(){var b=new Backend();var r=new MicrophoneController(b).Toggle("default",[]);Assert.True(r.Success);Assert.Equal(new[]{("a",true)},b.Writes);}
    [Fact]public void MixedMutesAll(){var b=new Backend();var r=new MicrophoneController(b).Toggle("all",[]);Assert.True(r.Success);Assert.Equal(MicrophoneState.Muted,r.State);Assert.All(b.Writes,w=>Assert.True(w.Item2));}
    [Fact]public void MutedUnmutesAll(){var b=new Backend();b.Devices=b.Devices.Select(d=>d with{Muted=true}).ToList();var r=new MicrophoneController(b).Toggle("all",[]);Assert.True(r.Success);Assert.Equal(MicrophoneState.Enabled,r.State);}
    [Fact]public void SelectedOnlyTouchesChosen(){var b=new Backend();var r=new MicrophoneController(b).Toggle("selected",["b"]);Assert.True(r.Success);Assert.Equal(new[]{("b",false)},b.Writes);}
    [Fact]public void MissingSelectionNeverFallsBackToDefault(){var b=new Backend();var r=new MicrophoneController(b).Toggle("selected",["missing"]);Assert.False(r.Success);Assert.Empty(b.Writes);Assert.Equal(MicrophoneState.Unavailable,r.State);}
    [Fact]public void ReadFailureIsUnavailable(){var b=new Backend{FailRead=true};var r=new MicrophoneController(b).Toggle("all",[]);Assert.False(r.Success);Assert.Empty(b.Writes);}
    [Fact]public void WriteFailureIsReported(){var b=new Backend{FailWrite=true};Assert.False(new MicrophoneController(b).Toggle("all",[]).Success);}
    [Fact]public void UnchangedDeviceIsNotReportedAsSuccess(){var b=new Backend{IgnoreWrite=true};Assert.False(new MicrophoneController(b).Toggle("default",[]).Success);}
    [Fact]public void DisconnectDuringToggleIsReported(){var b=new Backend{Disconnect=true};Assert.False(new MicrophoneController(b).Toggle("all",[]).Success);}
    [Fact]public void UnknownModeUsesWindowsDefault(){var b=new Backend();Assert.Equal("a",Assert.Single(new MicrophoneController(b).Read("invalid",[]).Devices).Id);}
    [Fact]public void ChangingWindowsDefaultIsObserved(){var b=new Backend();var c=new MicrophoneController(b);Assert.Equal("a",Assert.Single(c.Read("default",[]).Devices).Id);b.Devices=b.Devices.Select(d=>d with{IsDefault=d.Id=="b"}).ToList();Assert.Equal("b",Assert.Single(c.Read("default",[]).Devices).Id);}
    [Fact]public void EmptyMachineHasNoTargets(){var b=new Backend();b.Devices.Clear();Assert.Equal(MicrophoneState.Unavailable,new MicrophoneController(b).Read("all",[]).State);}
    // Read-only hardware integration. Empty is valid on a PC without a microphone.
    [Fact]public void CoreAudioEnumeratesCaptureEndpointsWithoutWriting(){var devices=new CoreAudioMicrophones().Read();Assert.Equal(devices.Count,devices.Select(d=>d.Id).Distinct().Count());Assert.All(devices,d=>Assert.False(string.IsNullOrWhiteSpace(d.Id)));Assert.True(devices.Count(d=>d.IsDefault)<=1);}
}
