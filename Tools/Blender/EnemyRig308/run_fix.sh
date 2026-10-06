#!/usr/bin/env bash
# #308 enemy rig: repair only (the measure JSON must exist). bash Tools/Blender/EnemyRig308/run_fix.sh [ids...]
cd "$(dirname "$0")/../../.." || exit 1
B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
W=Art/Characters308/DokkaebiVerify/fix/work
for id in ${*:-dokkaebi agwi changgui}; do
  python Tools/resource_guard.py --wait | tail -1 || exit 3
  "$B" -b --factory-startup -t 4 --python Tools/Blender/EnemyRig308/enemyrig308.py -- fix $id > "$W/fix_$id.log" 2>&1
  echo "fix $id exit $?"; grep -E "DONE|Error|Traceback" "$W/fix_$id.log" | cut -c1-400
done
