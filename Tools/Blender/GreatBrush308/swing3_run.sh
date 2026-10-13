#!/bin/bash
# #308 swing3: 휘두르기 셋 · 대기 둘.  swing3_run.sh solve <이름 ...> [-- solve_m2 추가 인자] | check [--out f] | render <이름> [render_m2 인자] | sheet [이름 ...] | export | truth | carrycheck
ROOT=/c/Users/yj666/Oheangbu; B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"; T=Tools/Blender/GreatBrush308; M=Art/Characters308/GreatBrush308/motion2
ALL="SwingSweep SwingArc SwingSlam"
cd $ROOT; cmd=$1; shift
case $cmd in
  solve) names=(); extra=(); seen=0; for a in "$@"; do if [ "$a" = "--" ]; then seen=1; elif [ $seen = 1 ]; then extra+=("$a"); else names+=("$a"); fi; done
         for n in "${names[@]}"; do "$B" -b --factory-startup -P $T/solve_m2.py -- $n "${extra[@]}" 2>&1 | python $T/sum_m2.py | cut -c1-${CUT:-2400}; done ;;
  check) "$B" -b --factory-startup -P $T/swing3_check.py -- "$@" 2>&1 | grep -E "^S3 ${GREP:-(sum|seam|DONE)}|Error|Traceback|^  File" | cut -c1-${CUT:-2600} ;;
  carrycheck) "$B" -b --factory-startup -P $T/carry2_check.py -- "$@" 2>&1 | grep -E "^C2 (sum|seam|DONE)|Error|Traceback|^  File" | cut -c1-${CUT:-1500} ;;
  render) n=$1; shift; python Tools/resource_guard.py --gpu-run GreatBrush308 -- "$B" -b --factory-startup -P $T/render_m2.py -- $n "$@" 2>&1 | grep -E "M1 DONE|Error|Traceback|^  File|guard|GPU|lock" | cut -c1-400 ;;
  sheet) python $T/sheet_m2.py ${@:-$ALL} ;;
  export) "$B" -b --factory-startup -P $T/export_m2.py -- ${@:-$ALL} 2>&1 | grep -E "^M1|Error|Traceback|^  File" | cut -c1-900 && python $T/swing3_merge_report.py ;;
  truth) "$B" -b --factory-startup -P $T/truth_m2.py 2>&1 | grep -E "^TR|Error|Traceback|^  File" | cut -c1-400 ;;
esac
