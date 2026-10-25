using System.Runtime.InteropServices;
namespace ClipShot;

// Capture endpoints only. No recording and no changes during enumeration.
internal sealed class CoreAudioMicrophones : IMicrophoneBackend
{
    public IReadOnlyList<MicrophoneDevice> Read()
    {
        var enumerator=(IEnum)new Enumerator();ICollection? collection=null;IDevice? defaultDevice=null;
        try
        {
            string defaultId="";
            int hr=enumerator.GetDefaultAudioEndpoint(1,0,out defaultDevice);
            if(hr<0)hr=enumerator.GetDefaultAudioEndpoint(1,2,out defaultDevice);
            if(hr>=0&&defaultDevice is not null)Check(defaultDevice.GetId(out defaultId));
            Check(enumerator.EnumAudioEndpoints(1,1,out collection));Check(collection.GetCount(out uint count));
            var devices=new List<MicrophoneDevice>();
            for(uint index=0;index<count;index++)
            {
                IDevice? device=null;object? volume=null;IProperties? properties=null;
                try
                {
                    Check(collection.Item(index,out device));Check(device.GetId(out string id));
                    var iid=typeof(IVolume).GUID;Check(device.Activate(ref iid,23,0,out volume));
                    Check(((IVolume)volume).GetMute(out bool muted));string name="Микрофон";
                    if(device.OpenPropertyStore(0,out properties)>=0)
                    {
                        var key=new PropertyKey{Format=new("a45c254e-df1c-4efd-8020-67d146a850e0"),Id=14};
                        Check(properties.GetValue(ref key,out var value));
                        try{if(value.Type==31)name=Marshal.PtrToStringUni(value.Pointer)??name;}finally{PropVariantClear(ref value);}
                    }
                    devices.Add(new(id,name,muted,id==defaultId));
                }
                catch(COMException){/* A removed or inaccessible endpoint must not hide the others. */}
                finally{Release(properties);Release(volume);Release(device);}
            }
            return devices;
        }
        finally{Release(defaultDevice);Release(collection);Release(enumerator);}
    }
    public void SetMute(string id,bool muted)
    {
        var enumerator=(IEnum)new Enumerator();IDevice? device=null;object? volume=null;
        try
        {
            Check(enumerator.GetDevice(id,out device));var iid=typeof(IVolume).GUID;
            Check(device.Activate(ref iid,23,0,out volume));var context=Guid.Empty;
            Check(((IVolume)volume).SetMute(muted,ref context));
            Check(((IVolume)volume).GetMute(out bool actual));if(actual!=muted)throw new COMException("Mute verification failed");
        }
        finally{Release(volume);Release(device);Release(enumerator);}
    }
    private static void Check(int result)=>Marshal.ThrowExceptionForHR(result);
    private static void Release(object? value){if(value is not null&&Marshal.IsComObject(value))Marshal.ReleaseComObject(value);}
    [DllImport("ole32.dll")]private static extern int PropVariantClear(ref PropertyValue value);
    [StructLayout(LayoutKind.Sequential)]internal struct PropertyKey {public Guid Format;public uint Id;}
    [StructLayout(LayoutKind.Explicit,Size=24)]internal struct PropertyValue{[FieldOffset(0)]public ushort Type;[FieldOffset(8)]public nint Pointer;}
    [ComImport,Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]private class Enumerator{}
    [ComImport,Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IEnum
    {
        [PreserveSig]int EnumAudioEndpoints(int flow,uint state,out ICollection devices);
        [PreserveSig]int GetDefaultAudioEndpoint(int flow,int role,out IDevice device);
        [PreserveSig]int GetDevice([MarshalAs(UnmanagedType.LPWStr)]string id,out IDevice device);
    }
    [ComImport,Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface ICollection
    {[PreserveSig]int GetCount(out uint count);[PreserveSig]int Item(uint index,out IDevice device);}
    [ComImport,Guid("D666063F-1587-4E43-81F1-B948E807363F"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IDevice
    {
        [PreserveSig]int Activate(ref Guid iid,int context,nint activation,[MarshalAs(UnmanagedType.IUnknown)]out object volume);
        [PreserveSig]int OpenPropertyStore(int access,out IProperties properties);
        [PreserveSig]int GetId([MarshalAs(UnmanagedType.LPWStr)]out string id);
        [PreserveSig]int GetState(out uint state);
    }
    [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IProperties
    {
        [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint index,out PropertyKey key);
        [PreserveSig]int GetValue(ref PropertyKey key,out PropertyValue value);
    }
    [ComImport,Guid("5CDF2C82-841E-4546-9722-0CF74078229A"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]private interface IVolume
    {
        [PreserveSig]int RegisterControlChangeNotify(nint callback);[PreserveSig]int UnregisterControlChangeNotify(nint callback);
        [PreserveSig]int GetChannelCount(out uint count);
        [PreserveSig]int SetMasterVolumeLevel(float value,ref Guid context);[PreserveSig]int SetMasterVolumeLevelScalar(float value,ref Guid context);
        [PreserveSig]int GetMasterVolumeLevel(out float value);[PreserveSig]int GetMasterVolumeLevelScalar(out float value);
        [PreserveSig]int SetChannelVolumeLevel(uint channel,float value,ref Guid context);[PreserveSig]int SetChannelVolumeLevelScalar(uint channel,float value,ref Guid context);
        [PreserveSig]int GetChannelVolumeLevel(uint channel,out float value);[PreserveSig]int GetChannelVolumeLevelScalar(uint channel,out float value);
        [PreserveSig]int SetMute([MarshalAs(UnmanagedType.Bool)]bool muted,ref Guid context);
        [PreserveSig]int GetMute([MarshalAs(UnmanagedType.Bool)]out bool muted);
    }
}
