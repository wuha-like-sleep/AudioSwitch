namespace AudioSwitch;

internal sealed record AudioSnapshot(List<OutputDevice> Devices, string? Current);
internal sealed record PreparedSwitch(OutputDevice Device, bool AlreadyCurrent);

internal static class SwitchPreparation
{
    // Retry only the read/selection stage. Never repeat a possibly partially applied write.
    internal static async Task<PreparedSwitch> PrepareAsync(Settings settings, string? explicitTarget, bool startupGrace,
        Func<Task<AudioSnapshot>> read, Func<CancellationToken, Task> delay, Action waiting, CancellationToken cancellation)
    {
        bool announced = false;
        for (int attempt = 0; ; attempt++)
        {
            cancellation.ThrowIfCancellationRequested();
            try
            {
                var snapshot = await read();
                cancellation.ThrowIfCancellationRequested();
                var target = explicitTarget ?? SwitchTarget.Choose(settings, snapshot.Current, snapshot.Devices.Select(d => d.Id));
                var device = snapshot.Devices.FirstOrDefault(d => d.Id == target);
                if (device != null) return new(device, snapshot.Current == device.Id);
                if (!startupGrace || attempt >= 5)
                    throw new InvalidOperationException("所选输出设备尚未连接或就绪，请连接设备后重试。");
            }
            catch (Exception ex) when (startupGrace && attempt < 5 && IsStarting(ex)) { }
            if (!announced) { waiting(); announced = true; }
            await delay(cancellation);
        }
    }
    private static bool IsStarting(Exception error) => error.HResult is unchecked((int)0x80070426)
        or unchecked((int)0x800706BA) or unchecked((int)0x800706D9);

    internal static async Task VerifyAsync()
    {
        var settings = new Settings("a", "b");
        int reads = 0, waits = 0, announcements = 0;
        var prepared = await PrepareAsync(settings, null, true,
            () => Task.FromResult(++reads < 3 ? new AudioSnapshot([], null) : new AudioSnapshot([new("a", "A"), new("b", "B")], "a")),
            _ => { waits++; return Task.CompletedTask; }, () => announcements++, CancellationToken.None);
        if (prepared.Device.Id != "b" || prepared.AlreadyCurrent || reads != 3 || waits != 2 || announcements != 1)
            throw new InvalidOperationException("Startup read retry did not preserve the selected cycle target.");
        reads = 0;
        prepared = await PrepareAsync(settings, null, true,
            () => ++reads == 1 ? Task.FromException<AudioSnapshot>(new System.Runtime.InteropServices.COMException("Starting", unchecked((int)0x80070426)))
                : Task.FromResult(new AudioSnapshot([new("b", "B")], "a")),
            _ => Task.CompletedTask, () => { }, CancellationToken.None);
        if (reads != 2 || prepared.Device.Id != "b") throw new Exception("Starting audio service was not retried");
        reads = 0;
        try
        {
            await PrepareAsync(settings, null, true, () => { reads++; throw new InvalidCastException(); },
                _ => throw new InvalidOperationException("Unexpected retry"), () => { }, CancellationToken.None);
            throw new InvalidOperationException("Unexpected success");
        }
        catch (InvalidCastException) { if (reads != 1) throw new InvalidOperationException("A programming error was retried."); }
        reads = 0;
        try
        {
            await PrepareAsync(settings, null, true, () => { reads++; return Task.FromResult(new AudioSnapshot([], null)); },
                _ => Task.CompletedTask, () => { }, CancellationToken.None);
            throw new Exception("Unbounded startup retry");
        }
        catch (InvalidOperationException) { if (reads != 6) throw new Exception("Startup retry limit failed"); }
        using var stop = new CancellationTokenSource();
        try
        {
            await PrepareAsync(settings, null, true, () => { stop.Cancel(); return Task.FromResult(new AudioSnapshot([new("b", "B")], "a")); },
                _ => Task.CompletedTask, () => { }, stop.Token);
            throw new Exception("A cancelled request selected a device");
        }
        catch (OperationCanceledException) { }
        reads = 0;
        prepared = await PrepareAsync(settings, "a", false,
            () => { reads++; return Task.FromResult(new AudioSnapshot([new("a", "A")], "a")); },
            _ => throw new Exception("Unexpected delay"), () => { }, CancellationToken.None);
        if (!prepared.AlreadyCurrent || reads != 1) throw new Exception("Current-device selection failed");
    }
}
