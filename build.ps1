$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$manifest = Join-Path $PSScriptRoot "app.manifest"
$outputDirectory = Join-Path $projectRoot "outputs"
$output = Join-Path $outputDirectory "电脑管家-Lite-2.3.exe"
$sources = @(
    (Join-Path $PSScriptRoot "ManagerServices.cs"),
    (Join-Path $PSScriptRoot "ManagerUi.cs")
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
