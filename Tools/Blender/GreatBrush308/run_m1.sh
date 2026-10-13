#!/bin/bash
# #308 GreatBrush motion1: Blender 한 번(헤드리스, GPU 잠금 경유).  run_m1.sh <script_m1.py> [인자 ...]
ROOT=/c/Users/yj666/Oheangbu; B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
S=$1; shift
cd $ROOT && python Tools/resource_guard.py --gpu-run GreatBrush308 -- "$B" -b --factory-startup -P Tools/Blender/GreatBrush308/$S -- "$@" 2>&1 | grep -E "^M1|Error|Traceback|^  File|guard|GPU" | cut -c1-${CUT:-2000}
