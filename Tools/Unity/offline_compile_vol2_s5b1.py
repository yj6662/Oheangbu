"""Offline compile of the S5-b slice 1 assemblies (D308-149) with staged C# and staged asmdef references.

offline_compile297.py builds each assembly from its (possibly stale) .csproj: it knows neither the new asmdef references of this slice
(Spellcraft / App / Vol2.Editor now reference Oheangbu.Vol2.* ...) nor staged files in folders the csproj never listed (App/Vol2/...).
This driver patches oc.parse so that, per assembly:
  - every `references` entry of the (staged, else live) asmdef that the csproj lacks is added as a project reference
    (oc resolves it to the fresh DLL of this run, else a private copy of Library/ScriptAssemblies/<name>.dll),
  - every staged .cs whose NEAREST asmdef is this assembly is added to its sources.
Usage: python Tools/Unity/offline_compile_vol2_s5b1.py [--stage Tools/Unity/Vol2S5b1Stage] [assemblies...]
Default order: Vol2.Rules, Vol2.Import, Vol2.Data, Spellcraft, Combat, App, EditorTools, EditorTools.WorldMacro, Vol2.Editor.
Shares the compile lock and RAM guard of offline_compile297. Exit 0 = every assembly compiled with 0 errors."""
from pathlib import Path
import json, sys
sys.path.insert(0, str(Path(__file__).resolve().parent))
import offline_compile297 as oc

SCRIPTS = oc.PROJECT / 'Assets/_Project/Scripts'
DEFAULT = ['Oheangbu.Vol2.Rules', 'Oheangbu.Vol2.Import', 'Oheangbu.Vol2.Data', 'Oheangbu.Spellcraft', 'Oheangbu.Combat', 'Oheangbu.App',
           'Oheangbu.EditorTools', 'Oheangbu.EditorTools.WorldMacro', 'Oheangbu.Vol2.Editor']
STAGE = None


def asmdef_of_dir(rel_dir, stage):
    """name of the nearest asmdef at or above rel_dir (a path relative to Scripts), looking in the stage first, then in Assets."""
    d = rel_dir
    while True:
        for root in (stage, SCRIPTS):
            if root is None: continue
            for f in (root / d).glob('*.asmdef') if (root / d).exists() else []:
                return json.loads(f.read_text(encoding='utf-8-sig'))['name'], f
        if d == Path('.') or str(d) == '': return None, None
        d = d.parent


def asmdef_file(name, stage):
    for root in (stage, SCRIPTS):
        if root is None: continue
        for f in root.rglob('*.asmdef'):
            try:
                if json.loads(f.read_text(encoding='utf-8-sig'))['name'] == name: return f
            except Exception: pass
    return None


def main():
    global STAGE
    args = [a for a in sys.argv[1:]]
    stage_rel = 'Tools/Unity/Vol2S5b1Stage'
    if '--stage' in args:
        i = args.index('--stage'); stage_rel = args[i + 1]; del args[i:i + 2]
    names = args or DEFAULT
    STAGE = oc.ROOT / stage_rel
    staged_by_asm = {}
    for f in STAGE.rglob('*.cs'):
        rel = f.relative_to(STAGE)
        asm, _ = asmdef_of_dir(rel.parent, STAGE)
        staged_by_asm.setdefault(asm, []).append(rel.as_posix())
    original = oc.parse

    def parse(name):
        info = original(name)
        f = asmdef_file(name, STAGE)
        if f is not None:
            refs = json.loads(f.read_text(encoding='utf-8-sig')).get('references', [])
            have = set(info['projects'])
            for r in refs:
                if r not in have and not r.startswith('GUID:'): info['projects'].append(r); have.add(r)
        listed = {s.replace('\\', '/').lower() for s in info['sources']}
        for rel in staged_by_asm.get(name, []):
            key = ('Assets/_Project/Scripts/' + rel)
            if key.lower() not in listed: info['sources'].append(key.replace('/', '\\'))
        return info

    oc.parse = parse
    sys.argv = [sys.argv[0]] + names + ['--stage', stage_rel]
    oc._guarded_main()


if __name__ == '__main__':
    main()
