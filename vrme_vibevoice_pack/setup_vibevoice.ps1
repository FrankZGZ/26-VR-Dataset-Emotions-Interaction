$ErrorActionPreference = "Stop"

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

$repo = $env:VIBEVOICE_REPO
if (-not $repo) {
    $repo = "D:\leetcode\VibeVoice"
}

if (-not (Test-Path $repo)) {
    git clone https://github.com/microsoft/VibeVoice.git $repo
}

Set-Location $repo

if (-not (Test-Path ".venv")) {
    python -m venv .venv
}

.\.venv\Scripts\python.exe -m pip install --upgrade pip

.\.venv\Scripts\python.exe -m pip install -e ".[streamingtts]"

Write-Host ""
Write-Host "VibeVoice installed at: $repo"
Write-Host "Set VIBEVOICE_REPO=$repo in vrme_vibevoice_pack\.env"
Write-Host "Model cache: $env:HF_HOME"
Write-Host "If the first run downloads models, let it finish."
