#!/bin/bash
# #308 carry2: 어깨 걸치기 세 클립.  carry2_run.sh solve [solve_m2 추가 인자] | check [--out f] | render [이름 ...] | sheet | export
ROOT=/c/Users/yj666/Oheangbu; B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"; T=Tools/Blender/GreatBrush308; M=Art/Characters308/GreatBrush308/motion2
cd $ROOT; cmd=$1; shift
case $cmd in
  solve) for n in CarryIdle ToCarry ToStance; do "$B" -b --factory-startup -P $T/solve_m2.py -- $n "$@" 2>&1 | python $T/sum_m2.py | cut -c1-1500; done ;;
  check) "$B" -b --factory-startup -P $T/carry2_check.py -- "$@" 2>&1 | grep -E "^C2|Error|Traceback|^  File" | cut -c1-${CUT:-2600} ;;
  render) for n in ${@:-CarryIdle ToCarry ToStance}; do python Tools/resource_guard.py --gpu-run GreatBrush308 -- "$B" -b --factory-startup -P $T/render_m2.py -- $n 2>&1 | grep -E "M1 DONE|Error|Traceback|^  File|guard|GPU|lock" | cut -c1-400; done ;;
  sheet) python $T/sheet_m2.py CarryIdle ToCarry ToStance ;;
  export) "$B" -b --factory-startup -P $T/export_m2.py -- CarryIdle ToCarry ToStance 2>&1 | grep -E "^M1|Error|Traceback|^  File" | cut -c1-900 && python $T/carry2_merge_report.py ;;
esac
