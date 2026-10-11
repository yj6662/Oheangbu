#!/bin/bash
# Trailer 4 (2026-10-11): one shooting round = one editor life, run ONLY while the user is away (last input >= IDLE_MIN s).
#   bash Tools/Trailer308/shoot.sh <label> <play|mum|world|car|fix:<take[,take]>>
#   TAKES="p_hook p_fight" ...   only these takes of the round        NOLIFE=1 ...   no editor restart first (this life has had no Play yet)
#   IDLE_MIN=900 ...             absence needed before Play opens      RETRY=2 ...    re-shoots of a take that fails verify.py
#   FG_ANY=1 ...                 do not insist on Unity being the foreground window   DRY=1 ...   print the plan, touch nothing
# Order (session.sh skeleton): absence -> no queue file / gpu lock -> editor status (dirty / playing = stop) -> life.ps1 restart
#   -> absence again -> foreground = Unity -> error CS 0 -> slot_hash308 record -> MemoryFix308 scene:open W_Demo_Main
#   -> RealmStream308 status (close-all when a Realm_ scene is open) -> takes.py (specs + manifest; fix: keeps the manifest entries)
#   -> [resource_guard --gpu-run: play_wait308b run hold -> SpellSmoke308 tutorial-skip -> per take: mem 23.5 | Presentation297 resume
#   | PRE commands (hold-teleport / guardian: / actors; a `guardian:` reply's "head N" regenerates the giant spec) | filmwait308.py
#   (settling + running both wait) | verify.py (+ the PRE replies) | encode.py on PASS, RETRY re-shoots on FAIL | POST commands
#   -> release.steps (release-if-held, Restart307 stale, clean)] -> slot_hash308 compare (+ park a left test slot) -> so-get DemoAllFinals
#   -> open the lobby -> mem / error CS.
# A queue conflict ("Unconsumed polish command") stops the round at once (ABORT marker). A "refused" reply (no held Play, no ground,
# unsaved changes, no mum service ...) is never a success: PRE = that attempt FAILs, scene:open / close-all = exit 8.
# Logs: Tools/Trailer308/logs/<label>/, ledger: Tools/Trailer308/ledger.jsonl (verify.py lines + round events).
# Exit: 0 ok | 1 a take FAILed | 2 memory stop | 3 hold failed | 6 error CS | 7 restart failed | 8 queue / lock / dirty / refused
#       9 user present BEFORE anything was touched (no restart happened) | 10 AFTER the restart: user came back or the foreground is not Unity
cd /c/Users/yj666/Oheangbu || exit 1
export PYTHONIOENCODING=utf-8
T=Tools/Trailer308
OPS=Tools/Unity/Stage308_ops
PW=Tools/Unity/Stage308_playsmoke/play_wait308b.py
PP=Tools/Unity/playtest_polish.py
SLOT=Tools/Unity/Stage308_playsmoke/slot_hash308.py
MAIN=Assets/_Project/Scenes/World/W_Demo_Main.unity
LOBBY=Assets/_Project/Art/UI/Loading270/W_Compact_Lobby.unity
CONTENT=Assets/_Project/Scenes/World/Main/WorldContent_Main.asset
EDLOG="$LOCALAPPDATA/Unity/Editor/Editor.log"
RETRY=${RETRY:-2}
MEM_LINE=${MEM_LINE:-23.5}
REFUSED='refused|no held Play|no ground under|no mum service|^no session|REFUSED to close|has unsaved changes'

