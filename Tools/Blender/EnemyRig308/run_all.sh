#!/usr/bin/env bash
# #308 enemy rig: measure + repair the three Meshy humanoids, one Blender at a time (CPU only, no render).
#   bash Tools/Blender/EnemyRig308/run_all.sh [ids...]        default: dokkaebi agwi changgui
# The resource guard runs before every Blender start. Logs: Art/Characters308/DokkaebiVerify/fix/work/<step>_<id>.log
cd "$(dirname "$0")/../../.." || exit 1
B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
W=Art/Characters308/DokkaebiVerify/fix/work
mkdir -p "$W"
ids="${*:-dokkaebi agwi changgui}"
for id in $ids; do
  for step in measure fix; do
    python Tools/resource_guard.py --wait | tail -1 || { echo "guard gave up before $step $id"; exit 3; }
    "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRig308/enemyrig308.py -- $step $id > "$W/${step}_$id.log" 2>&1
    echo "$step $id exit $?"
    grep -E "DONE|Error|Traceback" "$W/${step}_$id.log" | cut -c1-500
  done
done
