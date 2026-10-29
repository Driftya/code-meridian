"""Make only a synthetic directory unreadable, without changing Windows ACLs."""
from contextlib import contextmanager
import os


@contextmanager
def inaccessible_directory(path):
    if os.name == "nt":
        import ctypes
        from ctypes import wintypes
        kernel = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD,
            wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        kernel.CreateFileW.restype = wintypes.HANDLE
        kernel.CloseHandle.argtypes = [wintypes.HANDLE]
        # FILE_LIST_DIRECTORY, no sharing, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS.
        handle = kernel.CreateFileW(str(path), 1, 0, None, 3, 0x02000000, None)
        if handle == wintypes.HANDLE(-1).value:
            raise ctypes.WinError(ctypes.get_last_error())
        try:
            yield
        finally:
            kernel.CloseHandle(handle)
    else:
        if os.geteuid() == 0:
            raise RuntimeError("Inaccessible-directory acceptance must run as an unprivileged user")
        mode = path.stat().st_mode
        path.chmod(0)
        try:
            yield
        finally:
            path.chmod(mode)
