$ErrorActionPreference = "Stop"
Set-Location "D:\leetcode\avartar-server"

$python = ".\.venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    $python = "python"
}

& $python tsst_batch_stt_fusion.py
