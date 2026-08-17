$ErrorActionPreference = "Stop"

$compiler = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$source = Join-Path $PSScriptRoot "gui\ProgramMulti.cs"
$manifest = Join-Path $PSScriptRoot "gui\app.manifest"
$output = Join-Path $PSScriptRoot "InstagramProfileChecker.exe"
$assets = Join-Path $PSScriptRoot "assets"
$iconSource = Join-Path $PSScriptRoot "tools\IconBuilder.cs"
$icon = Join-Path $assets "InstagramProfileChecker.ico"
$iconPreview = Join-Path $assets "InstagramProfileChecker.png"
$iconBuilder = Join-Path ([IO.Path]::GetTempPath()) "InstagramProfileChecker.IconBuilder.exe"

if (-not (Test-Path -LiteralPath $compiler)) {
    Write-Error "Der Windows C#-Compiler wurde nicht gefunden."
}

if (-not (Test-Path -LiteralPath $assets)) {
    New-Item -ItemType Directory -Path $assets | Out-Null
}

& $compiler `
    /nologo `
    /target:exe `
    /optimize+ `
    "/out:$iconBuilder" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    $iconSource

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $iconBuilder $icon $iconPreview

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

& $compiler `
    /nologo `
    /target:winexe `
    /optimize+ `
    /platform:anycpu `
    "/out:$output" `
    "/win32manifest:$manifest" `
    "/win32icon:$icon" `
    /reference:System.dll `
    /reference:System.Drawing.dll `
    /reference:System.Windows.Forms.dll `
    /reference:System.Web.Extensions.dll `
    $source

if ($LASTEXITCODE -ne 0) {
    exit $LASTEXITCODE
}

Write-Host "Erstellt: $output" -ForegroundColor Green
