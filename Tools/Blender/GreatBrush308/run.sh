#!/bin/bash
# #308 GreatBrush: 자세 스틸 한 번(Blender 는 한 번에 하나, GPU 잠금 경유).  run.sh [자세 ...] [--res N] [--only-metrics] [--blend]
ROOT=/c/Users/yj666/Oheangbu; B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"
OUT=C:/Users/yj666/Oheangbu/Art/Characters308/GreatBrush308${SUB:+/$SUB}
FBX=C:/Users/yj666/Oheangbu/Oheangbu/Assets/_Project/Art/Characters/PlaytestReRig/Models/Player_C02_ReRig.fbx
cd $ROOT && python Tools/resource_guard.py --gpu-run GreatBrush308 -- "$B" -b --factory-startup -P Tools/Blender/GreatBrush308/${SCRIPT:-pose.py} -- $OUT $FBX "$@" 2>&1 | grep -E "^POSE|^CLOSE|Error|Traceback|^  File|guard|GPU" | cut -c1-${CUT:-1400}
