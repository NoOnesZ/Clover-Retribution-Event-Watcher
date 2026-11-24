param([string]$OutExe)

$ErrorActionPreference = "Stop"

$Root = Split-Path -Parent $MyInvocation.MyCommand.Path
$FrameworkDir = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319"
$Csc = Join-Path $FrameworkDir "csc.exe"
$WpfDir = Join-Path $FrameworkDir "WPF"
$WinMdDir = Join-Path $env:WINDIR "System32\WinMetadata"

if (-not (Test-Path $Csc)) {
    throw "csc.exe not found at $Csc"
}

if (-not $OutExe) {
    $OutExe = Join-Path $Root "RoMulti.exe"
}
$Manifest = Join-Path $Root "app.manifest"
$Icon = Join-Path $Root "app.ico"
$ExtraArgs = @()
if (Test-Path $Icon) {
    $ExtraArgs += "/win32icon:$Icon"
}

$SrcFiles = @(
    (Join-Path $Root "src\NativeMethods.cs"),
    (Join-Path $Root "src\Models.cs"),
    (Join-Path $Root "src\AfkOptimizer.cs"),
    (Join-Path $Root "src\RobloxHandleEngine.cs"),
    (Join-Path $Root "src\EventCatalog.cs"),
    (Join-Path $Root "src\RobloxLogTracker.cs"),
    (Join-Path $Root "src\ChatOcrEngine.cs"),
    (Join-Path $Root "src\DiscordNotifier.cs"),
    (Join-Path $Root "src\AntiAfkKeeper.cs"),
    (Join-Path $Root "src\MainWindow.cs"),
    (Join-Path $Root "src\Program.cs")
)

$Refs = @(
    "/r:$WpfDir\PresentationFramework.dll",
    "/r:$WpfDir\PresentationCore.dll",
    "/r:$WpfDir\WindowsBase.dll",
    "/r:$FrameworkDir\System.Xaml.dll",
    "/r:$FrameworkDir\System.Windows.Forms.dll",
    "/r:$FrameworkDir\System.Drawing.dll",
    "/r:$FrameworkDir\System.Core.dll",
    "/r:$FrameworkDir\System.Net.Http.dll",
    "/r:$FrameworkDir\System.Web.Extensions.dll",
    "/r:$FrameworkDir\System.Runtime.dll",
    "/r:$FrameworkDir\System.Runtime.WindowsRuntime.dll",
    "/r:$FrameworkDir\System.Threading.Tasks.dll",
    "/r:$WinMdDir\Windows.Foundation.winmd",
    "/r:$WinMdDir\Windows.Globalization.winmd",
    "/r:$WinMdDir\Windows.Graphics.winmd",
    "/r:$WinMdDir\Windows.Media.winmd",
    "/r:$WinMdDir\Windows.Storage.winmd"
)

Write-Host "Building RoMulti.exe (x64 WPF Native + AFK Saver + WinRT OCR Event Watcher + Anti-AFK)..."
& $Csc /nologo /target:winexe /platform:x64 /optimize+ "/win32manifest:$Manifest" @ExtraArgs "/out:$OutExe" @Refs @SrcFiles

if ($LASTEXITCODE -ne 0) {
    throw "Build failed with exit code $LASTEXITCODE"
}

$Info = Get-Item $OutExe
Write-Host "Build succeeded: $($Info.FullName) ($([Math]::Round($Info.Length / 1KB, 1)) KB)"