ts(){ date +%H:%M:%S; }
idle(){ bash $OPS/mem.sh | sed -E 's/.*idle ([0-9,]+) s.*/\1/' | tr -d ','; }
fgwin(){ bash $OPS/mem.sh | sed -E 's/.*\| fg //'; }
unity_gb(){ bash $OPS/mem.sh | sed -E 's/^unity ([0-9.,]+) GB.*/\1/' | tr -d ','; }
event(){ # event <label> <event> <note>: one ledger line for the round itself
  python -c "import json,sys,time;print(json.dumps({'ts':time.strftime('%Y-%m-%dT%H:%M:%S'),'round':sys.argv[1],'event':sys.argv[2],'note':sys.argv[3]},ensure_ascii=False))" "$1" "$2" "$3" >> $T/ledger.jsonl
}
reply(){ # reply <file>: the result text of a queue reply file (show.py's parse), for grep
  python $OPS/show.py "$1" '.' 400
}
call(){ # call <file label> <Tool|editor> "<command>" [lines]: one queue call, reply in $D/<label>.txt, the judged lines printed
        # return 0 ok | 5 the editor REFUSED (reply matches $REFUSED) | 8 queue conflict (ABORT marker) | python's exit otherwise
  local lab=$1 tool=$2 cmd=$3 lines=${4:-6} type method
  if [ "$tool" = editor ]; then type=editor; method=$cmd; cmd=""; else type=Oheangbu.EditorTools.WorldMacro.$tool; method=Run; fi
  python $PP "$type" "$method" "$cmd" > "$D/$lab.txt" 2>&1; local c=$?
  if grep -aq "Unconsumed polish command" "$D/$lab.txt"; then echo "[$(ts)] QUEUE CONFLICT (Unconsumed polish command) at $lab - stopping"; touch "$D/ABORT"; return 8; fi
  reply "$D/$lab.txt" | head -$lines | sed 's/^/   /'
  if [ $c = 0 ] && reply "$D/$lab.txt" | grep -aqiE "$REFUSED"; then echo "   REFUSED at $lab ($tool $cmd)" | tee -a $D/summary.txt; return 5; fi
  return $c
}
editor_status(){ # the EditorStatus json ({scene, playing, dirty, commit}) within 90 s, or "ERROR ..."; its own pending command is removed on a timeout
  python - <<'EOF'
import sys, json, re, contextlib, io
sys.path.insert(0, 'Tools/Unity')
from playtest_polish import call, OUT
try:
    with contextlib.redirect_stdout(io.StringIO()):
        r = call('editor', 'status', '', 90)
    print(r.get('result', ''))
except TimeoutError as e:
    token = re.search(r'response_([0-9a-f]{32})', str(e))
    p = OUT / 'command.json'
    try:
        if token and p.exists() and json.loads(p.read_text(encoding='utf-8')).get('id') == token.group(1): p.unlink()   # ours, unanswered
    except Exception: pass
    print('ERROR editor not answering within 90 s')
except Exception as e:
    print('ERROR ' + str(e))
EOF
}
round_takes(){ # the manifest's take list for a round, minus takes.py's excluded (TBD) ones
  python - "$1" <<'EOF'
import json,sys
m=json.load(open('Tools/Trailer308/manifest.json',encoding='utf-8'))
ex=set(m.get('excluded',[]))
print(' '.join(t for t in m['rounds'][sys.argv[1]] if t not in ex))
EOF
}
missing_takes(){ # of the comma list, the takes with no manifest entry (a fix: round regenerates only these)
  python - "$1" <<'EOF'
import json,sys
from pathlib import Path
p=Path('Tools/Trailer308/manifest.json')
m=json.loads(p.read_text(encoding='utf-8')) if p.exists() else {'takes':{}}
print(','.join(t for t in sys.argv[1].split(',') if t and t not in m.get('takes',{})))
EOF
}

