"""Compile checkpoint sources against an existing Unity installation, without a player build."""
import argparse
import json
from pathlib import Path
import subprocess
import xml.etree.ElementTree as ET


def main(source, target):
    source, target = Path(source).resolve(), Path(target).resolve()
    ns = 'http://schemas.microsoft.com/developer/msbuild/2003'
    ET.register_namespace('', ns)
    tag = lambda x: '{' + ns + '}' + x
    temp = target / 'Tools/Checkpoint/_local/compile'
    temp.mkdir(parents=True, exist_ok=True)
    msbuild = Path('C:/Program Files/Microsoft Visual Studio/2022/Community/MSBuild/Current/Bin/MSBuild.exe')
    folders = dict(zip(['Core', 'Data', 'Drawing', 'BrushRender', 'Spellcraft', 'Combat', 'Presentation', 'App', 'EditorTools'],
                       ['Core', 'Data', 'Drawing', 'BrushRender', 'Spellcraft', 'Combat', 'Presentation', 'App', 'Editor']))
    results = []
    built = {}
    for short, folder in folders.items():
        assembly = 'Oheangbu.' + short
        tree = ET.parse(source / 'Oheangbu' / (assembly + '.csproj'))
        root = tree.getroot()
        for group in root.findall(tag('ItemGroup')):
            for child in list(group):
                if child.tag in {tag('Compile'), tag('None')}:
                    group.remove(child)
                elif child.tag == tag('ProjectReference'):
                    name = Path(child.attrib['Include']).stem
                    group.remove(child)
                    ref = ET.SubElement(group, tag('Reference'), {'Include': name})
                    ET.SubElement(ref, tag('HintPath')).text = str(built.get(name, source / 'Oheangbu/Library/ScriptAssemblies' / (name + '.dll')))
        for hint in root.iter(tag('HintPath')):
            if hint.text and not Path(hint.text).is_absolute():
                hint.text = str(source / 'Oheangbu' / hint.text)
        group = ET.SubElement(root, tag('ItemGroup'))
        scripts = sorted((target / 'Oheangbu/Assets/_Project/Scripts' / folder).rglob('*.cs'))
        for path in scripts:
            ET.SubElement(group, tag('Compile'), {'Include': str(path)})
        # Include declared package references added since the editor last generated csproj files.
        asm = json.loads((target / 'Oheangbu/Assets/_Project/Scripts' / folder / (assembly + '.asmdef')).read_text(encoding='utf-8-sig'))
        existing = {r.attrib.get('Include') for r in root.iter(tag('Reference'))}
        for name in asm['references']:
            dll = built.get(name, source / 'Oheangbu/Library/ScriptAssemblies' / (name + '.dll'))
            if name not in existing and dll.exists():
                ref = ET.SubElement(group, tag('Reference'), {'Include': name})
                ET.SubElement(ref, tag('HintPath')).text = str(dll)
        project = temp / (assembly + '.csproj')
        tree.write(project, encoding='utf-8', xml_declaration=True)
        output = temp / assembly
        command = [str(msbuild), str(project), '/t:Build', '/p:Configuration=Debug', '/nologo', '/v:m',
                   '/clp:ErrorsOnly;Summary', '/p:OutputPath=' + str(output) + '/',
                   '/p:IntermediateOutputPath=' + str(temp / ('obj-' + assembly)) + '/']
        run = subprocess.run(command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        log = run.stdout.decode('utf-8', errors='replace')
        (temp / (assembly + '.txt')).write_text(log, encoding='utf-8')
        results.append({'assembly': assembly, 'sources': len(scripts), 'exitCode': run.returncode,
                        'summary': log[-1800:]})
        print(assembly, 'PASS' if run.returncode == 0 else 'FAIL', len(scripts), flush=True)
        if run.returncode:
            print(log[-3500:], flush=True)
            break
        built[assembly] = output / (assembly + '.dll')
    report = target / 'Docs/Checkpoints/Playtest-20260915/COMPILE.json'
    report.write_text(json.dumps({'kind': 'offline source compile; not a Unity import or player build', 'results': results}, ensure_ascii=False, indent=2), encoding='utf-8')
    return 0 if len(built) == len(folders) else 1


if __name__ == '__main__':
    parser = argparse.ArgumentParser()
    parser.add_argument('source')
    parser.add_argument('target')
    args = parser.parse_args()
    raise SystemExit(main(args.source, args.target))
