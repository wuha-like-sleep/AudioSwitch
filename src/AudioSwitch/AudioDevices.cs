using System.Runtime.InteropServices;

namespace AudioSwitch;

internal record OutputDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

internal static class AudioDevices
{
    public static List<OutputDevice> List()
    {
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
        IMMDeviceCollection? collection = null;
        try
        {
            Marshal.ThrowExceptionForHR(enumerator.EnumAudioEndpoints(0, 1, out collection));
            Marshal.ThrowExceptionForHR(collection.GetCount(out uint count));
            var result = new List<OutputDevice>();
            for (uint i = 0; i < count; i++)
            {
                Marshal.ThrowExceptionForHR(collection.Item(i, out var device));
                IPropertyStore? properties = null;
                try
                {
                    Marshal.ThrowExceptionForHR(device.GetId(out string id));
                    Marshal.ThrowExceptionForHR(device.OpenPropertyStore(0, out properties));
                    var key = new PropertyKey { Format = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), Id = 14 };
                    Marshal.ThrowExceptionForHR(properties.GetValue(ref key, out var value));
                    try { result.Add(new(id, Marshal.PtrToStringUni(value.Pointer) ?? id)); }
                    finally { PropVariantClear(ref value); }
                }
                finally { if (properties != null) Marshal.ReleaseComObject(properties); Marshal.ReleaseComObject(device); }
            }
            return result;
        }
        finally { if (collection != null) Marshal.ReleaseComObject(collection); Marshal.ReleaseComObject(enumerator); }
    }

    public static string? Default(int role = 1)
    {
        var enumerator = (IMMDeviceEnumerator)new DeviceEnumerator();
        IMMDevice? device = null;
        try
        {
            int hr = enumerator.GetDefaultAudioEndpoint(0, role, out device);
            if (hr == unchecked((int)0x80070490)) return null;
            Marshal.ThrowExceptionForHR(hr);
            Marshal.ThrowExceptionForHR(device.GetId(out string id));
            return id;
        }
        finally { if (device != null) Marshal.ReleaseComObject(device); Marshal.ReleaseComObject(enumerator); }
    }

    public static void SetDefault(string id)
    {
        // PolicyConfig is undocumented: keep this interop isolated and test on both Windows versions.
        var policy = (IPolicyConfig)new PolicyConfig();
        try
        {
            for (int role = 0; role < 3; role++)
                Marshal.ThrowExceptionForHR(policy.SetDefaultEndpoint(id, role));
            for (int role = 0; role < 3; role++)
                if (Default(role) != id) throw new InvalidOperationException("系统未确认默认输出切换，请重试。");
        }
        finally { Marshal.ReleaseComObject(policy); }
    }

    public static void VerifyInterfaces()
    {
        _ = List();
        _ = Default();
        var policy = (IPolicyConfig)new PolicyConfig();
        Marshal.ReleaseComObject(policy);
    }

    [DllImport("ole32.dll")] private static extern int PropVariantClear(ref PropVariant value);
}

[StructLayout(LayoutKind.Sequential)] internal struct PropertyKey { public Guid Format; public uint Id; }
// PROPVARIANT is 24 bytes on x64 and 16 bytes on x86. The explicit trailing union member preserves both sizes.
[StructLayout(LayoutKind.Explicit)] internal struct PropVariant
{
    [FieldOffset(0)] public ushort Type;
    [FieldOffset(8)] public IntPtr Pointer;
    [FieldOffset(8)] private CountedPointer counted;
}
[StructLayout(LayoutKind.Sequential)] internal struct CountedPointer { public uint Count; public IntPtr Pointer; }
[ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class DeviceEnumerator { }
[ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceEnumerator
{
    [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IMMDeviceCollection devices);
    [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
}
[ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDeviceCollection
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int Item(uint index, out IMMDevice device);
}
[ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IMMDevice
{
    [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, out IntPtr instance);
    [PreserveSig] int OpenPropertyStore(uint access, out IPropertyStore properties);
    [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    [PreserveSig] int GetState(out uint state);
}
[ComImport, Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPropertyStore
{
    [PreserveSig] int GetCount(out uint count);
    [PreserveSig] int GetAt(uint index, out PropertyKey key);
    [PreserveSig] int GetValue(ref PropertyKey key, out PropVariant value);
}
[ComImport, Guid("870AF99C-171D-4F9E-AF0D-E63DF40C2BC9")] internal class PolicyConfig { }
[ComImport, Guid("F8679F50-850A-41CF-9C72-430F290290C8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfig
{
    // Unused slots must remain in exact vtable order before SetDefaultEndpoint.
    [PreserveSig] int GetMixFormat(IntPtr id, IntPtr format);
    [PreserveSig] int GetDeviceFormat(IntPtr id, int defaultFormat, IntPtr format);
    [PreserveSig] int ResetDeviceFormat(IntPtr id);
    [PreserveSig] int SetDeviceFormat(IntPtr id, IntPtr endpoint, IntPtr mix);
    [PreserveSig] int GetProcessingPeriod(IntPtr id, int defaultPeriod, IntPtr period, IntPtr minimum);
    [PreserveSig] int SetProcessingPeriod(IntPtr id, IntPtr period);
    [PreserveSig] int GetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int SetShareMode(IntPtr id, IntPtr mode);
    [PreserveSig] int GetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
    [PreserveSig] int SetPropertyValue(IntPtr id, IntPtr key, IntPtr value);
    [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
}
