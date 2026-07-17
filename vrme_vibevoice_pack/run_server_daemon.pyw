"""Windowless, persistent launcher for the Unity VRME backend."""

from __future__ import annotations

import os
from pathlib import Path
import runpy
import sys
import traceback


HERE = Path(__file__).resolve().parent
STDOUT_LOG = HERE / "vrme_server_daemon.out.log"
STDERR_LOG = HERE / "vrme_server_daemon.err.log"

os.chdir(HERE)
stdout_log = STDOUT_LOG.open("a", encoding="utf-8", buffering=1)
stderr_log = STDERR_LOG.open("a", encoding="utf-8", buffering=1)
sys.stdout = stdout_log
sys.stderr = stderr_log

try:
    runpy.run_path(str(HERE / "server_unity_vibevoice.py"), run_name="__main__")
except BaseException:
    traceback.print_exc(file=stderr_log)
    raise
finally:
    stdout_log.flush()
    stderr_log.flush()
