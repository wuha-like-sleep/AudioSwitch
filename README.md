# AudioSwitch

**耳机与音箱，一键切换。**

Windows 10 / 11 托盘音频切换工具，支持 USB 耳机、USB 音箱、自定义全局快捷键和不抢焦点的切换提示。

开发者：**wuha-like-sleep** · 许可证：[MIT](LICENSE)

## 安装与使用

在 [Releases](https://github.com/wuha-like-sleep/AudioSwitch/releases) 下载对应的安装包：

| 电脑类型 | 安装包 |
| --- | --- |
| 常见 Intel / AMD 64 位电脑 | `AudioSwitch-Setup-win-x64.exe` |
| Windows ARM64 电脑 | `AudioSwitch-Setup-win-arm64.exe` |
| Windows 10 32 位电脑 | `AudioSwitch-Setup-win-x86.exe` |

运行安装向导，选择安装位置、自启动及桌面快捷方式。程序默认安装到当前用户目录，无需管理员权限，安装包包含运行所需的 .NET 组件。

首次启动后，在设置页分别选择耳机和音箱并保存。日常使用有三种方式：

- **托盘左键**：在耳机和音箱之间切换。
- **全局快捷键**：默认为 `Ctrl + Alt + F11`，可在设置页直接按下新组合键。
- **托盘右键**：直接选中耳机或音箱，打开设置或检查更新。

设置保存于 `%LOCALAPPDATA%\AudioSwitch\settings.json`。程序在系统托盘运行，图标可能被 Windows 收到隐藏图标区域。

## 功能

### 自启动

安装时可选择“登录 Windows 后自动启动”，也可在设置页和托盘菜单中开启或关闭。
自启动只对当前用户生效；登录后在托盘运行，不主动打开设置窗口。卸载会清理自启动项和快捷方式，保留设备配置。
如果在 Windows 任务管理器中禁用了启动项，请在系统设置中重新启用；应用不会绕过系统的禁用状态。

### 自定义快捷键

点击快捷键框，按下 `Ctrl` 或 `Alt` 加字母、数字、`F1`–`F11`，可同时使用 `Shift`。
保存时检测是否被占用，不可用时保留原快捷键。`F12` 不支持，因为 Windows 将其保留给调试器。

### 游戏中的切换提示

切换成功后，在当前前台窗口所在显示器的右下角短暂显示输出设备。
提示窗口不抢焦点、不接收鼠标点击，不需要音频通知声。

窗口化和无边框全屏为主要支持场景。独占全屏游戏可能遮住桌面提示，不能保证显示；不包含游戏进程注入或渲染挂钩。
游戏使用独立输出或独占音频时，也可能不跟随系统默认输出变化。

### 更新

启动时可自动检查 GitHub 正式版本，仅通知有更新，不弹出安装对话框。
在托盘菜单选择“检查更新”，查看版本与更新内容，确认后下载安装包，校验 GitHub 提供的 SHA-256，再退出应用并打开安装向导。
下载支持进度显示和取消；安装升级保留设备及快捷键配置。只有发布为正式 Release 的版本进入更新渠道，草稿和预发布版本不参与。

更新请求仅访问 GitHub 获取版本与安装包，不上传设备配置。尚未发布正式版本时会提示暂无可用版本。

## 设备行为

设备列表显示所有已启用的输出设备，请选择你的两台 USB 设备。按 Windows endpoint ID 保存，同名设备附带 ID 后缀。
换 USB 端口或重装驱动后 ID 可能变化，可刷新列表并重新选择。

同时切换普通播放、媒体和通信的默认输出，不改变麦克风或音量。
部分应用固定了输出设备，需要将应用输出设置为“系统默认”或重启播放。

每 2.5 秒刷新状态。当前配置设备消失且另一台可用时尝试回退；两台均断开时不任意选择其他设备。
重新连接耳机不会自动抢回输出。

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
2. 推送同版本标签，如 `v0.2.0`。
3. 自动构建完成后下载安装包，在 Windows 真机验证。
4. 创建同版本正式 GitHub Release，上传三个 `AudioSwitch-Setup-*.exe` 安装包并填写更新说明。

GitHub 为上传的资产生成 SHA-256 digest。只有包含校验信息且架构匹配的安装包才能自动更新。
应用与安装器尚未配置代码签名证书。

## 验证与兼容性

目标系统为 Windows 10 22H2 与 Windows 11。交叉编译通过不能替代 Windows 真机验证。

发行前请分别验证：

- [ ] 安装向导、快捷方式和“已安装的应用”卸载入口。
- [ ] 选择自启动后重新登录，应用静默进入托盘；关闭后不再启动。
- [ ] 升级安装保留设备及快捷键设置；卸载删除自启动和程序文件。
- [ ] 两台设备双向切换，普通默认输出与通信默认输出一致。
- [ ] 自定义快捷键生效，冲突时保留原快捷键，退出后释放占用。
- [ ] 游戏无边框全屏中显示提示，不抢焦点、不截断鼠标操作。
- [ ] USB 拔插、同名设备、损坏配置和写入失败时能正确处理。
- [ ] 更新检查、无网络、无正式版本、取消下载和校验失败。
- [ ] 普通用户权限、100% / 150% / 200% 缩放和多显示器布局。

## 实现

C# / WinForms，无第三方应用运行库依赖。

- [IMMDeviceEnumerator](https://learn.microsoft.com/en-us/windows/win32/api/mmdeviceapi/nn-mmdeviceapi-immdeviceenumerator)：枚举和读取默认音频设备。
- [RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)：全局快捷键。
- 未公开的 `IPolicyConfig` COM 接口：设置默认输出。声明集中在 `AudioDevices.cs`，系统更新后的兼容性需要验证。
- [Inno Setup](https://jrsoftware.org/ishelp/)：当前用户安装与卸载。
- [GitHub Releases API](https://docs.github.com/en/rest/releases/releases#get-the-latest-release)：版本检查和安装包校验信息。

三个默认角色依次设置并读回确认，不是原子操作；失败时可能只有部分角色改变，程序会提示检查 Windows 声音设置。

欢迎提交 Issue 和 Pull Request。报告问题时请提供 Windows 版本、CPU 架构、设备连接方式、操作步骤与错误信息，无需上传个人配置。
