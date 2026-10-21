#!/bin/bash
# D308-120 one look round: Refresh (optional R=1) -> bodies:build:<key> -> stills:<key> twice (the second counts) for each key given.
#   R=1 bash Tools/MeshyRuns/EnemyBodies308/grot_round.sh jackal boar ...
cd /c/Users/yj666/Oheangbu
if [ -n "$R" ]; then
  echo "{\"id\":\"grot308-$(date +%s)\",\"method\":\"Refresh\",\"request\":\"\"}" > Art/SpellVFX120/command.json
  for i in $(seq 1 90); do [ -f Art/SpellVFX120/command.json ] || break; sleep 2; done; sleep 25
fi
for k in "$@"; do
  RUN=grot308 bash Tools/Unity/Stage308_ops/q.sh build_$k Creatures308 "bodies:build:$k" 'REFUSED|FAILED|part |bodies built' 900 | grep -v "^\[" | sed 's/ | contact.*; part/ | part/'
  RUN=grot308 bash Tools/Unity/Stage308_ops/q.sh st_$k Creatures308 "stills:$k" 'REFUSED|FAILED' 200 >/dev/null
  RUN=grot308 bash Tools/Unity/Stage308_ops/q.sh st2_$k Creatures308 "stills:$k" 'REFUSED|FAILED|WARNING' 200 | grep -v "^\["
done
