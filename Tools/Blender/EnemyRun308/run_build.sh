#!/usr/bin/env bash
# #308 enemy run: build the run clips, one Blender at a time (CPU only, no render), then the sheets.
#   bash Tools/Blender/EnemyRun308/run_build.sh [--no-export] [--tag <name>] [ids...]        default ids: agwi changgui
# The resource guard runs before every Blender start. Logs: Art/Characters308/DokkaebiVerify/run/work/build_<id>.log
cd "$(dirname "$0")/../../.." || exit 1
B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
W=Art/Characters308/DokkaebiVerify/run/work
mkdir -p "$W"
flag=""; tag=""; ids=""
while [ $# -gt 0 ]; do
  case "$1" in
    --no-export) flag="--no-export";;
    --tag) tag="$2"; shift;;
    *) ids="$ids $1";;
  esac
  shift
done
ids="${ids:- agwi changgui}"
for id in $ids; do
  PYTHONIOENCODING=utf-8 python Tools/resource_guard.py --wait | tail -1 || { echo "guard gave up before $id"; exit 3; }
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRun308/enemyrun308.py -- build $id $flag > "$W/build_$id.log" 2>&1
  echo "build $id exit $?"
  grep -E "DONE|Error|Traceback|refused" "$W/build_$id.log" | cut -c1-600
  # Blender exits 0 on a Python error: a build without its DONE line is a failed build, and the sheets would show the old poses
  grep -q "^RUN DONE" "$W/build_$id.log" || { echo "build $id FAILED (no RUN DONE line in $W/build_$id.log) - sheets not drawn"; exit 4; }
done
PYTHONIOENCODING=utf-8 python Tools/resource_guard.py --wait | tail -1
if [ -n "$tag" ]; then PYTHONIOENCODING=utf-8 python Tools/Blender/EnemyRun308/sheets_run308.py $ids --tag "$tag"; else PYTHONIOENCODING=utf-8 python Tools/Blender/EnemyRun308/sheets_run308.py $ids; fi
