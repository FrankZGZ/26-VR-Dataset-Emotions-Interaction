$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

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
