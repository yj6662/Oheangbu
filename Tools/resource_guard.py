"""Resource guard: pause only while the PC is in a danger state (memory, CPU or GPU).

usage:
  python Tools/resource_guard.py            # one reading, exit 0 = ok, 3 = danger
  python Tools/resource_guard.py --wait     # block until the danger clears (re-check every 15 s, give up after 30 min)
  python Tools/resource_guard.py --gpu-run <label> -- <command...>
        # run ONE GPU-heavy job at a time (Unity Play run, Blender render/bake): waits for the danger to clear and for the
        # GPU lock (Tools/.gpu_lock, stale after 45 min), runs the command, always releases. Exit code = the command's.
        # Added after the 2026-10-03 BSOD 0x13A (kernel heap corruption) while Unity Play and other GPU work overlapped.
Danger (any of):
  free physical RAM < 3 GB, or committed memory > 88 % of the commit limit
      (2026-10-04: 4 GB / 92 % before. With the editor open this 32 GB PC settles at 3.5-4.2 GB available whatever the
       editor's private size is - Windows pages the editor out - so a 4 GB line only stalled the queue; the commit line is
       the one that tracks the editor's creep, and it is tighter now.)
  CPU > 95 % averaged over 3 s
  GPU memory > 97 % of total, or GPU temperature >= 87 C   (nvidia-smi; skipped when absent)
Run it right before a heavy step: Unity Play, a Blender render or bake, a big numpy job, a video encode.
"""
import ctypes, subprocess, sys, time


class _MemStatus(ctypes.Structure):
    _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong),
                ('ullTotalPhys', ctypes.c_ulonglong), ('ullAvailPhys', ctypes.c_ulonglong),
                ('ullTotalPageFile', ctypes.c_ulonglong), ('ullAvailPageFile', ctypes.c_ulonglong),
                ('ullTotalVirtual', ctypes.c_ulonglong), ('ullAvailVirtual', ctypes.c_ulonglong),
                ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]


def memory():
    m = _MemStatus(); m.dwLength = ctypes.sizeof(_MemStatus)
    ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m))
    commit = 1 - m.ullAvailPageFile / max(1, m.ullTotalPageFile)
    return m.ullAvailPhys / 2**30, commit


def cpu(seconds=3.0):
    def times():
        idle, kernel, user = (ctypes.c_ulonglong() for _ in range(3))
        ctypes.windll.kernel32.GetSystemTimes(ctypes.byref(idle), ctypes.byref(kernel), ctypes.byref(user))
        return idle.value, kernel.value + user.value
    i0, t0 = times(); time.sleep(seconds); i1, t1 = times()
    return 1 - (i1 - i0) / max(1, t1 - t0)


def gpu():
    try:
        out = subprocess.run(['nvidia-smi', '--query-gpu=memory.used,memory.total,temperature.gpu,utilization.gpu',
                              '--format=csv,noheader,nounits'], capture_output=True, text=True, timeout=10).stdout
        used, total, temp, util = (float(x) for x in out.strip().splitlines()[0].split(','))
        return used / max(1, total), temp, util
    except Exception:
        return None


def reading():
    free_gb, commit = memory(); c = cpu(); g = gpu()
    danger = []
    if free_gb < 3.0: danger.append('free RAM %.1f GB' % free_gb)
    if commit > .88: danger.append('commit %.0f%%' % (commit * 100))
    if c > .95: danger.append('CPU %.0f%%' % (c * 100))
    if g and g[0] > .97: danger.append('GPU memory %.0f%%' % (g[0] * 100))
    if g and g[1] >= 87: danger.append('GPU %d C' % g[1])
    text = 'RAM free %.1f GB, commit %.0f%%, CPU %.0f%%' % (free_gb, commit * 100, c * 100)
    if g: text += ', GPU mem %.0f%% %d C util %d%%' % (g[0] * 100, g[1], g[2])
    return danger, text


LOCK = __import__('pathlib').Path(__file__).resolve().parent / '.gpu_lock'


def gpu_run(argv):
    import os
    i = argv.index('--gpu-run'); label = argv[i + 1]; cmd = argv[argv.index('--') + 1:]
    deadline = time.time() + 5400
    while True:
        danger, text = reading()
        if not danger:
            try:
                fd = os.open(str(LOCK), os.O_CREAT | os.O_EXCL | os.O_WRONLY)
                os.write(fd, ('%s pid %d %s' % (label, os.getpid(), time.strftime('%H:%M:%S'))).encode()); os.close(fd)
                break
            except FileExistsError:
                try:
                    if time.time() - LOCK.stat().st_mtime > 2700: LOCK.unlink(); continue
                    holder = LOCK.read_text(errors='ignore')
                except OSError:
                    holder = '?'
                print('GPU lock held by ' + holder + ' - waiting', flush=True)
        else:
            print('DANGER ' + '; '.join(danger) + ' | ' + text, flush=True)
        if time.time() > deadline:
            print('gave up waiting for the GPU lock', flush=True); return 4
        time.sleep(15)
    print('GPU lock acquired by ' + label + ' | ' + text, flush=True)
    try:
        return subprocess.call(cmd)
    finally:
        try: LOCK.unlink()
        except OSError: pass


def main():
    if '--gpu-run' in sys.argv: return gpu_run(sys.argv)
    wait = '--wait' in sys.argv
    deadline = time.time() + 1800
    while True:
        danger, text = reading()
        if not danger:
            print('OK ' + text); return 0
        print('DANGER ' + '; '.join(danger) + ' | ' + text, flush=True)
        if not wait or time.time() > deadline:
            return 3
        time.sleep(15)


if __name__ == '__main__':
    sys.exit(main())
