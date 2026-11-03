$ErrorActionPreference = "Stop"

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = $PSScriptRoot
$outputDirectory = Join-Path $root "dist"
$output = Join-Path $outputDirectory "pc-manager-lite.exe"
$manifest = Join-Path $root "src\app.manifest"
$icon = Join-Path $root "assets\app.ico"
$sources = @(
    (Join-Path $root "src\ManagerServices.cs"),
    (Join-Path $root "src\ManagerUi.cs")
)

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器: $compiler"
}
if (-not (Test-Path -LiteralPath $icon)) {
    throw "找不到应用程序图标: $icon"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

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

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
