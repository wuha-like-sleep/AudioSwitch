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
2. 推送同版本标签，如 `v0.2.1`。
3. 自动构建完成后下载安装包，在 Windows 真机验证。
4. 创建同版本正式 GitHub Release，上传三个 `AudioSwitch-Setup-*.exe` 安装包并填写更新说明。

GitHub 为上传的资产生成 SHA-256 digest。只有包含校验信息且架构匹配的安装包才能自动更新。
应用与安装器尚未配置代码签名证书。


## 安装器检查

`tests/installer-smoke.ps1` 用于独立 Windows CI 环境，验证静默安装、自启动注册、界面预览、进程运行和卸载清理。该脚本会实际安装和卸载应用，不应在个人日常环境中运行。

公开测试版使用 `v主版本.次版本.修订号-preview.序号` 标签，并在项目的 `InformationalVersion` 中设置相同版本标识。测试版更新检查同时接收测试版和正式版，正式版仅接收正式更新。
