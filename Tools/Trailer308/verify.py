"""Trailer 4: judge one take from Presentation297's film-state.json and write one ledger line.

  python Tools/Trailer308/verify.py <take> [--state <json>] [--round <label>] [--extra <file>] [--pre <file>...] [--manifest <json>] [--ledger <jsonl>] [--no-ledger]

Rules (the manifest's verify block from takes.py; without a manifest they are inferred from the take name):
  every take      status "done", frames == total, no error, PNG count == frames (skipped when the take is already encoded)
  glyph takes     one "OK <letter>" commit per letter the spec writes (p_fight: both letters)
  combat takes    (p_hook*, p_fight*, p_burst_1..4) at least one "HIT <id> dmg=.. hp=.. [KILLED] frame=N", after the first OK
  뭄 takes         "OK 뭄" and, when --extra holds a mum-status reply, active >= 1
  car takes       a "board True" commit
  world takes     frames == total only
  --pre files     the PRE command replies of the attempt (shoot.sh pre_<take>_<n>_*.txt): a "refused" / "no held Play" / "no ground"
                  reply fails the take, and the last "teleported to (x, y, z)" must sit within 5 m (horizontal) and 8 m (height)
                  of the manifest feet - a snap teleport that landed on the hill above the mine gallery is caught here.
Ledger line (Tools/Trailer308/ledger.jsonl): {ts, take, round, status, frames, total, hits, commits, reason, ok, first_hit_frame, killed[, teleported]}
Exit 0 = PASS, 1 = FAIL, 2 = state file missing / unreadable.
"""
import argparse
import json
import re
import sys
import time
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
HERE = Path(__file__).resolve().parent
FILMS = ROOT / 'Art/Presentation297/Films'
STATE = FILMS / 'film-state.json'
MANIFEST = HERE / 'manifest.json'
LEDGER = HERE / 'ledger.jsonl'

RE_OK = re.compile(r'^OK (\S+)(?:.*?frame=(\d+))?')
RE_HIT = re.compile(r'^HIT (\S+) dmg=(\S+) hp=(\S+)( KILLED)?(?:.*?frame=(\d+))?')
RE_BOARD = re.compile(r'^board (True|False)')
RE_GLYPH = re.compile(r'^glyph (\S+) strokes=(\d+)')
RE_TELEPORT = re.compile(r'teleported to \(\s*(-?[\d.]+),\s*(-?[\d.]+),\s*(-?[\d.]+)\s*\)')   # Map307Capture hold-teleport reply
RE_REFUSED = re.compile(r'refused|no held Play|no ground under|no mum service|no session|Unconsumed polish command', re.I)
TELEPORT_XZ_M, TELEPORT_Y_M = 5.0, 30.0  # 높이는 추정값 자리(갱구·광장·마을)가 많아 30 m로 완화(수평 5 m는 유지)


def reply_text(raw):
    """the editor's answer inside one playtest_polish reply file: result + error of the JSON (show.py's parse), else the raw text"""
    i = raw.find('{')
    if i >= 0:
        try:
            d = json.JSONDecoder().raw_decode(raw[i:])[0]
            if isinstance(d, dict): return '\n'.join(str(d.get(k) or '') for k in ('result', 'error'))
        except ValueError:
            pass
    return raw


def judge_pre(pre_text, feet):
    """(reasons, facts) from the PRE replies (reply_text of each file joined): a refusal fails, and the last teleport must land
    near the planned feet"""
    reasons, facts = [], {}
    if not pre_text: return reasons, facts
    for line in pre_text.splitlines():
        if RE_REFUSED.search(line) and 'teleported' not in line:
            reasons.append('PRE %s' % line.strip()[:120]); break
    tp = RE_TELEPORT.findall(pre_text)
    if tp:
        x, y, z = (float(v) for v in tp[-1])
        facts['teleported'] = [x, y, z]
        if feet and len(feet) == 3:
            dxz = ((x - feet[0]) ** 2 + (z - feet[2]) ** 2) ** 0.5; dy = abs(y - feet[1])
            if dxz > TELEPORT_XZ_M or dy > TELEPORT_Y_M:
                reasons.append('teleported to (%.1f, %.1f, %.1f) is %.1f m / %.1f m high off the planned feet (%.1f, %.1f, %.1f)' % (x, y, z, dxz, dy, feet[0], feet[1], feet[2]))
    return reasons, facts


