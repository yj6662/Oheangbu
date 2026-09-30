"""Sequential bounded compact authoring steps; never runs a build or enters Play."""
import json
import os
from pathlib import Path
import subprocess
import sys

ROOT = Path(__file__).resolve().parents[2]
ALLOWED = {'grade-step', 'dressing-step', 'nav-step', 'road-step', 'relief-context-step'}
if len(sys.argv) != 3 or sys.argv[1] not in ALLOWED:
    raise SystemExit('run_steps.py <grade-step|dressing-step|nav-step|road-step|relief-context-step> <1..8>')
count = int(sys.argv[2])
if not 1 <= count <= 8:
    raise SystemExit('At most eight sequential steps per batch')
for _ in range(count):
    result = subprocess.run([sys.executable, str(ROOT/'Tools/Demo/run_command.py'),
                             'chapter3:compact:'+sys.argv[1]], cwd=ROOT,
                            env={**os.environ, 'PYTHONUTF8':'1'}, text=True,
                            encoding='utf-8', capture_output=True)
    if result.returncode:
        print(result.stdout, result.stderr, flush=True)
        raise SystemExit(result.returncode)
    data = json.loads(result.stdout)
    keys=('status','phase','stage','next','nextTerrain','terrainCount','nextRoute','routeCount','totalSamples','totalFailedSamples','totalIssues','processedCells','totalCells','scannedMeshes','sourceMeshes','ready','nextSurface','completedSurfaces','error','commitRatio')
    print(json.dumps({k:data[k] for k in keys if k in data}, ensure_ascii=False), flush=True)
    if data.get('ready') or data.get('status') in ('FINDINGS','PASS','COMPLETE') or data.get('phase') in ('GRADED_GEOMETRY_READY_FOR_PHYSICAL_AUDIT','GRADED_TERRAIN_READY_FOR_PHYSICAL_AUDIT','COMPLETE'):
        break
