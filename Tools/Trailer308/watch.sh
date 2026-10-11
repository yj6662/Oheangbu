#!/bin/bash
# Trailer 4 (2026-10-11): the night watch. Waits until the user has been away for IDLE_MIN s (mem.sh idle), then runs the rounds
# R1 play -> R2 mum -> R3 world -> R4 car through shoot.sh, each in a fresh editor life (no NOLIFE). When the user is back
# (idle < 120 s, or shoot.sh refused) the next round is not opened and the watch waits for a full absence again.
#   bash Tools/Trailer308/watch.sh                 (background: nohup bash Tools/Trailer308/watch.sh > /dev/null 2>&1 &)
#   IDLE_MIN=900 ROUNDS="play mum world car" POLL=60 FG_FAILS=2 ...   touch Tools/Trailer308/logs/watch/STOP to end it after the current round
# shoot.sh exit codes and what the watch does:
#   9   user present before anything was touched (no restart)      -> wait POLL s, ask again (as often as needed: the user is here)
#   10  AFTER an editor restart: user came back / foreground is not Unity -> wait POLL s, try once more; FG_FAILS (2) in a row ends the
#       watch (a foreground stuck on a dialog or a console would otherwise restart the editor all night, 3-10 min each time)
#   6 / 7 / 8  error CS / restart failed / queue, lock, dirty scene, refused   -> the watch ends: that needs a person
#   0 / 1 / 2 / 3  the round ran (ok / a take FAILed / memory stop / hold failed) -> next round
# Summary: Tools/Trailer308/logs/watch.txt (one line per round) ; the watch log: Tools/Trailer308/logs/watch/watch_<date>.log
cd /c/Users/yj666/Oheangbu || exit 1
export PYTHONIOENCODING=utf-8
T=Tools/Trailer308
OPS=Tools/Unity/Stage308_ops
IDLE_MIN=${IDLE_MIN:-900}
POLL=${POLL:-60}
FG_FAILS=${FG_FAILS:-2}
ROUNDS=${ROUNDS:-"play mum world car"}
W=$T/logs/watch; mkdir -p $W
LOG=$W/watch_$(date +%Y%m%d_%H%M).log
SUM=$T/logs/watch.txt
STOP=$W/STOP
rm -f $STOP

ts(){ date +%H:%M:%S; }
idle(){ bash $OPS/mem.sh | sed -E 's/.*idle ([0-9,]+) s.*/\1/' | tr -d ','; }
say(){ echo "[$(ts)] $*" | tee -a $LOG; }

say "watch start: rounds [$ROUNDS] idle >= $IDLE_MIN s, poll $POLL s, FG_FAILS $FG_FAILS (STOP file: $STOP)"
echo "=== watch $(date +%F) $(ts): rounds [$ROUNDS]" >> $SUM
n=0
for round in $ROUNDS; do
  n=$((n + 1)); fgfail=0
  while true; do
    [ -f $STOP ] && { say "STOP file seen - ending the watch before $round"; echo "stopped before $round $(ts)" >> $SUM; exit 0; }
    i=$(idle)
    if [ "${i:-0}" -ge "$IDLE_MIN" ]; then
      label="r${n}_${round}_$(date +%H%M)"
      say "user away (idle $i s) - round $n $round as $label"
      bash $T/shoot.sh "$label" "$round" >> $LOG 2>&1; rc=$?
      line="$label exit $rc | $(grep -a '^round exit' $T/logs/$label/summary.txt 2>/dev/null | head -1)"
      case $rc in
        9) say "round $round not opened (user present, nothing touched) - waiting $POLL s"; sleep $POLL; continue ;;
        10) fgfail=$((fgfail + 1))
            if [ $fgfail -ge $FG_FAILS ]; then
              say "round $round: $fgfail restarts in a row ended with the user back / a foreground that is not Unity - the watch ends; see $T/logs/$label/"
              echo "$line  <- WATCH ENDED (foreground / user back after restart x$fgfail)" >> $SUM; exit 10
            fi
            say "round $round not opened after the restart (user back / foreground not Unity, $fgfail of $FG_FAILS) - waiting $POLL s"; sleep $POLL; continue ;;
        6|7|8) say "round $round stopped on the environment (exit $rc) - the watch ends; see $T/logs/$label/"; echo "$line  <- WATCH ENDED" >> $SUM; exit $rc ;;
        *) say "round $round done: exit $rc"; echo "$line" >> $SUM; break ;;
      esac
    else
      [ $(( $(date +%s) % 600 )) -lt $POLL ] && say "waiting: idle $i s < $IDLE_MIN s (before $round)"
      sleep $POLL
    fi
  done
  # back between rounds? the next loop waits for a full absence anyway; say so when it is the case
  i=$(idle); [ "${i:-0}" -lt 120 ] && say "user is back (idle $i s) - the next round waits for a full absence"
done
say "all rounds done"
{
  echo "--- ledger tail"; tail -40 $T/ledger.jsonl 2>/dev/null | python -c "import sys,json
for l in sys.stdin:
    try: d=json.loads(l)
    except ValueError: continue
    if 'take' in d: print('  %-14s %-5s %s hits %s | %s' % (d['take'], d['status'], d.get('round',''), d.get('hits',0), d.get('reason','')))
    else: print('  [%s] %s %s' % (d.get('round',''), d.get('event',''), d.get('note','')))"
} >> $SUM
say "summary -> $SUM"
