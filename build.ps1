$ErrorActionPreference = "Stop"

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$root = $PSScriptRoot
$outputDirectory = Join-Path $root "dist"
$output = Join-Path $outputDirectory "pc-manager-lite.exe"
$manifest = Join-Path $root "src\app.manifest"
$sources = @(
    (Join-Path $root "src\ManagerServices.cs"),
    (Join-Path $root "src\ManagerUi.cs")
)

if (-not (Test-Path -LiteralPath $compiler)) {
    throw "找不到 .NET Framework C# 编译器: $compiler"
}

New-Item -ItemType Directory -Path $outputDirectory -Force | Out-Null

& $compiler `
    /nologo `
    /target:winexe `
    /platform:x64 `
    /optimize+ `
    /codepage:65001 `
    /win32manifest:$manifest `
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

Get-Item -LiteralPath $output | Select-Object FullName, Length, LastWriteTime
