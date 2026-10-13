#!/bin/bash
# #308 s4: s4.sh solve <이름> [solve_m2 인자] | render <이름> [render 인자] | sheet [이름..] | export <이름..> | truth
ROOT=/c/Users/yj666/Oheangbu; B="C:/Program Files/Blender Foundation/Blender 5.0/blender.exe"; T=Tools/Blender/GreatBrush308
cd $ROOT; cmd=$1; shift
case $cmd in
  solve) n=$1; shift; "$B" -b --factory-startup -P $T/solve_m2.py -- $n "$@" 2>&1 | python $T/s4_sum.py ;;
  render) n=$1; shift; python Tools/resource_guard.py --gpu-run GreatBrush308 -- "$B" -b --factory-startup -P $T/render_m2.py -- $n "$@" 2>&1 | grep -E "M1 DONE|Error|Traceback|^  File|guard|lock" | cut -c1-300 ;;
  sheet) for n in "$@"; do python $T/s4_mont.py $n 2>&1 | tail -2; done ;;
  export) "$B" -b --factory-startup -P $T/export_m2.py -- "$@" 2>&1 | grep -E "^M1 (DONE|verify)|Error|Traceback|^  File" | cut -c1-330 && python $T/s4_merge.py "$@" ;;
  truth) "$B" -b --factory-startup -P $T/truth_m2.py 2>&1 | grep -E "^TR|Error|Traceback|^  File" | cut -c1-300 ;;
esac
