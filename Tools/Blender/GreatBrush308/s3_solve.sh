#!/bin/bash
# #308 swing3: solve 한 클립 + 짧은 요약.  s3_solve.sh <이름>
cd /c/Users/yj666/Oheangbu; bash Tools/Blender/GreatBrush308/swing3_run.sh solve $1 2>&1 | python Tools/Blender/GreatBrush308/s3_sum.py
