"""Verify scene dependency recovery coverage against the pre-change backup and Git."""
import hashlib
import json
import subprocess
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild'
RECOVERY = OUT / 'Recovery/20260923-050044'
manifest = json.loads((RECOVERY / 'manifest.json').read_text(encoding='utf-8'))
backed = {row['path'] for row in manifest['files']}
tracked = set(subprocess.check_output(['git', 'ls-files'], cwd=ROOT).decode('utf-8').splitlines())
dependencies = json.loads((OUT / 'dependencies.json').read_text(encoding='utf-8-sig'))['assets']
rows, extra = [], []
for asset in dependencies:
    for relative in (f'Oheangbu/{asset}', f'Oheangbu/{asset}.meta'):
        path = ROOT / relative
        if not path.is_file():
            continue
        status = 'prechange_zip' if relative in backed else 'git' if relative in tracked else 'dependency_supplement'
        rows.append(dict(path=relative, recovery=status))
        if status == 'dependency_supplement':
            extra.append(relative)
supplement = RECOVERY / 'dependency_supplement.zip'
if extra and not supplement.exists():
    with zipfile.ZipFile(supplement, 'w', zipfile.ZIP_DEFLATED, compresslevel=1) as archive:
        for relative in extra:
            archive.write(ROOT / relative, relative)
if supplement.exists():
    with zipfile.ZipFile(supplement) as archive:
        assert archive.testzip() is None
        assert set(extra).issubset(set(archive.namelist()))
result = dict(dependency_assets=len(dependencies), files=len(rows), supplement_files=len(extra),
              note='Supplement taken after code/campaign changes; prechange ZIP is authoritative for files it contains.', entries=rows)
(RECOVERY / 'dependency_coverage.json').write_text(json.dumps(result, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps({k: v for k, v in result.items() if k != 'entries'}))
