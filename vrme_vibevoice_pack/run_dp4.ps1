$ErrorActionPreference = "Stop"
Set-Location "D:\leetcode\avartar-server"

$python = ".\.venv\Scripts\python.exe"
if (-not (Test-Path $python)) {
    $python = "python"
}

& $python dp4.py