# ---------------------------------------------------------------- the round inside the GPU lock (re-entered with --inner)
if [ "$1" = "--inner" ]; then
  label=$2; round=$3; shift 3; takes="$*"
  D=$T/logs/$label
  echo "=== inner $label $round: $takes $(ts)"
  python $PW run hold > $D/hold.txt 2>&1; c=$?
  grep -a "ENDED\|NOT STARTED\|CAP \|STOP\|precondition\|refused\|QUEUE" $D/hold.txt | tail -3
  if [ $c != 0 ]; then echo "HOLD FAILED exit $c"; event "$label" hold-failed "exit $c"; exit 3; fi
  sleep 3
  call tutorial_skip SpellSmoke308 "tutorial-skip" 1 || { [ -f $D/ABORT ] && exit 8; }
  sleep 12
  call tutorial_status SpellSmoke308 "tutorial-status" 1
  result=0
  for take in $takes; do
    [ -f $D/ABORT ] && { result=8; break; }
    python $PW mem $MEM_LINE > $D/mem_$take.txt 2>&1; mc=$?; tail -1 $D/mem_$take.txt
    if [ $mc = 1 ]; then echo "memory line reached before $take"; event "$label" memory-stop "before $take"; result=2; break; fi
    plan=$(python $T/takes.py --plan $take) || { echo "no plan for $take"; result=1; continue; }
    spec=$(echo "$plan" | sed -n 's/^SPEC //p'); limit=$(echo "$plan" | sed -n 's/^LIMIT //p')
    mum=$(echo "$plan" | grep -c "^MUM 1"); echo "$plan" | grep -q "^TBD " && echo "   note: $(echo "$plan" | grep '^TBD ')"
    echo "--- $take ($(echo "$plan" | sed -n 's/^TYPE //p'), limit $limit s) $(ts)"
    pass=0
    for attempt in $(seq 1 $((RETRY + 1))); do
      [ -f $D/ABORT ] && break
      call resume_${take}_$attempt Presentation297 "resume" 1 || { [ -f $D/ABORT ] && break; }
      n=0; pre_fail=""
      while read -r p; do
        [ -z "$p" ] && continue
        n=$((n + 1)); tool=${p%%|*}; cmd=${p#*|}
        call pre_${take}_${attempt}_$n "$tool" "$cmd" 3; pc=$?
        [ -f $D/ABORT ] && break
        [ $pc = 5 ] && pre_fail="$tool $cmd"
        if [[ "$cmd" == guardian:* ]]; then
          # only the `guardian:` reply carries the head height ("guardian at (x, y, z) head N"); guardian-info has root/feet/ground/clock
          head=$(reply $D/pre_${take}_${attempt}_$n.txt | grep -ao "head [0-9.]*" | head -1 | cut -d' ' -f2)
          if [ -n "$head" ]; then python $T/takes.py --take $take --giant-head $head | head -1 | sed 's/^/   regenerated: /'
          else echo "   WARNING guardian: reply has no 'head N' - the giant spec keeps its estimate" | tee -a $D/summary.txt; fi
        fi
      done <<< "$(echo "$plan" | sed -n 's/^PRE //p')"
      [ -f $D/ABORT ] && break
      pre_files=$(ls $D/pre_${take}_${attempt}_*.txt 2>/dev/null | tr '\n' ' ')
      if [ -n "$pre_fail" ]; then
        echo "   attempt $attempt of $((RETRY + 1)) FAIL: PRE refused ($pre_fail) $(ts)"; event "$label" pre-refused "$take attempt $attempt: $pre_fail"; continue
      fi
      sleep 2
      python $T/filmwait308.py "$spec" --limit $limit > $D/${take}_a$attempt.txt 2>&1; fc=$?
      echo "   film: $(tail -1 $D/${take}_a$attempt.txt) (exit $fc)"
      if grep -aq "Unconsumed polish command" $D/${take}_a$attempt.txt; then echo "[$(ts)] QUEUE CONFLICT during the film - stopping"; touch $D/ABORT; break; fi
      extra=""
      if [ "$mum" = 1 ]; then call mum_after_${take}_$attempt Presentation297 "mum-status" 1; extra=$D/mum_after_${take}_$attempt.txt; fi
      python $T/verify.py $take --round $label ${extra:+--extra "$extra"} ${pre_files:+--pre $pre_files} 2>&1 | tee -a $D/verify.txt | sed 's/^/   /'; vc=${PIPESTATUS[0]}
      if [ $vc = 0 ]; then
        python $T/encode.py $take 2>&1 | tee -a $D/encode.txt | sed 's/^/   /'
        pass=1; break
      fi
      echo "   attempt $attempt of $((RETRY + 1)) FAIL $(ts)"
    done
    [ $pass = 1 ] || result=1
    n=0
    while read -r p; do
      [ -z "$p" ] && continue
      n=$((n + 1)); tool=${p%%|*}; cmd=${p#*|}
      call post_${take}_$n "$tool" "$cmd" 1
    done <<< "$(echo "$plan" | sed -n 's/^POST //p')"
  done
  # the held Play is released through the steps runner (release-if-held = play-release only when a Play is really up)
  printf 'finally:\nrelease-if-held\nsleep 8\ncall Restart307 Run stale\nclean\n' > $D/release.steps
  python $PW script $D/release.steps > $D/release.txt 2>&1; rc=$?
  grep -a "ENDED\|RESULT\|stale\|editor:\|SEQUENCE" $D/release.txt | tail -4
  [ -f $D/ABORT ] && exit 8
  [ $rc = 0 ] || { echo "RELEASE exit $rc"; [ $result = 0 ] && result=1; }
  exit $result
fi

# ---------------------------------------------------------------- the outer round
label=$1; round=$2
case "$round" in play|mum|world|car) ;; fix:*) ;; *) echo "usage: shoot.sh <label> <play|mum|world|car|fix:<take[,take]>>"; exit 2;; esac
[ -z "$label" ] && { echo "usage: shoot.sh <label> <round>"; exit 2; }
D=$T/logs/$label; mkdir -p $D; rm -f $D/ABORT
echo "[$label] $round $(date +%F) $(ts) RETRY=$RETRY IDLE_MIN=${IDLE_MIN:-900} NOLIFE=${NOLIFE:-} TAKES=${TAKES:-}" | tee $D/summary.txt

