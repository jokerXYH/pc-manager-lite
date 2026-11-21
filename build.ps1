$ErrorActionPreference = "Stop"

# ============================================================
# 版本号唯一来源：
# 发版只改 $Version 与 $ReleaseNotes；程序集特性(src\AssemblyInfo.cs)、
# src\app.manifest、README.md 的版本行、dist\version.js、dist\version.json
# 与 dist\SHA256.txt 都由本脚本自动生成/同步。
# ============================================================
$Version = "2.7.0"
$ReleaseNotes = @(
    "一键体检与系统状态读取改为后台执行，界面切换更流畅",
    "启动项、恢复中心、计划任务查询等操作改为后台执行，不再卡住界面",
    "修复 Defender 病毒库更新与扫描在错误输出较多时可能无响应的问题",
    "更新安装后自动清理旧版本 .bak；本地清理日志与帧采样按保留期自动清理",
    "修正无 D 盘机器体检分被误扣；软件管理搜索改为行可见性过滤，输入更流畅"
)

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = $PSScriptRoot
$outputDirectory = Join-Path $root "dist"
$output = Join-Path $outputDirectory "pc-manager-lite.exe"
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "assets\app.ico"

$fourPart = $Version
if (($Version.Split('.').Count) -lt 4) {
    $fourPart = "$Version.0"
}

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器: $compiler"
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw "找不到应用程序图标: $icon"
}

# ---- 生成程序集特性（版本号不再硬编码在源码里） ----
$assemblyInfo = Join-Path $root "src\AssemblyInfo.cs"
$assemblyInfoText = @"
using System.Reflection;

[assembly: AssemblyTitle("电脑管家 Lite")]
[assembly: AssemblyDescription("轻量清理、软件管理、安全防护与游戏加速工具")]
[assembly: AssemblyProduct("电脑管家 Lite")]
[assembly: AssemblyCompany("Joker X")]
[assembly: AssemblyCopyright("Copyright © 2026 Joker X")]
[assembly: AssemblyTrademark("jokerx.top")]
[assembly: AssemblyVersion("$fourPart")]
[assembly: AssemblyFileVersion("$fourPart")]
"@
[System.IO.File]::WriteAllText($assemblyInfo, $assemblyInfoText, (New-Object System.Text.UTF8Encoding($true)))

# ---- 同步 app.manifest 的 assemblyIdentity 版本 ----
$manifestText = [System.IO.File]::ReadAllText($manifest)
$newManifest = [regex]::Replace(
    $manifestText,
    'version="\d+(\.\d+){3}"',
    'version="' + $fourPart + '"')
if ($newManifest -ne $manifestText) {
    [System.IO.File]::WriteAllText($manifest, $newManifest)
    Write-Host "app.manifest 版本已同步为 $fourPart"
}

# ---- 同步 README 当前版本行 ----
$readmePath = Join-Path $root "README.md"
if (Test-Path -LiteralPath $readmePath) {
    $readmeText = [System.IO.File]::ReadAllText($readmePath)
    # README 的版本行格式为：当前版本：`X.Y.Z`（Markdown 反引号包裹）
    $newReadme = [regex]::Replace(
        $readmeText,
        '当前版本：`[^`\r\n]+`',
        '当前版本：`' + $Version + '`')
    if ($newReadme -ne $readmeText) {
        [System.IO.File]::WriteAllText($readmePath, $newReadme)
        Write-Host "README 版本已同步为 $Version"
    }
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

$sources = @(
    (Join-Path $root "src\ManagerServices.cs"),
    (Join-Path $root "src\ManagerUi.cs"),
    $assemblyInfo
)

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /codepage:65001 `
    /win32manifest:$manifest `
    /win32icon:$icon `
    /out:$output `
    /reference:System.dll `
    /reference:System.Core.dll `
    /reference:System.Drawing.dll `
    /reference:System.Management.dll `
    /reference:System.Web.Extensions.dll `
    /reference:System.Windows.Forms.dll `
    $sources

if ($LASTEXITCODE -ne 0) {
    throw "编译失败，退出代码: $LASTEXITCODE"
}

# ---- 生成发布清单（version.js / version.json / SHA256.txt） ----
$exeItem = Get-Item -LiteralPath $output
$sha256 = (Get-FileHash -LiteralPath $output -Algorithm SHA256).Hash.ToLowerInvariant()
$notesJson = ($ReleaseNotes | ForEach-Object {
    '    "' + ($_ -replace '"', '\"') + '"'
}) -join ",`r`n"

$versionJsText = @"
window.PC_MANAGER_LITE_RELEASE = {
  "versionName": "$Version",
  "size": $($exeItem.Length),
  "sha256": "$sha256",
  "downloadUrl": "https://jokerx.top/downloads/pc-manager-lite/pc-manager-lite.exe",
  "releaseNotes": [
$notesJson
  ]
};
"@
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory "version.js"),
    $versionJsText,
    (New-Object System.Text.UTF8Encoding($false)))

$versionJsonText = @"
{
  "versionName": "$Version",
  "size": $($exeItem.Length),
  "releaseNotes": [
$notesJson
  ]
}
"@
[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory "version.json"),
    $versionJsonText,
    (New-Object System.Text.UTF8Encoding($false)))

[System.IO.File]::WriteAllText(
    (Join-Path $outputDirectory "SHA256.txt"),
    "$sha256  pc-manager-lite.exe",
    (New-Object System.Text.UTF8Encoding($false)))

# 可选代码签名：设置 SIGN_PFX / SIGN_PASSWORD 后自动签名，减少 SmartScreen 警告。
if ($env:SIGN_PFX -and $env:SIGN_PASSWORD -and (Test-Path -LiteralPath $env:SIGN_PFX)) {
    $signtool = Get-ChildItem "$env:ProgramFiles(x86)\Windows Kits\10\bin" -Recurse -Filter signtool.exe -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -match '\\x64\\' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $signtool) {
        Write-Warning "未找到 signtool.exe，跳过签名。请安装 Windows SDK。"
    } else {
        & $signtool.FullName sign /fd SHA256 /f $env:SIGN_PFX /p $env:SIGN_PASSWORD `
            /tr http://timestamp.digicert.com /td SHA256 $output
        if ($LASTEXITCODE -ne 0) {
            throw "代码签名失败，退出代码: $LASTEXITCODE"
        }
        Write-Host "已使用 $($env:SIGN_PFX) 完成代码签名。"
    }
}

Write-Host ""
Write-Host "版本 $Version 构建完成："
Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
