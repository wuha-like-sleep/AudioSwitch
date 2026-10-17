# 开发与发布

## 构建

需要 Windows、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) 和 [Inno Setup 6.5+](https://jrsoftware.org/isinfo.php)。

```powershell
# 构建默认 x64 安装包
.\build.ps1 -Repository wuha-like-sleep/AudioSwitch

# 其他架构
.\build.ps1 -Runtime win-arm64 -Repository wuha-like-sleep/AudioSwitch
.\build.ps1 -Runtime win-x86 -Repository wuha-like-sleep/AudioSwitch

# 开发时只编译应用
.\build.ps1 -SkipInstaller
```

安装包位于 `artifacts/installer/`。开发构建目录位于 `artifacts/<runtime>/`，不是面向用户的发行包。
更改仓库时通过 `-Repository owner/repo` 指定更新源；程序不需要 GitHub Token。
项目采用 .NET 8，维护时需在支持周期结束前升级目标框架。

## 发布

GitHub Actions 自动构建三种架构的安装包，更新源绑定到实际仓库。
x64 作业额外验证静默安装、自启动注册、应用运行和卸载清理。

1. 更新 `.csproj` 中的版本号，提交代码。
2. 推送同版本标签，如 `v0.3.0-preview.1`。
3. 自动构建完成后下载安装包，在 Windows 真机验证。
4. 创建同版本 GitHub Release（测试版勾选 prerelease），上传三个 `AudioSwitch-Setup-*.exe` 安装包并填写更新说明。

GitHub 为上传的资产生成 SHA-256 digest。只有包含校验信息且架构匹配的安装包才能自动更新。
应用与安装器尚未配置代码签名证书。


## 安装器检查

`tests/installer-smoke.ps1` 用于独立 Windows CI 环境，验证静默安装、自启动注册、界面预览、进程运行和卸载清理。该脚本会实际安装和卸载应用，不应在个人日常环境中运行。

公开测试版使用 `v主版本.次版本.修订号-preview.序号` 标签，并在项目的 `InformationalVersion` 中设置相同版本标识。测试版更新检查同时接收测试版和正式版，正式版仅接收正式更新。

## 手机离开锁屏

目标框架为 `net8.0-windows10.0.19041.0`，使用 Windows SDK 的 `DeviceWatcher` 读取已配对的经典蓝牙和 BLE 关联端点。首次枚举可能携带缓存，不用缓存信号启动信号锁屏；只有更新事件中的实时负值信号参与判定，超过 20 秒暂停信号模式。信号值不映射成米数，不保证所有手机提供该属性。

`ProximityGuard` 独立验证首次确认、持续离开、重连取消、信号过期和单次触发；CI 不调用真实锁屏。生产环境通过 `LockWorkStation` 锁定，系统解锁、休眠恢复和编辑设置时重置判定。需要在 Windows 10/11 真机验证实际手机协议、蓝牙关闭/恢复、手机休眠、信号更新频率和系统锁屏行为。

参考：[DeviceWatcher 蓝牙选择器](https://learn.microsoft.com/en-us/uwp/api/windows.devices.bluetooth.bluetoothdevice.getdeviceselectorfrompairingstate)、[SignalStrength 属性](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-aep-signalstrength)、[LockWorkStation](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-lockworkstation)。

## 手机解锁边界

当前没有实现手机自动解锁或手机确认解锁。解除已经锁定的 Windows 会话需要系统登录认证；`DeviceWatcher` 的连接状态、名称和信号值不能直接作为解锁凭据。仅使用现有 iPhone 配对、不安装手机 App 的路线仍需独立验证：Windows Credential Provider 与受保护的认证通道、手机身份确认、账户类型兼容性、撤销/恢复方案，以及真实锁定会话测试。

后续登录组件不得禁用 Windows 原有的密码、PIN 或 Windows Hello 提供程序；不得以模拟输入、明文配置或单纯名称/RSSI 匹配宣称完成认证。当前发行版只取消离开倒计时或调用系统锁屏，不会解锁会话。参考：[Windows Credential Providers](https://learn.microsoft.com/en-us/windows/win32/secauthn/credential-providers-in-windows)。