# 1. specs + manifest for the round (TBD takes are excluded unless TAKES names them explicitly)
if [[ "$round" == fix:* ]]; then
  # a fix: round shoots what the manifest holds (letters, --giant-head, --giant-site set earlier with takes.py --take ... stay);
  # only a take without a manifest entry is generated here
  list=${round#fix:}; takes=${list//,/ }
  miss=$(missing_takes "$list")
  if [ -n "$miss" ]; then python $T/takes.py --take "$miss" --allow-tbd > $D/takes.txt 2>&1; tc=$?; echo "generated (no manifest entry): $miss"
  else echo "manifest entries kept for: $takes" > $D/takes.txt; tc=0; fi
else
  python $T/takes.py --round $round > $D/takes.txt 2>&1; tc=$?; takes=$(round_takes $round)
fi
[ -n "$TAKES" ] && takes="$TAKES"
grep -a "TBD\|excluded\|overrides" $D/takes.txt | head -8
echo "takes: $takes" | tee -a $D/summary.txt
[ -z "$takes" ] && { echo "no takes to shoot"; exit 2; }
if [ -n "$DRY" ]; then echo "DRY: would restart the editor ($( [ -z "$NOLIFE" ] && echo yes || echo no)), hold a Play and shoot: $takes"; for t in $takes; do python $T/takes.py --plan $t | sed "s/^/   $t: /"; done; exit 0; fi

# 2. absence, queue, lock, editor state, restart, foreground, errors
i=$(idle); echo "[$label] $(ts) idle $i s"
if [ "${i:-0}" -lt "${IDLE_MIN:-900}" ]; then echo "USER PRESENT (idle $i s < ${IDLE_MIN:-900}) - nothing touched"; exit 9; fi
if ls Art/PlaytestPolish/Unity/command.json Art/SpellVFX120/command.json Tools/.gpu_lock >/dev/null 2>&1; then echo "queue file or gpu lock present - stop"; ls Art/PlaytestPolish/Unity/command.json Art/SpellVFX120/command.json Tools/.gpu_lock 2>/dev/null; exit 8; fi
gb=$(unity_gb)
if [ -n "$gb" ] && [ "${gb%%.*}" != 0 ] 2>/dev/null; then
  # life.ps1 rule: never with a dirty scene or a pending command; a running Play (another session's) is not ours to close either
  st=$(editor_status); echo "[$label] editor status: $st" | tee -a $D/summary.txt
  case "$st" in
    ERROR*) echo "editor not answering - restart not attempted (a dialog / compile / another session?)"; exit 8 ;;
    *'"dirty":true'*) echo "scene has unsaved changes - life.ps1 not called (save or discard first)"; exit 8 ;;
    *'"playing":true'*) echo "a Play is up in the editor - not ours to close"; exit 8 ;;
  esac
