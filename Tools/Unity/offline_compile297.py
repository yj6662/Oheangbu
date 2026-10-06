"""Compile Unity assemblies outside the Editor with Unity's Roslyn, substituting staged files (#297).

The live Editor is shared with another session; staged C# is checked here before it enters Assets/.
Usage: python Tools/Unity/offline_compile297.py Oheangbu.Data Oheangbu.App [--stage Tools/Unity/Finish297Stage]
  Staged files replace the project file with the same relative path under Assets/_Project/Scripts/
  (e.g. <stage>/Data/World/InkSkyProfile.cs -> Assets/_Project/Scripts/Data/World/InkSkyProfile.cs);
  staged files with no project counterpart are added to the assembly whose folder contains them.
Assemblies are compiled in the given order; later ones reference the fresh output of earlier ones.
"""
from pathlib import Path
import argparse, re, subprocess, sys, tempfile
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
PROJECT = ROOT / 'Oheangbu'
CSC = Path('C:/Program Files/Unity/Hub/Editor/6000.3.9f1/Editor/Data/DotNetSdkRoslyn/csc.dll')
NS = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}


def parse(name):
    tree = ET.parse(PROJECT / (name + '.csproj')); r = tree.getroot()
    text = lambda tag: ';'.join(e.text or '' for e in r.iter('{%s}%s' % (NS['m'], tag)))
    compile_items = [e.get('Include') for e in r.iter('{%s}Compile' % NS['m'])]
    refs = [e.text for e in r.iter('{%s}HintPath' % NS['m'])]
    projects = [Path(e.get('Include')).stem for e in r.iter('{%s}ProjectReference' % NS['m'])]
    return dict(sources=compile_items, refs=refs, projects=projects, defines=text('DefineConstants'),
                lang=(text('LangVersion').split(';')[0] or '9.0'), unsafe='true' in text('AllowUnsafeBlocks').lower(),
                nowarn=text('NoWarn'))


def snap(ref, out):
    """Private copy of a Library/ScriptAssemblies reference (retries while Unity is writing it); other paths pass through."""
    if not ref or 'scriptassemblies' not in ref.replace(chr(92), '/').lower(): return ref
    src = Path(ref); dst = out / 'refs' / src.name
    if dst.exists(): return str(dst)
    dst.parent.mkdir(parents=True, exist_ok=True)
    import shutil, time
    for attempt in range(40):
        try: shutil.copyfile(src, dst); return str(dst)
        except (PermissionError, OSError):
            time.sleep(.5)
    return ref


def main():
    sys.stdout.reconfigure(encoding="utf-8", errors="replace")
    ap = argparse.ArgumentParser(); ap.add_argument('assemblies', nargs='+'); ap.add_argument('--stage', default='Tools/Unity/Finish297Stage')
    a = ap.parse_args(); stage = ROOT / a.stage
    staged = {}
    if stage.exists():
        for f in stage.rglob('*.cs'):
            staged[('Assets/_Project/Scripts/' + f.relative_to(stage).as_posix()).lower()] = f
    out = Path(tempfile.mkdtemp(prefix='offline297_')); fresh = {}; failed = False
    for name in a.assemblies:
        info = parse(name); used = set(); sources = []
        for s in info['sources']:
            key = s.replace('\\', '/').lower()
            if key in staged: sources.append(staged[key]); used.add(key)
            else: sources.append(PROJECT / s)
        folders = {Path(s.replace('\\', '/')).parent.as_posix().lower() for s in info['sources']}
        for key, f in staged.items():
            # a staged file the (possibly stale) csproj does not list joins its folder's assembly, also when it already sits in Assets
            if key not in used and Path(key).parent.as_posix() in folders:
                sources.append(f); used.add(key)
        # #308: never hand csc a path inside Library/ScriptAssemblies - csc maps its references, and while it does Unity cannot
        # overwrite that DLL (the editor then reports a script compilation error at CopyFiles). Reference a private copy instead.
        refs = [snap(r, out) for r in info['refs']]
        for p in info['projects']:
            refs.append(str(fresh[p]) if p in fresh else snap(str(PROJECT / 'Library/ScriptAssemblies' / (p + '.dll')), out))
        target = out / (name + '.dll')
        rsp = ['-nologo', '-noconfig', '-nostdlib+', '-target:library', '-out:' + str(target), '-langversion:' + info['lang'],
               '-define:' + info['defines'], '-nowarn:' + (info['nowarn'] or '0169') + ';1701;1702', '-deterministic']
        if info['unsafe']: rsp.append('-unsafe')
        rsp += ['-r:"%s"' % r for r in refs if r]
        rsp += ['"%s"' % s for s in sources]
        rf = out / (name + '.rsp'); rf.write_text('\n'.join(rsp), encoding='utf-8')
        run = subprocess.run(['dotnet', str(CSC), '@' + str(rf)], capture_output=True, text=True, encoding='utf-8', errors='replace')
        errors = [l for l in run.stdout.splitlines() if ': error ' in l]
        print('%s: %s (%d sources, %d staged, %d errors)' % (name, 'OK' if run.returncode == 0 else 'FAILED', len(sources), len(used), len(errors)))
        for l in errors[:40]: print('  ' + l)
        if run.returncode != 0: failed = True; break
        fresh[name] = target
    sys.exit(1 if failed else 0)


def _guarded_main():
    """One offline compile at a time on this PC, and only while memory is not short (2026-10-04: several agents compile in
    parallel while Unity holds ~17 GB; the user is remote and the PC has blue-screened under load). Lock file Tools/.compile_lock,
    stale after 20 min. Set OFFLINE297_NO_LOCK=1 to skip (never needed in normal use)."""
    import os, time, ctypes
    if os.environ.get('OFFLINE297_NO_LOCK') == '1': return main()
    lock = ROOT / 'Tools' / '.compile_lock'

    class _Mem(ctypes.Structure):
        _fields_ = [('dwLength', ctypes.c_ulong), ('dwMemoryLoad', ctypes.c_ulong), ('ullTotalPhys', ctypes.c_ulonglong),
                    ('ullAvailPhys', ctypes.c_ulonglong), ('ullTotalPageFile', ctypes.c_ulonglong), ('ullAvailPageFile', ctypes.c_ulonglong),
                    ('ullTotalVirtual', ctypes.c_ulonglong), ('ullAvailVirtual', ctypes.c_ulonglong), ('ullAvailExtendedVirtual', ctypes.c_ulonglong)]

    def free_gb():
        try:
            m = _Mem(); m.dwLength = ctypes.sizeof(_Mem); ctypes.windll.kernel32.GlobalMemoryStatusEx(ctypes.byref(m)); return m.ullAvailPhys / 2**30
        except Exception:
            return 99.0
    deadline = time.time() + 3600; said = False; fd = None
    while True:
        if free_gb() >= 4.5:
            try:
                fd = os.open(str(lock), os.O_CREAT | os.O_EXCL | os.O_WRONLY); os.write(fd, ('pid %d %s' % (os.getpid(), time.strftime('%H:%M:%S'))).encode()); os.close(fd); break
            except FileExistsError:
                try:
                    if time.time() - lock.stat().st_mtime > 1200: lock.unlink(); continue
                except OSError:
                    pass
        if not said: print('offline297: waiting (another compile is running or free RAM < 4.5 GB)', flush=True); said = True
        if time.time() > deadline: print('offline297: gave up waiting for the compile lock', flush=True); sys.exit(4)
        time.sleep(5)
    try:
        main()
    finally:
        try: lock.unlink()
        except OSError: pass


if __name__ == '__main__':
    _guarded_main()
