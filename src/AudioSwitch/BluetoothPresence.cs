using Windows.Devices.Bluetooth;
using Windows.Devices.Enumeration;

namespace AudioSwitch;

internal sealed class BluetoothPresence : IDisposable
{
    private readonly object gate = new();
    private readonly Dictionary<string, PhoneSnapshot> phones = new();
    private DeviceWatcher? watcher;
    public string? Error { get; private set; }
    internal Exception? Failure { get; private set; }
    public List<PhoneSnapshot> Snapshot() { lock (gate) return phones.Values.OrderBy(p => p.Name).ToList(); }
    public void Start()
    {
        if (watcher != null) return;
        try
        {
            string selector = $"({BluetoothDevice.GetDeviceSelectorFromPairingState(true)}) OR ({BluetoothLEDevice.GetDeviceSelectorFromPairingState(true)})";
            watcher = DeviceInformation.CreateWatcher(selector, ["System.Devices.Aep.IsConnected", "System.Devices.Aep.SignalStrength"], DeviceInformationKind.AssociationEndpoint);
            watcher.Added += Added; watcher.Updated += Updated; watcher.Removed += Removed;
            watcher.Stopped += Stopped; watcher.Start();
        }
        catch (Exception ex) { Failure = ex; Error = "蓝牙监测不可用：" + ex.Message; }
    }
    private void Added(DeviceWatcher sender, DeviceInformation info)
    {
        lock (gate)
        {
            bool? connected = info.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is bool value ? value : null;
            // Enumeration can contain cached signal data. Wait for a real update before using signal mode.
            phones[info.Id] = new(info.Id, info.Name, connected, null, null);
        }
    }
    private void Updated(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        lock (gate)
        {
            if (!phones.TryGetValue(update.Id, out var phone)) return;
            if (update.Properties.TryGetValue("System.Devices.Aep.IsConnected", out var c) && c is bool connected) phone = phone with { Connected = connected };
            if (update.Properties.TryGetValue("System.Devices.Aep.SignalStrength", out var s) && s is int strength && strength is >= -127 and < 0)
                phone = phone with { Signal = strength, SignalAt = DateTimeOffset.UtcNow };
            phones[update.Id] = phone;
        }
    }
    internal static PhoneSnapshot Disconnected(PhoneSnapshot phone) => phone with { Connected = false, Signal = null, SignalAt = null };
    private void Removed(DeviceWatcher sender, DeviceInformationUpdate update)
    {
        // Preserve a known endpoint as disconnected. Deleting it would erase an
        // armed departure countdown by turning an explicit removal into unknown.
        lock (gate) if (phones.TryGetValue(update.Id, out var phone)) phones[update.Id] = Disconnected(phone);
    }
    private void Stopped(DeviceWatcher sender, object args) { if (sender.Status == DeviceWatcherStatus.Aborted) Error = "蓝牙监测已中断，请重启应用后重试。"; }
    public void Dispose()
    {
        if (watcher == null) return;
        watcher.Added -= Added; watcher.Updated -= Updated; watcher.Removed -= Removed; watcher.Stopped -= Stopped;
        if (watcher.Status is DeviceWatcherStatus.Started or DeviceWatcherStatus.EnumerationCompleted) watcher.Stop();
    }
}
