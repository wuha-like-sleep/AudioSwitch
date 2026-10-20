# 开发与发布

## 构建

需要 Windows、[.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)、[Inno Setup 6.5+](https://jrsoftware.org/isinfo.php)、CMake 3.24+，以及 Visual Studio 2022 的“使用 C++ 的桌面开发”工具链和对应架构工具（ARM64 包需要 ARM64 编译工具）。`-SkipInstaller` 只构建音频应用，不构建登录实验组件。

```powershell
# 构建默认 x64 安装包
.\build.ps1 -Repository wuha-like-sleep/AudioSwitch

# 其他架构
.\build.ps1 -Runtime win-arm64 -Repository wuha-like-sleep/AudioSwitch
.\build.ps1 -Runtime win-x86 -Repository wuha-like-sleep/AudioSwitch

# 开发时只编译应用
.\build.ps1 -SkipInstaller
```

两组安装包位于 `artifacts/installer/`：`AudioSwitch-Setup-*` 为普通音频应用，`AudioSwitch-LoginLab-Setup-*` 为可选原生登录实验组件。开发构建目录位于 `artifacts/<runtime>/`，不是面向用户的发行包。
更改仓库时通过 `-Repository owner/repo` 指定更新源；程序不需要 GitHub Token。
项目采用 .NET 8，维护时需在支持周期结束前升级目标框架。

## 发布

GitHub Actions 自动构建三种架构的安装包，更新源绑定到实际仓库。
x64 作业额外验证静默安装、自启动注册、应用运行和卸载清理。

1. 同步更新 `.csproj`、两个 Inno 安装器的默认版本、`native/LoginProvider/Version.rc` 和 `NOTICE.txt` 中的版本号，提交代码。
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

当前没有实现手机自动解锁或手机确认解锁。0.3.4 开始提供独立、可选的原生登录实验组件，尚未启用手机自动提交。解除已经锁定的 Windows 会话需要系统登录认证；`DeviceWatcher` 的连接状态、名称和信号值不能直接作为解锁凭据。仅使用现有 iPhone 配对、不安装手机 App 的路线仍需独立验证：Windows Credential Provider 与受保护的认证通道、手机身份确认、账户类型兼容性、撤销/恢复方案，以及真实锁定会话测试。

后续登录组件不得禁用 Windows 原有的密码、PIN 或 Windows Hello 提供程序；不得以模拟输入、明文配置或单纯名称/RSSI 匹配宣称完成认证。当前发行版只取消离开倒计时或调用系统锁屏，不会解锁会话。参考：[Windows Credential Providers](https://learn.microsoft.com/en-us/windows/win32/secauthn/credential-providers-in-windows)。


### 0.3.3：无手机 App 的 iPhone 路线研究

| 环节 | 当前状态 |
| :--- | :--- |
| 已配对手机、连接和实时信号观察 | 已有实现，具体 iPhone/适配器支持需真机确认 |
| 认证加密蓝牙链路 | 新增手动检测；尚无真实 iPhone 成功记录 |
| 微软账户认证材料 | 未确认；日常使用 PIN / Hello 不足以判断是否有密码 |
| 自定义 Windows Credential Provider | 未实现、未注册、未随安装包安装 |
| 已锁定会话自动解锁 | 未实现；不得把检测通过写成解锁成功 |

`UnlockLinkProbe` 尝试查询 iPhone 的 HFP Audio Gateway RFCOMM 服务，建立要求 `BluetoothEncryptionWithAuthentication` 的 `StreamSocket`，并核对实际 `ProtectionLevel`。不发送通话指令，不采用允许空认证的保护级别；连接、服务和设备对象随后释放。20 秒超时，关闭窗口取消。该测试只在用户点击时运行，可能因手机服务未暴露、权限、适配器或其他软件占用而失败。HFP 连接成功也不证明手机的实时距离、人在场或 Windows 账户身份，仍需评估中继风险和锁定会话下的可用性。

Credential Provider 负责收集和序列化凭据，最终由 Windows 认证机制授权；添加一个提供程序本身不会让蓝牙身份变成有效的微软账户凭据。PIN / Hello 不能作为可读取并重复提交的账户密码。Windows 的“仅允许 Windows Hello 登录”设置也不能单独证明微软账户是否已删除密码。账户状态应由用户在 Windows 登录选项及微软账户安全设置中确认，本程序不要求向开发者提供密码或 PIN。

后续原型必须先确定系统支持的认证材料及本机初始化方式，再设计独立提供程序和受保护服务之间的协议、秘密保存、撤销、重试限制与失败回退。保留系统登录入口，在隔离的 Windows 10/11 真机上验证锁定/解锁、重启、离线、手机丢失、蓝牙断开、组件卸载和恢复。当前 CI 只检查保护级别拒绝、缺少手机、模拟结果展示、关闭取消和界面布局，不连接真实手机、不调用登录认证或解锁。

资料：[Credential Provider 架构](https://learn.microsoft.com/en-us/windows/win32/secauthn/credential-providers-in-windows)、[Windows Hello](https://learn.microsoft.com/en-us/windows/apps/develop/security/windows-hello)、[Windows 无密码登录设置](https://support.microsoft.com/en-us/windows/security/go-passwordless-in-windows)、[RFCOMM](https://learn.microsoft.com/en-us/uwp/devices-sensors/send-or-receive-files-with-rfcomm)、[SocketProtectionLevel](https://learn.microsoft.com/en-us/uwp/api/windows.networking.sockets.socketprotectionlevel)。


### 0.3.4：原生微软账户登录实验组件

`native/LoginProvider` 为独立 C++17 V2 Credential Provider，独立 CLSID 为 `{A3AC9286-65AB-4438-B97E-84A4C9A65320}`。实现 `ICredentialProvider`、`ICredentialProviderSetUserArray` 和 `ICredentialProviderCredential2`，通过系统给出的用户数组绑定 SID 与 `PKEY_Identity_QualifiedUserName`，仅枚举明确 `MicrosoftAccount\` 命名空间的用户。无密码微软账户缺少本实验所需的密码；不根据注册表推断是否有密码。

仅支持 `CPUS_LOGON` 与 `CPUS_UNLOCK_WORKSTATION`，不接受来自其他组件的序列化凭据，不成为默认入口，`SetSelected` 和枚举均返回不自动登录。手动密码采用 `CRED_PACK_PROTECTED_CREDENTIALS | CRED_PACK_ID_PROVIDER_CREDENTIALS` 打包，提交给 Negotiate 认证包，由 Windows 授权。密码只存在于当前凭据对象内存，替换、提交、取消、失败或释放时擦除；不持久化、不向托盘进程发送。此提交路径遵循微软公开 API，**不等于已通过真实微软账户认证**。

独立 `AudioSwitch-LoginLab-Setup-*.exe` 安装器需要管理员权限，将原生 DLL 安装到 Program Files，只注册自身 CLSID 与 Credential Providers 子键；不安装过滤器，不包装或修改系统提供程序，不更改 Hello/PIN 策略。x64、ARM64、x86 安装器各限制为对应原生操作系统；普通音频安装器仍为当前用户安装，完全不注册实验登录组件。卸载实验组件只删除自身键和文件，DLL 被登录界面占用时可能需要重启。

本版完成的是可向 Windows 提交账户凭据的实验组件。自动解锁尚缺少受保护服务与组件间协议、绑定/撤销、已验证的手机身份和新鲜度、凭据初始化与保存方案、限制重试，以及真实锁定会话验证。任何自动提交均尚未启用。Hello / PIN 兼容指保留原有系统入口，不代表可以由本组件无交互调用 Hello。旧 Companion Device Framework 在 Windows 10 2004 起已弃用，不作为方案依赖。

CI 原生测试在不注册时加载 DLL，验证 COM 生命周期、SID 绑定、非微软账户排除、使用场景拒绝、非默认/非自动提交、受保护缓冲区可被 Windows 还原、缓冲区不含测试密码明文、取消/失败清除。隔离 CI 还安装和卸载实验组件，检查 Program Files 路径、自身注册及系统提供程序键保留，加载已安装 DLL 重复测试。测试仅使用虚构账户和测试字符串，不执行真实 LSA 登录、锁屏或退出会话；ARM64 / x86 仅构建，原生运行测试为 x64。

资料：[身份提供程序凭据打包](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-credpackauthenticationbufferw)、[GetUserSid](https://learn.microsoft.com/en-us/windows/win32/api/credentialprovider/nf-credentialprovider-icredentialprovidercredential2-getusersid)、[使用场景](https://learn.microsoft.com/en-us/windows/win32/api/credentialprovider/ne-credentialprovider-credential_provider_usage_scenario)、[伴随设备框架弃用](https://learn.microsoft.com/en-us/windows-hardware/design/device-experiences/windows-hello-companion-device-framework)。

此外核查了 [Windows Hello for Business 可信信号解锁](https://learn.microsoft.com/en-us/windows/security/identity-protection/hello-for-business/trusted-signal-unlock)：蓝牙为附加因素，仍需 PIN、指纹或人脸作为第一因素，不能据此宣称个人微软账户可仅靠 iPhone 靠近自动解锁。

0.3.4 发行代码为 `0f7d5b9e6bc8b357a3336aab296abd3431ee367b`；[Windows CI 39023772363](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39023772363) 三架构构建成功，x64 原生契约、保护凭据测试副本恢复、密码清除、独立安装注册与卸载检查通过。真实账户认证未执行。


### 0.3.5：手机认证 → 系统服务 → 登录组件

新增 `AudioSwitch.LoginBridge.exe` LocalSystem 服务，独立实验安装器负责停止旧服务、安装/启动、卸载服务和删除自身绑定。普通音频安装器不安装服务。应用从所选经典蓝牙端点读取地址，本机管理员工具确认已经配对后，将绑定写入受保护的 HKLM 账户 SID 子键。绑定和解除即时生效，独立于普通音频配置保存，不保存密码。

已绑定时，Credential Provider 在序列化密码前请求服务；服务只接受原生系统目录中的 LogonUI.exe、LocalSystem 身份、对应会话和该会话真实用户 SID，并要求会话已锁定。服务通过蓝牙套接字对绑定地址的 HFP 网关服务发起现场连接，强制 `SO_BTH_AUTHENTICATE` 和 `SO_BTH_ENCRYPT`，检查实际对端地址，无普通连接降级、无配对向导、不发送通话命令。连接最多等待约 8 秒。会话事件改变、绑定撤销或手机验证失败均拒绝返回可用结果。经典蓝牙 API 仅支持查询 MTU 选项，本实现依赖强制设置认证/加密后的连接成功语义，不假称读回了保护级别；真实 iPhone 和 LocalSystem 蓝牙兼容性尚未验证。

IPC 为固定版本命名管道，仅本机 SYSTEM/管理员可连接，拒绝远程客户端。客户端以识别级别连接，核对管道服务端 PID 与 SCM 中实际运行服务的 PID；不信任同名假服务。每次请求生成 32 字节随机 nonce，结果对应 nonce、会话、绑定手机，完成时间最多 2 秒，未来时间拒绝。每次密码提交均新建请求，无成功状态缓存或持久化“手机通过”文件，管道中不传递账户密码或序列化凭据。读取、写入及等待均有超时，关闭服务取消挂起操作。

未绑定保留原来的手动密码实验；已绑定但配置损坏、服务停止、身份不符、手机不可用时停止提交并清除密码，不自动退回无手机的实验登录。系统 PIN / Hello 入口仍保留，不新增 Credential Provider Filter。解除绑定恢复手动密码实验模式。绑定模式不适用于开机首次登录、其他用户或组织/本地账户。

本版接通的是“手机通道验证作为手动密码提交门槛”。**仍未完成无确认自动解锁。** 尚无密码初始化、持久凭据存储和自动提交，真实微软账户/锁定会话/真实 iPhone 未验证。链路级认证不代表人在场、不提供距离绑定，不解决蓝牙中继风险。Hello 仍由系统直接处理，本组件不绕过用户确认来调用 Hello。

CI 验证服务安装、启动、停止、删除；构造管理员测试绑定，确保普通测试进程不是 LogonUI 时拿不到允许提交的结果，服务不存在时拒绝。协议测试覆盖正确结果、错误 nonce/手机/会话/版本、过期和未来时间。保护密码序列化等原有检查继续保留。正向蓝牙验证和真实登录不在 CI 测试范围；不能把这些失败路径测试写成端到端自动解锁通过。

资料：[蓝牙套接字选项](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-socket-options)、[管道客户端进程](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeclientprocessid)、[管道服务端进程](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-getnamedpipeserverprocessid)、[用户会话令牌](https://learn.microsoft.com/en-us/windows/win32/api/wtsapi32/nf-wtsapi32-wtsqueryusertoken)。

0.3.5 Preview 2 发行代码：`459cd86aef6a4aba5839846df991f090dced33df`。[Windows CI 39024788404](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39024788404) 三架构构建通过，x64 服务安装/重复安装升级/启动/停止/删除、缺损绑定拒绝、普通进程拒绝和服务不可用拒绝通过。仍无真实手机或真实账户认证成功记录。

系统服务安装固定为原生 Program Files 下的 AudioSwitch Login Lab，不接受自定义目录；管理员工具也核对自身路径才允许注册服务。CI 验证自定义可写目录和开发目录中的松散工具不能注册服务。

Preview 2 的固定路径和自定义目录拒绝检查已通过最终 Windows CI；升级实验组件后可能需重启以释放旧 DLL，首次启动仍使用系统 Hello / PIN 登录。


### 0.3.6：本机凭据初始化、存储与自动提交

默认关闭。应用仅启动已安装的原生管理员工具，命令参数只有操作与目标 SID；不接触密码。原生工具调用 Windows CredUI（`CREDUIWIN_GENERIC`，不是安全桌面提示；不宣称它是 Hello），用户输入邮箱与账户密码并确认持久化。初始化请求仅发往固定路径的 LocalSystem 服务，以机器 DPAPI 包装临时内容；服务核对安装目录中的管理员工具、会话用户 SID，执行 `LogonUserW` 后比较返回令牌 SID。验证失败、取消、账户不符或未绑定不启用。真实个人微软账户的此初始化路径仍需真机验证，CI 不使用真实密码。无密码账户及 PIN / Hello 不作为可保存密码。

服务保存机器 DPAPI 密文，注册表 Vault 根与账户子键使用受保护 DACL，仅 SYSTEM 和 Administrators 全权访问，并检查 ACL 与所有者。机器 DPAPI 本身允许同机上下文解密，**访问隔离依靠注册表 ACL，管理员仍可读取/解密或修改系统**。密文记录内绑定版本、SID、账户名与手机地址；加载时逐一核对。初始化缓冲、解密内容、密码和测试密文在释放时擦除，不写日志，不经命令行传递。公开绑定键只保存自动配置标记，不保存密码。

桥接协议升至 v2。AutoProbe 仅允许已初始化的真实 LogonUI / SYSTEM 锁定会话：至少 3 次通道验证失败、跨度至少 12 秒后标记可以检测恢复，初始就在场不会马上自动提交；缺少适配器或配对身份不用于启动此判定。恢复认证连接后发放 2 秒内、绑定当前会话/手机/会话事件代次的随机请求票据。AutoCommit 消耗该票据，同一次锁屏最多一次；不通过重复密码尝试猜测账户。新锁屏事件可重置次数，服务重启会丢失内存判定，需要重新经历失败/恢复过程。

Credential Provider 后台线程仅探测手机，不预先解密密码，通过所属线程的消息窗口通知 `ICredentialProviderEvents::CredentialsChanged`；只有新鲜许可才在 `GetCredentialCount` 返回自动默认凭据。`GetSerialization` 再向服务消费票据、读取并解密本机凭据、再次检查撤销/手机/期限，随后按原受保护身份提供程序格式交给 Windows。不会模拟按键、不设置 Windows AutoAdminLogon，也不禁用系统提供程序。结束或重新枚举取消线程并释放事件窗口。未初始化仍支持原手动实验路径。

自动 Windows 认证失败在 `ReportResult` 撤销并删除保存凭据，必须用 Hello / PIN 登录后重新初始化；存储损坏或身份不匹配也停用。按钮“停用并清除”、重新绑定/解除绑定和卸载清理密文与标记。更换账户密码后需重新初始化。普通音频安装器不安装服务或初始化凭据。

CI 检查虚构记录的 DPAPI 往返、密文不含测试密码、受限令牌不能读取注册表密文、身份不符、密文篡改及撤销；策略测试覆盖初始在场、短暂失败、持续失败后恢复、错误绑定/代次、过期、单次消费和禁止重复尝试。真实 CredUI 输入、微软账户初始化、LogonUI 事件切换、真实手机与自动解锁成功不在 CI 范围。不能将单元检查写成实际无确认自动解锁已经成功。

限制：通道不可用也可能由配置、服务占用或无线干扰导致，不等于精确离开；恢复认证不证明人在场，仍不防蓝牙中继。不适用于无密码微软账户、本地/组织账户、开机首次登录；系统 Hello / PIN 始终作为独立入口。

资料：[CredUI](https://learn.microsoft.com/en-us/windows/win32/api/wincred/nf-wincred-creduipromptforwindowscredentialsw)、[LogonUserW](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-logonuserw)、[DPAPI 范围](https://learn.microsoft.com/en-us/windows/win32/seccrypto/example-c-program-using-cryptprotectdata)、[自动默认凭据](https://learn.microsoft.com/en-us/windows/win32/api/credentialprovider/nf-credentialprovider-icredentialprovider-getcredentialcount)、[CredentialsChanged](https://learn.microsoft.com/en-us/windows/win32/api/credentialprovider/nf-credentialprovider-icredentialproviderevents-credentialschanged)。

0.3.6 Preview 1 发行代码：`9cfc1eb5e6e2fced96b7be9a616686fbb3464c4f`。[Windows CI 39026011908](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39026011908) 三架构构建成功；x64 DPAPI、受限令牌读取拒绝、篡改、身份不符、撤销、单次策略、自动线程启动/取消及无许可不通知、服务与安装检查通过。数据仅为虚构测试凭据，没有执行真实微软账户初始化或解锁。

### 0.3.7：端到端验证诊断

为已授权请求记录初始化、手机认证和单次证明结果；原生登录组件记录自动凭据提交及 Windows ReportResult 主状态 / 子状态。记录不含密码、邮箱、地址、nonce 或序列化凭据，不参与授权决策。设置窗口显示阶段和时间，支持复制诊断。初始化常见错误提供处理说明，登录失败显示状态码并保留 Hello / PIN。

[真机验收步骤](UNLOCK-VALIDATION.md)。没有真实 iPhone / 微软账户测试机，不能把 CI 通过称为端到端解锁成功。

修正磁贴 SetSelected 始终返回 FALSE 的行为：仅 AutomaticReady 的短时许可有效且没有手动编辑时请求自动提交，与 GetCredentialCount 条件一致。[Microsoft SetSelected 文档](https://learn.microsoft.com/en-us/windows/win32/api/credentialprovider/nf-credentialprovider-icredentialprovidercredential-setselected) 明确系统仍可能展示登录按钮，不能承诺任何系统策略下都无需确认。

0.3.7 Preview 1 发行代码：`4120246550dec2ca2ae25f36605b6f88e27f496f`。[Windows CI 39026758882](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39026758882) 三架构构建成功；x64 安装、系统登录入口保留、服务和存储检查，以及诊断状态 / 子状态保存、ReportResult 空输出回归测试通过。真机自动解锁仍待验证。

### 0.3.8：应用内安装登录组件

登录组件页面直接获取当前版本 GitHub Release 的原生系统架构 LoginLab 安装包，校验项目、版本、文件名及 SHA-256 后启动交互安装向导。提供下载进度、取消、网络超时和安装错误提示，成功后刷新状态；不退出主程序、不自动启用解锁。管理员权限仅在安装向导启动时请求。下载取消和失败会清理临时文件，已启动安装包暂留以避免影响安装子进程。组件下载功能不改变无密码账户兼容性与真机验证边界。

修复已识别蓝牙端点 Removed 时被删除记录、导致离开计时复位的问题。现在保留断开状态并清空过期信号，只有此前已确认在附近的手机才会触发倒计时；断开模式与组合模式添加回归检查。托盘显示设置窗口打开、手动暂停、休眠和蓝牙异常的具体暂停原因。纯信号模式缺失 RSSI 仍暂停，并提示改用组合模式监测蓝牙断开。

0.3.8 Preview 1 发行代码：`2aa793a501bacd191c5a93c7b3a71047e5eae08c`。[Windows CI 39029965220](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39029965220) 三架构构建及 x64 安装、组件、设置回归检查通过。用户真机反馈：关闭设置窗口后离开锁屏正常；重新连接后自动解锁仍失败，等待分阶段诊断。

### 0.3.9：系统服务蓝牙路径诊断

用户真机反馈：账户密码与当前 SID 初始化通过，但锁屏阶段记录 PhoneUnavailable，旧实现没有保留原因；用户态 WinRT 探测曾通过，不能据此推断 LocalSystem 连接结果。没有凭据提交或 Windows 认证结果记录，因此尚未确认端到端解锁成功。

增加管理员桌面“检测系统服务”请求，只允许受保护目录中的提权辅助程序、当前会话用户 SID；服务使用同一配对检查和认证加密 RFCOMM 路径。此请求不读写凭据、不改变单次自动策略，不生成可供自动提交使用的许可。独立 ServiceProbe 诊断与锁屏 Phone 记录分开。

记录适配器、配对、套接字、认证、加密、连接事件、connect、等待与对端校验阶段和原生错误。以 FD_CONNECT / WSAEnumNetworkEvents 保留异步连接失败码；8 秒超时与服务停止均明确返回。仅可达性失败可计入离开条件，认证与本机配置错误不能用于触发自动提交。IPC 升级为 v3；旧进程不会复用新协议，组件升级后可能需要重启使 LogonUI 载入新 DLL。没有降低认证或加密要求。

依据：[Bluetooth connect](https://learn.microsoft.com/en-us/windows/win32/bluetooth/bluetooth-and-connect)、[WSAEventSelect](https://learn.microsoft.com/en-us/windows/win32/api/winsock2/nf-winsock2-wsaeventselect)。此版用于定位用户态成功、系统服务失败的差异，不宣称已修复真机解锁。

0.3.9 Preview 1 发行代码：`ba73bc4c3f0f43dfae912d1f7e00aadda5736ec1`。[Windows CI 39030502631](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39030502631) 三架构构建，x64 安装、服务、受保护存储、协议、跨 SID 检测拒绝与错误解释检查通过。CI 没有真实 iPhone，系统服务连接失败原因仍待用户新诊断确认。

### 0.3.10：登录组件版本匹配提示

针对“检测系统服务提示参数错误、没有新诊断”的反馈，在运行检测、初始化及新绑定前检查磁盘辅助程序 ProductVersion 与主程序版本一致，避免旧辅助程序不认识新命令时只显示通用错误。状态页列出主程序与磁盘组件版本；检测错误显示退出码，87 明确说明请求可能尚未进入服务。安装向导退出后再次核对磁盘组件版本，不匹配时提示重启 / 重试，不宣称升级成功。停用和解绑仍可使用已有辅助程序，不因版本检查阻碍撤销。磁盘版本不代表已经加载到 LogonUI 的 DLL 版本；仍需按安装提示重启。此改动没有确认或修复底层蓝牙连接故障，等待同版本组件真机结果。

提示窗口统一使用当前最深层可见子窗口作为模态所有者，显示时在应用窗口中激活并居中，避免新提示落在旧窗口后面；不启用全局 TopMost，也不改动游戏中的无焦点切换提示。初始化失败但历史配置保留时明确显示“此前配置仍保留，最近初始化失败”，不把旧标记描述为本次成功。

严格启动回归中曾返回 1168（未读到有效绑定），没有放宽为测试通过。读取手机地址改为校验“恰好 12 位十六进制字符且剩余均为零填充”，兼容注册表字符串写入端的终止符数量差异，仍拒绝额外非零后缀、错误类型、超长值或非法字符；增加零填充和非零后缀回归。

回归进一步确认实质缺陷：LoginDiagnostics 使用 `Phone` 作为诊断值名，与手机绑定地址 `Phone`（REG_SZ）冲突，后台写入错误记录后地址被覆盖成 REG_BINARY。所有新诊断写入改用 `Diagnostic.` 前缀，UI 保留旧记录只读兼容，并检测旧版本损坏绑定；增加诊断写入后地址不变的回归。旧地址无法从诊断记录恢复，用户需升级主程序与组件、重新绑定所选手机（会按现有流程撤销旧凭据），再初始化。错误 1326 仍是独立的账户认证失败，不能承诺重新绑定会解决它。此前关于零填充差异的调查不是这次回归失败根因。启动检查区分非交互 Session 0（预期拒绝 1062）和交互会话的错误 SID（预期拒绝 5），不把 CI 当作真实解锁验证。

修复安装恢复流程：登录组件文件支持占用时安排重启替换，主程序同时核对辅助程序及 Provider DLL 的磁盘版本，避免只更新 EXE 后仍使用旧 DLL。旧版正在内存中加载的模块也可能继续写入冲突字段，因此从受影响版本升级后请先重启，再重新绑定与初始化。

0.3.10 Preview 1 发行代码：`00aaa31a0cc5adf36815d957d48d42cf088483a4`。[Windows CI 39032287278](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39032287278) 三架构构建成功；x64 写诊断后手机地址保持不变、原生绑定读取、runas / ArgumentList 在会话 2 返回错误 SID 拒绝 5、版本匹配、弹窗所有者、安装 / 卸载与存储检查通过。旧字段覆盖问题在 CI 中复现并修复；真实 iPhone 自动解锁和用户最新 1326 原因仍需真机确认。

### 0.3.11：弹窗边缘与排版细节

移除切换提示的硬 Region 裁切及整窗半透明，避免锯齿边缘和剪贴感；普通窗口与提示共享 DWM 边缘设置，使用系统合成器的圆角提示与阴影支持，Windows 10 不强制模拟圆角。系统策略、远程桌面和虚拟机可能不绘制圆角 / 阴影，因此 CI 窗口位图不能证明真机合成效果。

提示正文放入统一白色内容区，缩短短消息窗口，长消息保留滚动；打开后清除默认文本选择并聚焦操作按钮。复制按钮提供反馈，下载进度增加一致的内容表面，统一边框与间距。切换提示的 WS_EX_NOACTIVATE、鼠标穿透、独立屏幕定位与显示时长保持原有行为；不更改认证或凭据逻辑。

参考：[Microsoft 系统圆角说明](https://learn.microsoft.com/en-us/windows/apps/desktop/modernize/ui/apply-rounded-corners)。

0.3.11 Preview 1 发行代码：`6f46e8c5593a9b10a8ab7a4b1bcec354859735ad`。[Windows CI 39199601349](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39199601349) 三架构构建、x64 安装、保存 / 确认弹窗交互、设置、登录组件检查通过；实际提示与进度窗口截图已核对。滚动条仅在窗口显示后计算一次，避免 SizeChanged 回调反复布局；按钮焦点采用圆角描边。系统合成器的外部阴影与真机 Windows 11 圆角不由 PrintWindow 位图证明。

### 0.3.12：侧边动效与启动响应

切换提示用单一 UI 定时器及 Stopwatch 计算缓动，260ms 入场、180ms 离场；停留时间在入场完成后计算。左右方向随四角选择，整个窗口始终留在目标工作区，避免多屏时进入邻屏。关闭 Windows 客户区动画或远程桌面时直接显示，保留 NOACTIVATE / HTTRANSPARENT。窗口载入时先定位，避免首帧出现在错误位置；关闭时释放两个定时器。

设置窗口先显示，音频 COM 枚举在后台创建并释放，只返回设备数据；移除打开设置时的重复枚举。状态读取最多同时一份，保存或切换使旧读取结果失效，退出后不再更新界面。蓝牙启动移至消息循环之后，设置窗口打开期间仍暂停正式锁屏。安装检查新增真实设置启动路径耗时记录；CI 热启动时间不代表用户电脑冷启动，也没有旧版本对照，不能声称加速百分比。

0.3.12 Preview 1 发行代码：`3ae84cf9829391daca3f18de0ec8632b76a95220`。[Windows CI 39375281312](https://github.com/wuha-like-sleep/AudioSwitch/actions/runs/39375281312) 三架构构建成功，x64 实际安装 / 卸载、设置交互、音频 COM 与登录组件检查通过。真实设置启动路径在该 CI 热环境中 392ms 出现可见窗口；这是窗口出现时间，不是所有设备就绪时间，无旧版对照，不能外推到用户电脑。截图已核对。CI 远程环境可能关闭动效，左右动画的体感仍需真机确认。

### 0.3.13：登录 Windows 后的自启动

专门检查 `--startup` 路径。核心托盘、快捷键及消息循环先就绪，更新模块（含 HttpClient）按需创建，自动检查更新延后 30 秒，离开锁屏监测不增加延时。新增仅在自启动时生成的 `startup-timing.json`，记录进程创建时间和各阶段耗时，诊断信息可复制；后台原子写入，后续轮询不重复写入。Windows Run 启动项可能由系统延后调度，本记录不包含进程创建前等待，不能称为从登录到可用的完整耗时。[微软说明](https://learn.microsoft.com/en-us/windows/win32/setupapi/run-and-runonce-registry-keys)。不修改系统全局启动延迟或账户权限。

安装检查实际运行 `--startup`，要求不出现设置窗口、快捷键注册成功、音频后台读取完成，并保存分阶段结果。CI 不是实际重启 / 用户登录，无法验证系统拉起前的等待时间。
