param(
    [ValidateSet("win-x64", "win-arm64", "win-x86")]
    [string]$Runtime = "win-x64",
    [string]$Repository = "",
    [switch]$SkipInstaller
)
$ErrorActionPreference = "Stop"
if ($Repository -and $Repository -notmatch '^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$') { throw "Repository must be owner/repo" }
$project = Join-Path $PSScriptRoot "src/AudioSwitch/AudioSwitch.csproj"
$output = Join-Path $PSScriptRoot "artifacts/$Runtime"
if (Test-Path $output) { Remove-Item $output -Recurse -Force }
if (-not $SkipInstaller) {
$nativeArchitecture = @{ "win-x64" = "x64"; "win-arm64" = "ARM64"; "win-x86" = "Win32" }[$Runtime]
$nativeOutput = Join-Path $PSScriptRoot "artifacts/login-$Runtime"
cmake -S (Join-Path $PSScriptRoot "native/LoginProvider") -B $nativeOutput -A $nativeArchitecture
if ($LASTEXITCODE -ne 0) { throw "Login component configuration failed" }
cmake --build $nativeOutput --config Release
if ($LASTEXITCODE -ne 0) { throw "Login component build failed" }
    if ($Runtime -eq 'win-x64' -and $env:GITHUB_ACTIONS -eq 'true') {
        & (Join-Path $nativeOutput 'Release/LoginProviderTests.exe') (Join-Path $nativeOutput 'Release/AudioSwitch.LoginProvider.dll')
        if ($LASTEXITCODE -ne 0) { throw 'Native login contract checks failed before packaging' }
    }
}
dotnet publish $project -c Release -r $Runtime --self-contained true -p:PublishSingleFile=true -p:DebugType=None -p:DebugSymbols=false "-p:GitHubRepository=$Repository" -o $output
if ($LASTEXITCODE -ne 0) { throw "Publish failed ($LASTEXITCODE)" }
Copy-Item (Join-Path $PSScriptRoot "LICENSE") $output
Copy-Item (Join-Path $PSScriptRoot "README.md") $output
if ($SkipInstaller) { Write-Host "Application files: $output"; exit 0 }
$iscc = Get-Command ISCC.exe -ErrorAction SilentlyContinue
$compiler = if ($iscc) { $iscc.Source } else { Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6/ISCC.exe" }
if (-not (Test-Path $compiler)) { throw "Install Inno Setup 6.5+ or use -SkipInstaller for development builds." }
[xml]$projectXml = Get-Content $project
$version = $projectXml.Project.PropertyGroup.Version | Select-Object -First 1
& $compiler "/DRuntime=$Runtime" "/DAppVersion=$version" "/DSourceDir=$output" (Join-Path $PSScriptRoot "installer/AudioSwitch.iss")
if ($LASTEXITCODE -ne 0) { throw "Installer compilation failed ($LASTEXITCODE)" }
Write-Host "Installer: artifacts/installer/AudioSwitch-Setup-$Runtime.exe"
& $compiler "/DRuntime=$Runtime" "/DAppVersion=$version" "/DSourceDir=$nativeOutput/Release" (Join-Path $PSScriptRoot "installer/LoginLab.iss")
if ($LASTEXITCODE -ne 0) { throw "Login component installer failed" }
