namespace AudioSwitch;

internal sealed record ProximitySettings(bool Enabled = false, string PhoneId = "", string Mode = "disconnect", int DelaySeconds = 30, int SignalThreshold = -75, int SignalHysteresis = 5);
internal sealed record PhoneSnapshot(string Id, string Name, bool? Connected, int? Signal, DateTimeOffset? SignalAt);
internal sealed record ProximityState(string Message, bool ShouldLock = false, int? Remaining = null, bool Healthy = false);

internal sealed class ProximityGuard
{
    private bool armed;
    private DateTimeOffset? awaySince;
    public void Reset() { armed = false; awaySince = null; }
    public ProximityState Evaluate(ProximitySettings? settings, PhoneSnapshot? phone, DateTimeOffset now)
    {
        if (settings?.Enabled != true) { Reset(); return new("离开锁屏已关闭"); }
        if (phone == null || phone.Id != settings.PhoneId || (phone.Connected == null && settings.Mode != "signal")) { Reset(); return new("手机状态不可用，暂停自动锁屏"); }
        bool fresh = phone.SignalAt is { } seen && now >= seen && now - seen <= TimeSpan.FromSeconds(20) && phone.Signal is >= -127 and < 0;
        bool signalMode = settings.Mode == "signal", combined = settings.Mode == "either";
        if (signalMode && !fresh) { awaySince = null; return new("没有实时信号，信号锁屏已暂停；需监测蓝牙关闭请选“断开或信号较弱”"); }
        int recoveryThreshold = settings.SignalThreshold + Math.Clamp(settings.SignalHysteresis, 1, 20);
        bool healthy = signalMode ? fresh && phone.Signal > recoveryThreshold : phone.Connected == true;
        if (healthy && (!combined || !fresh || phone.Signal > recoveryThreshold))
        { armed = true; awaySince = null; return new("手机在附近，监测中", Healthy: true); }
        if (!armed) return new("等待手机连接或靠近，尚未启用锁屏判定");
        bool away = (settings.Mode != "signal" && phone.Connected == false) || ((signalMode || combined) && fresh && phone.Signal <= settings.SignalThreshold);
        if (!away && awaySince != null && (signalMode || combined) && fresh && phone.Signal <= recoveryThreshold) away = true;
        if (!away) { awaySince = null; return new("信号处于缓冲区域，继续监测"); }
        awaySince ??= now;
        int remaining = Math.Max(0, Math.Clamp(settings.DelaySeconds, 15, 120) - (int)(now - awaySince.Value).TotalSeconds);
        if (remaining > 0) return new($"手机已离开，{remaining} 秒后锁屏；触发条件恢复后会取消", Remaining: remaining);
        Reset(); return new("正在锁定电脑", ShouldLock: true);
    }
}
