using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Rfcomm;
using Windows.Networking.Sockets;

namespace AudioSwitch;

internal sealed record UnlockProbeResult(string Title, string Details, bool AuthenticatedChannel = false);

// Research probe only: this class never collects Windows credentials or invokes logon/unlock APIs.
internal static class UnlockLinkProbe
{
    private static readonly Guid HandsFreeAudioGateway = new("0000111f-0000-1000-8000-00805f9b34fb");
    public static bool IsAuthenticated(SocketProtectionLevel actual) => actual == SocketProtectionLevel.BluetoothEncryptionWithAuthentication;
    public static async Task<UnlockProbeResult> RunAsync(string phoneId, CancellationToken userCancellation)
    {
        if (string.IsNullOrWhiteSpace(phoneId)) return new("请先选择手机", "返回手机离开锁屏设置，选择已经在 Windows 配对的 iPhone，再运行检测。");
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(userCancellation);
        deadline.CancelAfter(TimeSpan.FromSeconds(20));
        try
        {
            using var phone = await BluetoothDevice.FromIdAsync(phoneId).AsTask(deadline.Token);
            if (phone == null) return new("未打开经典蓝牙接口", "该端点可能仅为低功耗接口，或 Windows 未允许访问。请检查配对状态并尝试同名的经典蓝牙端点。");
            var result = await phone.GetRfcommServicesForIdAsync(RfcommServiceId.FromUuid(HandsFreeAudioGateway), BluetoothCacheMode.Uncached).AsTask(deadline.Token);
            try
            {
                if (result.Error != BluetoothError.Success) return new("未取得服务", "Windows 返回：" + result.Error + "。这不是 Windows 登录认证结果。");
                var service = result.Services.FirstOrDefault();
                if (service == null) return new("没有可测试的服务", "未找到手机的免提音频网关服务。未证实无手机 App 的认证加密通道；不会退回仅名称、配对记录或信号匹配。");
                using var socket = new StreamSocket();
                await socket.ConnectAsync(service.ConnectionHostName, service.ConnectionServiceName, SocketProtectionLevel.BluetoothEncryptionWithAuthentication).AsTask(deadline.Token);
                var protection = socket.Information.ProtectionLevel;
                if (!IsAuthenticated(protection)) return new("未确认所需保护级别", $"实际保护级别：{protection}。不将普通或允许空认证的连接视为通过。");
                return new("认证加密蓝牙通道已建立", "Windows 报告连接采用 BluetoothEncryptionWithAuthentication。连接随后已关闭，未发送通话命令。\n\n这只验证蓝牙链路，不等于 Windows 身份认证或成功解锁。独立 Credential Provider 实验版已具备本机初始化、受保护存储和自动提交代码，默认关闭；仍需真实 iPhone、微软账户和锁定会话端到端验证。", true);
            }
            finally { foreach (var service in result.Services) service.Dispose(); }
        }
        catch (OperationCanceledException)
        {
            return userCancellation.IsCancellationRequested ? new("检测已取消", "连接已结束，没有更改 Windows 登录配置。") : new("检测超时", "20 秒内未完成连接。可能是手机不可用、系统拒绝访问或服务被其他软件占用；不能据此认定支持解锁。");
        }
        catch (Exception ex) { return new("未建立所需通道", $"{ex.GetType().Name} · 0x{ex.HResult:X8}\n{ex.Message}\n\n没有更改登录配置，没有保存密码或 PIN。请在手机未通话时重试，并保留此信息用于排查。"); }
    }
}
