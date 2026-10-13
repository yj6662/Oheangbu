#!/bin/bash
# #308 GreatBrush motion1: windows_m1.json 의 창대로 클립별 스틸.  renderall_m1.sh [clip ...]
cd /c/Users/yj666/Oheangbu
python - "$@" <<'PY' | while read c fr; do bash Tools/Blender/GreatBrush308/run_m1.sh render_m1.py $c --frames $fr | tail -1; echo "rendered $c"; done
import json, sys
W = json.load(open('Tools/Blender/GreatBrush308/windows_m1.json', encoding='utf-8'))
for c, w in W.items():
    if len(sys.argv) > 1 and c not in sys.argv[1:]: continue
    print(c, ','.join(str(f) for f in w['frames']))
PY
