#!/bin/bash
# Run a Play session only when the user has been away 15 min (session.sh refuses otherwise): retry every 2 min, up to 6 h.
#   bash Tools/Unity/Stage308_ops/play_when_idle.sh <label> <steps name>
cd /c/Users/yj666/Oheangbu
for n in $(seq 1 180); do
  out=$(bash Tools/Unity/Stage308_ops/session.sh "$1" SEQ "$2" 2>&1); rc=$?
  if [ $rc -ne 9 ]; then echo "$out" | tail -6; echo "rc=$rc after $n tries"; exit $rc; fi
  sleep 120
done
echo "user present for 6 h - not run"; exit 9
