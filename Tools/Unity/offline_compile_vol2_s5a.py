"""Offline compile of the four NEW vol.2 S5-a assemblies (D308-149) with Unity's Roslyn, before Unity has ever generated their .csproj.

offline_compile297.py reads Oheangbu/<name>.csproj, which a brand-new asmdef does not have while the editor is off. This script builds
the reference lists from csprojs that exist and compiles the assemblies in dependency order:
  Oheangbu.Vol2.Rules   noEngineReferences  -> only the netstandard 2.1 reference set (a successful compile IS the proof)
  Oheangbu.Vol2.Import  noEngineReferences  -> netstandard + Rules
  Oheangbu.Vol2.Data    runtime             -> Oheangbu.Data.csproj references (UnityEngine modules) + Rules, Import
  Oheangbu.Vol2.Editor  Editor              -> Oheangbu.EditorTools.csproj references (UnityEditor, UNITY_EDITOR) + Rules, Import, Data
Usage:  python Tools/Unity/offline_compile_vol2_s5a.py [--source stage|assets]      (default: stage = Tools/Unity/Vol2S5aStage)
Sources: <root>/Vol2/Rules/*.cs, <root>/Vol2/Import/*.cs, <root>/Vol2/Data/*.cs, <root>/Editor/Vol2/*.cs  (root = stage or Assets/_Project/Scripts).
Shares the compile lock and RAM guard of offline_compile297. Exit 0 = every assembly compiled with 0 errors."""
from pathlib import Path
import argparse, subprocess, sys, tempfile
import xml.etree.ElementTree as ET
sys.path.insert(0, str(Path(__file__).resolve().parent))
import offline_compile297 as oc

ASSEMBLIES = [  # name, source folder (relative to root), reference csproj, engine-free, project references
    ('Oheangbu.Vol2.Rules', 'Vol2/Rules', 'Oheangbu.Data', True, []),
    ('Oheangbu.Vol2.Import', 'Vol2/Import', 'Oheangbu.Data', True, ['Oheangbu.Vol2.Rules']),
    ('Oheangbu.Vol2.Data', 'Vol2/Data', 'Oheangbu.Data', False, ['Oheangbu.Vol2.Rules', 'Oheangbu.Vol2.Import']),
    ('Oheangbu.Vol2.Editor', 'Editor/Vol2', 'Oheangbu.EditorTools', False, ['Oheangbu.Vol2.Rules', 'Oheangbu.Vol2.Import', 'Oheangbu.Vol2.Data']),
]


def references(csproj, engine_free):
    info = oc.parse(csproj)
    refs = [r for r in info['refs'] if r and 'scriptassemblies' not in r.replace(chr(92), '/').lower()]
    if engine_free:  # what Unity gives a noEngineReferences assembly: the netstandard reference set only
        refs = [r for r in refs if '/netstandard/' in r.replace(chr(92), '/').lower()]
        refs = [r for r in refs if not any(k in Path(r).name.lower() for k in ('unity',))]
    return refs, info


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(); ap.add_argument('--source', choices=['stage', 'assets'], default='stage'); a = ap.parse_args()
    root = oc.ROOT / 'Tools/Unity/Vol2S5aStage' if a.source == 'stage' else oc.PROJECT / 'Assets/_Project/Scripts'
    out = Path(tempfile.mkdtemp(prefix='vol2s5a_')); fresh = {}; failed = False
    for name, folder, ref_proj, engine_free, deps in ASSEMBLIES:
        sources = sorted((root / folder).glob('*.cs'))
        if not sources: print('%s: no sources in %s' % (name, root / folder)); failed = True; break
        refs, info = references(ref_proj, engine_free)
        refs += [str(fresh[d]) for d in deps]
        target = out / (name + '.dll')
        rsp = ['-nologo', '-noconfig', '-nostdlib+', '-target:library', '-out:' + str(target), '-langversion:9.0',
               '-define:' + info['defines'], '-nowarn:0169;0649;1701;1702', '-deterministic', '-warnaserror+:CS0108,CS0114']
        rsp += ['-r:"%s"' % r for r in refs] + ['"%s"' % s for s in sources]
        rf = out / (name + '.rsp'); rf.write_text('\n'.join(rsp), encoding='utf-8')
        run = subprocess.run(['dotnet', str(oc.CSC), '@' + str(rf)], capture_output=True, text=True, encoding='utf-8', errors='replace')
        errors = [l for l in run.stdout.splitlines() if ': error ' in l]
        warns = [l for l in run.stdout.splitlines() if ': warning ' in l]
        print('%s: %s (%d sources, %d refs, %d errors, %d warnings%s)' % (name, 'OK' if run.returncode == 0 else 'FAILED', len(sources), len(refs), len(errors), len(warns), ', engine-free' if engine_free else ''))
        for l in (errors + warns)[:40]: print('  ' + l)
        if run.returncode != 0: failed = True; break
        fresh[name] = target
    sys.exit(1 if failed else 0)


if __name__ == '__main__':
    oc.main = main
    oc._guarded_main()
