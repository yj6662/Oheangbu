#!/bin/bash
# #308 GreatBrush v4: Blender 한 번(헤드리스, GPU 잠금 경유).  run_v4.sh <script_v4.py> [인자 ...]   — 첫 인자로 출력 폴더(v4)가 자동으로 들어간다
ROOT=/c/Users/yj666/Oheangbu; B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
OUT=C:/Users/yj666/Oheangbu/Art/Characters308/GreatBrush308/v4
S=$1; shift
cd $ROOT && python Tools/resource_guard.py --gpu-run GreatBrush308 -- "$B" -b --factory-startup -P Tools/Blender/GreatBrush308/$S -- $OUT "$@" 2>&1 | grep -E "^BUILD|^VERIFY|^CLOSE|Error|Traceback|^  File|guard|GPU" | cut -c1-${CUT:-1600}
