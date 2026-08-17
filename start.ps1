param(
    [switch]$Once,
    [switch]$Demo
)

$ErrorActionPreference = "Stop"

if (-not $Demo -and -not (Test-Path -LiteralPath ".env")) {
    Write-Host "Fehlende .env-Datei. Kopiere zuerst .env.example nach .env und trage deinen RapidAPI-Key ein." -ForegroundColor Yellow
    exit 1
}

$pythonCommand = Get-Command python -ErrorAction SilentlyContinue
if (-not $pythonCommand) {
    $pythonCommand = Get-Command py -ErrorAction SilentlyContinue
}

$bundledPython = Join-Path $env:USERPROFILE ".cache\codex-runtimes\codex-primary-runtime\dependencies\python\python.exe"

if ($pythonCommand) {
    $pythonExecutable = $pythonCommand.Source
} elseif (Test-Path -LiteralPath $bundledPython) {
    $pythonExecutable = $bundledPython
} else {
    Write-Host "Python wurde nicht gefunden. Installiere Python 3 oder starte das Projekt aus Codex." -ForegroundColor Red
    exit 1
}

$arguments = @(".\monitor.py")
if ($Once) {
    $arguments += "--once"
}
if ($Demo) {
    $arguments += "--demo"
}

& $pythonExecutable @arguments
