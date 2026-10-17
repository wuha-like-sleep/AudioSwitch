<div align="center">

<img src="src/AudioSwitch/Assets/app.svg" width="88" height="88" alt="AudioSwitch Logo" />

# AudioSwitch

### 多个音频设备，按你的顺序切换。

为 Windows 10 / 11 打造的轻量音频切换工具。\
一个快捷键，循环切换 USB、蓝牙、HDMI 等输出设备。

[![Windows](https://img.shields.io/badge/Windows-10%20%7C%2011-465CE8?logo=windows&logoColor=white)](#安装)
[![Release](https://img.shields.io/badge/%E4%B8%8B%E8%BD%BD-v0.3.4--preview.1-465CE8)](https://github.com/wuha-like-sleep/AudioSwitch/releases)
[![Build](https://github.com/wuha-like-sleep/AudioSwitch/actions/workflows/build.yml/badge.svg)](https://github.com/wuha-like-sleep/AudioSwitch/actions/workflows/build.yml)
[![License](https://img.shields.io/badge/License-MIT-22A77A)](LICENSE)

**[下载安装版](https://github.com/wuha-like-sleep/AudioSwitch/releases) · [查看功能](#不止是切换) · [反馈问题](https://github.com/wuha-like-sleep/AudioSwitch/issues)**

开发者 **[wuha-like-sleep](https://github.com/wuha-like-sleep)**

</div>

<br />

![AudioSwitch 设置界面](docs/images/settings-0.3.4-preview1.png)

<div align="center"><sub>Windows 实际运行截图 · 展示使用示例设备</sub></div>

<br />

<details>
<summary>查看设备管理、手机锁屏、更新与安装界面</summary>

**管理输出设备**

![多设备循环列表](docs/images/devices-0.3.4-preview1.png)

**手机离开锁屏**

![蓝牙手机锁屏设置](docs/images/proximity-0.3.4-preview1.png)

<sub>手机状态为界面演示数据，真实连接和锁屏需在配对手机上验证。</sub>

**解锁链路研究检测**

![解锁链路检测界面](docs/images/unlock-0.3.4-preview1.png)

<sub>手动检测加密蓝牙连接，尚未实现 Windows 自动解锁；截图不代表真实 iPhone 检测通过。</sub>

**Windows 登录实验组件**

![原生登录组件说明与安装入口](docs/images/login-0.3.4-preview1.png)

<sub>说明页截图；登录页中的真实微软账户认证仍需真机验证。</sub>

**更新下载**

![更新下载界面](docs/images/update-0.3.4-preview1.png)

**版本检查结果**

![版本提示界面](docs/images/dialog-0.3.4-preview1.png)

**提示样式**

![提示位置与停留时间](docs/images/style-0.3.4-preview1.png)

**切换提示**

![切换提示](docs/images/toast-0.3.4-preview1.png)

**安装向导**

![安装向导](docs/images/installer-0.3.4-preview1.png)

</details>

## 不止是切换

| 一键切换 | 按你的习惯 | 保持专注 |
| :--- | :--- | :--- |
| 托盘点击或全局快捷键，按列表循环切换在线设备。 | 勾选任意数量的输出设备，自定义快捷键与循环顺序。 | 切换提示可固定到指定屏幕，不抢焦点、不拦截鼠标。 |
| **登录即用** | **更新更方便** | **轻量安装** |
| 可选开机自启动，登录后安静地留在托盘。 | 检查新版本、查看说明、下载安装包并校验。 | 当前用户安装，无需管理员权限，自带运行组件。 |
| **手机离开锁屏** | **信号阈值与测试** | **设备管理** |
| 可选断连或实时弱信号判定，持续离开后锁屏，托盘可暂停。 | 自定义阈值与恢复缓冲、参考标定，以及不会锁屏的测试模式。 | 名称搜索、循环排序、按筛选结果批量加入或移出。 |

## 安装

前往 **[下载页面](https://github.com/wuha-like-sleep/AudioSwitch/releases)**，选择适合你的安装包：

| 你的电脑 | 下载 |
| :--- | :--- |
| Intel / AMD 64 位电脑 · 大多数用户选择此项 | **[Windows x64](https://github.com/wuha-like-sleep/AudioSwitch/releases/download/v0.3.4-preview.1/AudioSwitch-Setup-win-x64.exe)** |
| Windows ARM64 电脑 | [Windows ARM64](https://github.com/wuha-like-sleep/AudioSwitch/releases/download/v0.3.4-preview.1/AudioSwitch-Setup-win-arm64.exe) |
| Windows 10 32 位电脑 | [Windows x86](https://github.com/wuha-like-sleep/AudioSwitch/releases/download/v0.3.4-preview.1/AudioSwitch-Setup-win-x86.exe) |

运行安装向导，选择安装位置、自启动和桌面快捷方式。程序默认安装到当前用户目录，安装完成后从开始菜单打开 **AudioSwitch**。

> 当前为公开测试版。Windows 自动安装、启动和卸载检查已通过；USB 音频切换和游戏兼容性仍需在 Windows 10 / 11 真机验证。

## 可选：Windows 登录实验组件

普通安装包只安装音频切换程序。**登录实验组件需单独安装、需要管理员权限，建议先在具备恢复手段的测试电脑上验证。** 它增加独立的 Windows Credential Provider，保留系统 Windows Hello / PIN 等入口，不设为默认、不自动登录。

| 账户 / 登录方式 | 实验组件状态 |
| :--- | :--- |
| 具备可用密码的个人微软账户 | 已实现账户 SID 绑定及受保护凭据提交；真实登录尚待验证 |
| Windows Hello / PIN | 继续使用系统入口；不代理、读取或重放 PIN / 生物识别 |
| 无密码微软账户、组织账户、本地账户 | 本次实验不支持，继续使用原有系统登录方式 |
| iPhone 靠近自动解锁 | 未实现；不能仅凭蓝牙连接或信号宣称完成 |

在 **[0.3.4 实验版下载页](https://github.com/wuha-like-sleep/AudioSwitch/releases/tag/v0.3.4-preview.1)** 选择匹配系统原生架构的 `AudioSwitch-LoginLab-Setup-win-*.exe`。安装后在 Windows 登录页的“登录选项”中选择“AudioSwitch 登录实验”，手动输入微软账户密码；失败时切回原有 PIN / Hello。密码不保存，提交、取消或失败后清除。不要把 PIN 当账户密码，也不要向开发者发送任何登录凭据。

卸载 Windows“已安装的应用”中的 **AudioSwitch 登录实验组件** 可移除它自己的注册，可能需要重启释放 DLL。独立卸载音频切换程序不会卸载该实验组件。[开发与验证范围](docs/DEVELOPMENT.md)

## 三步开始使用

1. **管理设备** — 连接输出设备，点击“管理设备”勾选参加循环的设备，并用上下按钮调整顺序。可搜索名称，并批量加入或移出当前筛选结果。
2. **设置快捷键** — 保留默认 `Ctrl + Alt + F11`，或点击输入框直接按下新组合键。
3. **保存，开始切换** — 点击托盘图标，或按下快捷键。设置页的“立即切换”可测试已保存的配置，托盘右键菜单可直接选择某台设备。

程序在系统托盘运行，图标可能位于 Windows 的隐藏图标区域。双击桌面或开始菜单快捷方式即可打开设置；已运行时会直接显示原窗口，也可从托盘打开。

## 用起来需要知道的事

**关于多屏提示。** 设置页的“切换提示屏幕”默认跟随系统主屏，也可指定某一块显示器。点击“样式”可用屏幕示意区域选择四个角落，停留时间可选 1.5、2.2、3、5 秒；“预览”可在保存前确认真实效果。屏幕选择会保存；指定屏幕断开时临时回退主屏，保留原选择。Windows 若重新编号显示器，可刷新并重新选择。

**关于游戏。** 可在设置中关闭“显示切换提示”，成功切换保持安静，错误仍会提醒；“预览”可随时测试提示位置。窗口化和无边框全屏是提示窗口的主要使用场景。提示不抢焦点、不拦截鼠标；独占全屏游戏可能遮住桌面提示。游戏或通话软件固定了独立输出时，需要先将其改为“系统默认”。

**关于快捷键。** 支持 `Ctrl` 或 `Alt` 加字母、数字、`F1`–`F11`，可加 `Shift`。被占用时保留原快捷键，设置窗口保持打开并保留刚才的选择。Windows 保留的 `F12` 不可用。不支持的按键会显示原因；“重置”可恢复默认组合，保存后生效。

**关于自启动。** 安装向导、设置页和托盘菜单均可设置。登录后在托盘运行；如果已在 Windows 任务管理器中禁用启动项，需要在那里重新开启。

**关于设备。** 同时切换播放和通信的默认输出，不修改麦克风或音量。当前循环设备断开时，尝试按列表顺序回退到可用设备；重新连接不会抢回输出。设置页自动刷新连接状态并显示当前输出；已选设备断开后保留配置，仍可保存快捷键等偏好。托盘菜单勾选当前输出，未连接设备不可点击。只有一台可用时选择可用设备，已在使用时给出提示。更换 USB 端口或驱动后，可刷新并重新选择设备。

**关于手机离开锁屏。** 从设置页右上角进入“离开锁屏”。先在 Windows 配对手机，确认页面显示连接状态；信号模式还需确认能收到实时信号。选择断开连接、弱信号或任一条件，持续时间可选 15、30、60、120 秒。点击完成后仍需在主设置页保存，并关闭设置窗口才开始监测。功能默认关闭，托盘可随时暂停。

**关于信号阈值。** 信号阈值支持 -120 至 -20 的自定义负值，恢复缓冲可选 1–20；倒计时开始后，信号需越过恢复线才取消，避免在阈值附近反复波动。状态区同时展示当前信号、触发阈值和恢复条件。“参考标定”使用最近 30 秒内最多 9 次实时读数，至少需要 3 次，取中位数减 15 作为可调整的参考阈值；当前信号过期时不进行标定。开启前可点击“测试判定”观察连接、实时信号和倒计时；测试不会执行锁屏，点击“停止测试”结束。

启动、解锁或休眠恢复后需先检测到手机可用；瞬间断连不会立即锁屏，条件恢复会取消倒计时。设置编辑、休眠、已锁屏或监测异常期间暂停。只调用 Windows 锁屏，不自动解锁。信号值不等于精确距离；仅使用实时负值信号，过期或不可用时暂停信号判定，不能保证所有手机都支持。[Windows 信号属性说明](https://learn.microsoft.com/en-us/windows/win32/properties/props-system-devices-aep-signalstrength)

**关于手机解锁。** 当前未实现自动解锁或手机确认解锁。0.3.4 新增独立的原生 Windows 登录实验组件，可手动提交微软账户密码，尚未经真实微软账户登录验证。返回电脑后请使用 Windows Hello、PIN 或密码；取消离开倒计时不会解除已经锁定的会话。手机锁屏设置中的“解锁检测”可手动尝试建立要求认证的加密蓝牙链路，并显示结果、超时或错误；无需手机 App，不输入密码或 PIN。通过只代表蓝牙链路，不能解锁。仅 iPhone 蓝牙配对的自动解锁仍缺少有效账户真机认证、手机身份协议、受保护后台服务与锁定会话验证。[研究进展](docs/DEVELOPMENT.md)

**关于诊断。** 设置页的“诊断信息”可复制软件版本、系统架构、快捷键注册和音频设备状态，便于反馈问题。不会上传信息；分享前请检查设备名称。

**关于更新。** 启动时检查版本只通知，不弹出安装对话框。从设置页或托盘选择“检查更新”，检查时立即显示进度，支持取消和连接超时提示。确认后下载并校验 SHA-256，再打开新版安装向导。测试版会检查后续测试版和正式版；正式版仅检查正式更新。

**关于配置。** 关闭有未保存修改的设置窗口时，会先询问是否放弃，默认继续编辑。设置保存在 `%LOCALAPPDATA%\AudioSwitch\settings.json`。更新请求不上传设备配置。卸载会清理程序、自启动项和快捷方式，保留配置以便重装。

## 开发与反馈

使用 C# / WinForms、原生 C++ 和 Inno Setup 构建，源码以 **[MIT](LICENSE)** 许可证开放。

- [开发、构建与发布说明](docs/DEVELOPMENT.md)
- [报告问题](https://github.com/wuha-like-sleep/AudioSwitch/issues)
- [参与改进](https://github.com/wuha-like-sleep/AudioSwitch/pulls)

报告问题时请提供 Windows 版本、CPU 架构、连接方式、操作步骤和错误提示，无需上传个人配置文件。

<details>
<summary>技术细节与验证范围</summary>

目标系统为 Windows 10 22H2 与 Windows 11。
使用 Windows Core Audio 枚举设备，使用未公开的 `IPolicyConfig` COM 接口设置默认输出；Windows 更新后的兼容性需要验证。
三个默认角色依次设置并读回确认，不是原子操作，失败时可能只改变部分默认角色。

Windows CI 验证音频接口、配置保存与覆盖、写入失败保护、快捷键、自启动、版本通道、更新取消、安装及卸载，并检查应用和安装器的界面截图。发行前仍需验证实际音频播放、USB/蓝牙拔插、手机离开锁屏、重新登录、快捷键冲突、游戏场景和显示缩放。
应用与安装器尚未配置代码签名证书。

</details>
