"""Start the bundled extractor in a private process group for reliable cancellation."""
import os
import io
import runpy
import sys

os.setsid()
# Official CPython Android redirects Python streams to logcat. The downloader
# protocol needs JSON/progress on its parent's private pipes, never device logs.
# Keep the OS descriptors open until process exit, including during shutdown.
sys.stdout = sys.__stdout__ = io.TextIOWrapper(
    io.FileIO(1, "w", closefd=False), encoding="utf-8",
    errors="backslashreplace", write_through=True)
sys.stderr = sys.__stderr__ = io.TextIOWrapper(
    io.FileIO(2, "w", closefd=False), encoding="utf-8",
    errors="backslashreplace", write_through=True)
entrypoint = sys.argv.pop(1)
sys.argv[0] = entrypoint
runpy.run_path(entrypoint, run_name="__main__")
