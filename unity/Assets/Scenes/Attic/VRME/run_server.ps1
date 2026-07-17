$ErrorActionPreference = "Stop"

# Keep this legacy scene launcher, but always delegate to the single canonical
# backend so scene-specific copies cannot silently drift out of sync.
$canonicalLauncher = [System.IO.Path]::GetFullPath(
    (Join-Path $PSScriptRoot "../../../../../vrme_vibevoice_pack/run_server.ps1")
)
if (Test-Path -LiteralPath $canonicalLauncher) {
    & $canonicalLauncher
    exit $LASTEXITCODE
}

Set-Location $PSScriptRoot

$port = 8080
$existingListener = Get-NetTCPConnection -LocalPort $port -State Listen -ErrorAction SilentlyContinue | Select-Object -First 1
if ($existingListener) {
    $pidOnPort = $existingListener.OwningProcess
    $processOnPort = Get-Process -Id $pidOnPort -ErrorAction SilentlyContinue
    $processName = if ($processOnPort) { $processOnPort.ProcessName } else { "unknown" }
    Write-Host "VRME server port $port is already in use by PID $pidOnPort ($processName). Restarting it now..."
    Stop-Process -Id $pidOnPort -Force
    Start-Sleep -Milliseconds 500
}

$defaultCacheRoot = "D:\leetcode\model-cache"
if (-not $env:HF_HOME) {
    $env:HF_HOME = Join-Path $defaultCacheRoot "huggingface"
}
if (-not $env:TRANSFORMERS_CACHE) {
    $env:TRANSFORMERS_CACHE = Join-Path $env:HF_HOME "transformers"
}
if (-not $env:TORCH_HOME) {
    $env:TORCH_HOME = Join-Path $defaultCacheRoot "torch"
}
New-Item -ItemType Directory -Force -Path $env:HF_HOME, $env:TRANSFORMERS_CACHE, $env:TORCH_HOME | Out-Null

if (Test-Path ".env") {
    Get-Content ".env" | ForEach-Object {
        if ($_ -match "^\s*#" -or $_ -notmatch "=") { return }
        $name, $value = $_.Split("=", 2)
        if ($name) { [Environment]::SetEnvironmentVariable($name.Trim(), $value.Trim(), "Process") }
    }
}

New-Item -ItemType Directory -Force -Path $env:HF_HOME, $env:TRANSFORMERS_CACHE, $env:TORCH_HOME | Out-Null

$python = Join-Path (Split-Path $PSScriptRoot -Parent) ".venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    $python = "python"
}

& $python -m pip install -r requirements.txt
& $python server_unity_vibevoice.py
