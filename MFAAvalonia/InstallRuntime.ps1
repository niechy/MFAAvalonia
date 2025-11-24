#Requires -RunAsAdministrator
[Console]::OutputEncoding = [System.Text.Encoding]::UTF8
$ErrorActionPreference = "Stop"

# 颜色输出函数
function Write-Color {
    param(
        [string]$Text,
        [ConsoleColor]$Color = [ConsoleColor]::White,
        [switch]$Bold
    )
    $originalColor = $Host.UI.RawUI.ForegroundColor

    Write-Host $Text -ForegroundColor $Color
    
    $Host.UI.RawUI.ForegroundColor = $originalColor
}


Write-Color -Text "如果是第一次使用 winget，可能会提示接受协议，请输入 Y 并按回车继续" -Color Yellow
Write-Color -Text "If this is your first time using winget, you may be prompted to accept the terms." -Color Yellow
Write-Color -Text "Please enter Y and press Enter to continue." -Color Yellow
$osVersion = [System.Environment]::OSVersion
if ($osVersion.Version.Major -eq 10) {
        Write-Color -Text "正在注册WinGet系统组件，请稍候..." -Color Yellow
        Add-AppxPackage -RegisterByFamilyName -MainPackage Microsoft.DesktopAppInstaller_8wekyb3d8bbwe
        Write-Color -Text "WinGet包管理器注册完成" -Color Green
} else {
        Write-Color -Text "当前系统非Windows 10，跳过WinGet注册步骤" -Color Cyan
}

# 1. 检测系统架构
Write-Color -Text "========== 检测系统架构 ==========" -Color Cyan
$osArch = (Get-CimInstance -ClassName Win32_OperatingSystem).OSArchitecture
$arch = if ($osArch -eq "ARM64") { "arm64" } else { "x64" }
Write-Color -Text "检测到系统架构：$arch" -Color Green

# 3. 安装Microsoft Visual C++ Redistributable
Write-Color -Text "`n========== 安装VC++ Redistributable ==========" -Color Cyan
$vcPackage = if ($arch -eq "arm64") { "Microsoft.VCRedist.2015+.arm64" } else { "Microsoft.VCRedist.2015+.x64" }
try {
    Write-Color -Text "正在安装 $vcPackage ..." -Color Yellow
    winget install --id $vcPackage --override "/repair /passive /norestart" --uninstall-previous --accept-package-agreements --force
    Write-Color -Text "VC++ Redistributable安装完成" -Color Green
}
catch {
    Write-Color -Text "VC++ Redistributable安装失败：$($_.Exception.Message)" -Color Red
    $errorOccurred = $true
}

# 4. 安装.NET Desktop Runtime 10
Write-Color -Text "`n========== 安装.NET Desktop Runtime 10 ==========" -Color Cyan
$dotnetPackage = if ($arch -eq "arm64") { "Microsoft.DotNet.DesktopRuntime.10.arm64" } else { "Microsoft.DotNet.DesktopRuntime.10" }
try {
    Write-Color -Text "正在安装 $dotnetPackage ..." -Color Yellow
    winget install --id $dotnetPackage --override "/repair /passive /norestart" --uninstall-previous --accept-package-agreements --force
    Write-Color -Text ".NET Desktop Runtime 10安装完成" -Color Green
}
catch {
    Write-Color -Text ".NET Desktop Runtime 10安装失败：$($_.Exception.Message)" -Color Red
    $errorOccurred = $true
}

# 5. 输出结果与手动下载链接
Write-Color -Text "`n========== 安装结果 ==========" -Color Cyan
if (-not $errorOccurred) {
    Write-Color -Text "运行库修复完成！请重启电脑后再次尝试运行MFA。" -Color Green
    Write-Color -Text "The runtime library repair is complete. Please restart your computer and try running MAA again." -Color Green
}
else {
    Write-Color -Text "安装过程中出现错误，请手动下载以下组件安装：" -Color Red
    # VC++ Redist下载链接
    $vcUrl = if ($arch -eq "arm64") { "https://aka.ms/vs/18/release/vc_redist.arm64.exe" } else { "https://aka.ms/vs/18/release/vc_redist.x64.exe" }
    Write-Color -Text "`nMicrosoft Visual C++ Redistributable ($arch)：" -Color White
    Write-Color -Text $vcUrl -Color Cyan
    # .NET 10运行库下载链接
    $dotnetUrl = if ($arch -eq "arm64") { "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-arm64.exe" } else { "https://aka.ms/dotnet/10.0/windowsdesktop-runtime-win-x64.exe" }
    Write-Color -Text "`n.NET Desktop Runtime 10 ($arch)：" -Color White
    Write-Color -Text $dotnetUrl -Color Cyan
}

# 等待用户按键退出
Write-Color -Text "`n按任意键退出..." -Color Yellow
$null = $Host.UI.RawUI.ReadKey("NoEcho,IncludeKeyDown")