def infer_rules(take, spec_path=None):
    """rules from the name (and the spec's glyph inputs when the spec file is there)"""
    glyphs = []
    if spec_path and Path(spec_path).exists():
        try:
            for e in json.loads(Path(spec_path).read_text(encoding='utf-8')).get('inputs') or []:
                if e.get('type') == 'glyph' and e.get('key'): glyphs.append(e['key'])
        except ValueError:
            pass
    combat = take.startswith('p_hook') or take.startswith('p_fight') or re.match(r'p_burst_[1-4]$', take) is not None
    return {'glyphs': glyphs, 'hits': combat, 'mum': take.startswith('p_mum'), 'board': take.startswith('p_car'), 'world': take.startswith('w_')}


def judge(take, state, rules, png_count=None, extra=''):
    """(status, reason, facts) - every failed rule is named in the reason"""
    reasons, facts = [], {}
    commits = state.get('commits') or []
    frames, total = int(state.get('frames') or 0), int(state.get('total') or 0)
    facts.update(frames=frames, total=total, commits=len(commits))
    if state.get('name') != take: reasons.append('state is for %r' % state.get('name'))
    if state.get('status') != 'done': reasons.append('status %s' % state.get('status'))
    if state.get('error'): reasons.append('error: %s' % state.get('error'))
    if total <= 0 or frames != total: reasons.append('frames %d/%d' % (frames, total))
    if png_count is not None and png_count != frames: reasons.append('%d PNG for %d frames' % (png_count, frames))
    oks, hits, boards, glyph_lines = [], [], [], []
    for c in commits:
        m = RE_OK.match(c)
        if m: oks.append((m.group(1), int(m.group(2)) if m.group(2) else -1)); continue
        m = RE_HIT.match(c)
        if m: hits.append({'id': m.group(1), 'dmg': m.group(2), 'hp': m.group(3), 'killed': bool(m.group(4)), 'frame': int(m.group(5)) if m.group(5) else -1}); continue
        m = RE_BOARD.match(c)
        if m: boards.append(m.group(1) == 'True'); continue
        m = RE_GLYPH.match(c)
        if m: glyph_lines.append(m.group(1))
    facts.update(ok=[o[0] for o in oks], hits=len(hits), first_hit_frame=min([h['frame'] for h in hits if h['frame'] >= 0], default=-1),
                 killed=any(h['killed'] for h in hits), glyph_expanded=glyph_lines)
    for letter in rules.get('glyphs') or []:
        want = sum(1 for g in (rules.get('glyphs') or []) if g == letter)
        got = sum(1 for o in oks if o[0] == letter)
        if got < want: reasons.append('OK %s %d/%d' % (letter, got, want))
    if rules.get('hits'):
        if not hits: reasons.append('no HIT')
        else:
            first_ok = min([f for _, f in oks if f >= 0], default=-1)
            first_hit = facts['first_hit_frame']
            if first_ok >= 0 and first_hit >= 0 and first_hit < first_ok: reasons.append('HIT frame %d before OK frame %d' % (first_hit, first_ok))
    if rules.get('mum'):
        if not any(o[0] == '뭄' for o in oks): reasons.append('no OK 뭄')
        m = re.search(r'active=(\d+)', extra or '')
        if m:
            facts['mum_active'] = int(m.group(1))
            if int(m.group(1)) < 1: reasons.append('mum-status active=%s' % m.group(1))
        elif extra: reasons.append('mum-status unreadable')
        if 'lastFailure=' in (extra or ''):
            lf = re.search(r'lastFailure=(\S*)', extra).group(1)
            if lf: facts['mum_last_failure'] = lf
    if rules.get('board') and not any(boards): reasons.append('board False' if boards else 'no board commit')
    if rules.get('q') and not any('q=True' in c for c in commits): facts['q_seen'] = False
    return ('PASS' if not reasons else 'FAIL'), ('; '.join(reasons) or 'ok'), facts


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('take'); ap.add_argument('--state', default=str(STATE)); ap.add_argument('--round', default='')
    ap.add_argument('--extra', default=''); ap.add_argument('--manifest', default=str(MANIFEST)); ap.add_argument('--ledger', default=str(LEDGER))
    ap.add_argument('--no-ledger', action='store_true'); ap.add_argument('--films', default=str(FILMS))
    ap.add_argument('--pre', nargs='*', default=[], help='PRE command reply files of this attempt (refusals fail; the teleport must land near the manifest feet)')
    a = ap.parse_args()
    state_path = Path(a.state)
    try:
        state = json.loads(state_path.read_text(encoding='utf-8-sig'))
    except (OSError, ValueError) as e:
        print('VERIFY %s FAIL state unreadable: %s' % (a.take, e)); return 2
    rules, spec_path, feet = None, None, None
    try:
        m = json.loads(Path(a.manifest).read_text(encoding='utf-8'))
        entry = m.get('takes', {}).get(a.take)
        if entry: rules = entry.get('verify'); spec_path = entry.get('spec'); feet = entry.get('feet')
    except (OSError, ValueError):
        pass
    if rules is None: rules = infer_rules(a.take, spec_path or (Path(a.films) / 'specs' / (a.take + '.json')))
    folder = Path(a.films) / a.take
    pngs = len(list(folder.glob('f*.png'))) if folder.is_dir() else 0
    png_count = pngs if pngs > 0 or not (Path(a.films) / (a.take + '.mp4')).exists() else None
    extra = ''
    if a.extra:
        try: extra = Path(a.extra).read_text(encoding='utf-8', errors='replace')
        except OSError: extra = ''
    pre_text = ''
    for f in a.pre:
        try: pre_text += reply_text(Path(f).read_text(encoding='utf-8', errors='replace')) + '\n'
        except OSError: pass
    status, reason, facts = judge(a.take, state, rules, png_count, extra)
    pre_reasons, pre_facts = judge_pre(pre_text, feet)
    facts.update(pre_facts)
    if pre_reasons:
        status = 'FAIL'; reason = '; '.join(pre_reasons + ([] if reason == 'ok' else [reason]))
    line = {'ts': time.strftime('%Y-%m-%dT%H:%M:%S'), 'take': a.take, 'round': a.round, 'status': status, 'frames': facts['frames'], 'total': facts['total'],
            'hits': facts['hits'], 'commits': facts['commits'], 'reason': reason, 'ok': facts['ok'], 'first_hit_frame': facts['first_hit_frame'], 'killed': facts['killed']}
    for k in ('mum_active', 'mum_last_failure', 'teleported'):
        if k in facts: line[k] = facts[k]
    if not a.no_ledger:
        with open(a.ledger, 'a', encoding='utf-8') as f: f.write(json.dumps(line, ensure_ascii=False) + '\n')
    print('VERIFY %s %s %s | frames %d/%d hits %d ok %s%s' % (a.take, status, reason, facts['frames'], facts['total'], facts['hits'], ','.join(facts['ok']) or '-',
                                                              ' first HIT f%d%s' % (facts['first_hit_frame'], ' KILLED' if facts['killed'] else '') if facts['hits'] else ''))
    return 0 if status == 'PASS' else 1


if __name__ == '__main__':
    sys.exit(main())