else
  echo "[$label] no Unity.exe - life.ps1 will start one" | tee -a $D/summary.txt
fi
if [ -z "$NOLIFE" ]; then
  powershell -NoProfile -ExecutionPolicy Bypass -File $OPS/life.ps1 | tee -a $D/summary.txt; lrc=${PIPESTATUS[0]}
  [ "$lrc" = 0 ] || { echo "editor restart failed (life.ps1 exit $lrc: 2 = not closed, a dialog may be open; 3 = not up after 600 s)"; event "$label" restart-failed "life.ps1 exit $lrc"; exit 7; }
  i=$(idle); if [ "${i:-0}" -lt 120 ]; then echo "USER CAME BACK during the restart (idle $i s) - Play not opened"; exit 10; fi
fi
w=$(fgwin); echo "[$label] foreground: $w"
if [ -z "$FG_ANY" ] && [[ "$w" != Oheangbu\ -* ]]; then echo "foreground window is not Unity ($w) - Play not opened"; exit 10; fi
bash $OPS/mem.sh | tee -a $D/summary.txt
e=$(grep -c "error CS" "$EDLOG"); if [ "$e" != 0 ]; then echo "error CS $e after the restart - Play not opened"; exit 6; fi
python $SLOT record | head -3

# 3. the main scene alone (no Realm_ scene open: the loader streams them in Play)
call scene_open MemoryFix308 "scene:open:$MAIN" 1 || exit 8
call stream_status RealmStream308 "status" 8 || exit 8
if grep -a "scene .*Realm_" $D/stream_status.txt | grep -avq "not loaded"; then
  echo "   Realm_ scene(s) open in the editor - close-all"; call stream_close RealmStream308 "close-all" 2 || exit 8
  call stream_status2 RealmStream308 "status" 8
fi
python $PW clean 2>&1 | tail -1

# 4. the round under the GPU lock
event "$label" start "$round: $takes"
echo "=== round $label $round $(ts)"
python Tools/resource_guard.py --gpu-run trailer-$label -- bash $T/shoot.sh --inner "$label" "$round" $takes 2>&1 | tee $D/round.txt
rc=${PIPESTATUS[0]}
echo "=== exit $rc $(ts)"

# 5. after the Play: real slot, finals flag, lobby, memory, errors
python $SLOT compare > $D/slot.txt; tail -3 $D/slot.txt
for f in $(grep -a "^LEFT" $D/slot.txt | sed -E 's/.*world-main(_[A-Za-z0-9_]+)\.json.*/\1/' | sort -u); do
  python $PP Oheangbu.EditorTools.WorldMacro.SpellSmoke308 Run "saves:park:$f" 2>&1 | grep -a -o "parked[^\"]*" | head -1
done
if grep -aq "^LEFT" $D/slot.txt; then
  python $PP Oheangbu.EditorTools.WorldMacro.Restart307 Run "stale:clear" 2>&1 | grep -a -o "session suffix[^\"]*" | head -2
  python $SLOT compare | tail -1
fi
call finals Presentation297 "so-get:$CONTENT:DemoAllFinals" 1
grep -aq "True" $D/finals.txt && finals=True || finals="NOT True"
call lobby MemoryFix308 "scene:open:$LOBBY" 1
bash $OPS/mem.sh | tee -a $D/summary.txt
echo "error CS: $(grep -c "error CS" "$EDLOG")" | tee -a $D/summary.txt
{
  echo "round exit $rc | slot: $(tail -1 $D/slot.txt) | DemoAllFinals $finals"
  grep -a "^VERIFY" $D/verify.txt 2>/dev/null
  grep -a "^ENCODED\|^ENCODE " $D/encode.txt 2>/dev/null
} | tee -a $D/summary.txt
event "$label" end "exit $rc; $(grep -ac '^VERIFY .* PASS' $D/verify.txt 2>/dev/null) PASS / $(grep -ac '^VERIFY .* FAIL' $D/verify.txt 2>/dev/null) FAIL; finals $finals"
exit $rc
