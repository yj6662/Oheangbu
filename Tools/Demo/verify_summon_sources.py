"""Mirror source derivatives and compile without making a player build or changing checkpoint evidence."""
from pathlib import Path
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
worktree = Path('C:/Users/yj666/.codex/worktrees/oheangbu-playtest-checkpoint')
scripts = root / 'Oheangbu/Assets/_Project/Scripts'
for source in scripts.rglob('*'):
    if source.is_file() and source.suffix in ('.cs', '.meta', '.asmdef'):
        destination = worktree / source.relative_to(root)
        destination.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, destination)
historical = worktree / 'Docs/Checkpoints/Playtest-20260915/COMPILE.json'
previous = historical.read_bytes()
output = root / 'Art/Demo/Summons'
output.mkdir(parents=True, exist_ok=True)
try:
    result = subprocess.run([sys.executable, str(worktree / 'Tools/Checkpoint/compile_snapshot.py'), str(root), str(worktree)], capture_output=True, text=True, encoding='utf-8', errors='replace')
    (output / 'compile.log').write_text(result.stdout + '\n' + result.stderr, encoding='utf-8')
    shutil.copy2(historical, output / 'compile.json')
    print(result.stdout[-6000:])
    raise SystemExit(result.returncode)
finally:
    historical.write_bytes(previous)
