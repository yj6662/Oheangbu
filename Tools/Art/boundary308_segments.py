# -*- coding: utf-8 -*-
"""SPEC-WORLD-CLIFF-BOUNDARY-308 1b - the stage-1b plan lines in the OLD plan shape (the shape of plan/boundary308.json).

    python Tools/Art/boundary308_segments.py            -> Art/World/Compact/Rebuild/CliffBoundary308/plan/boundary308_1b_segments.json
    python Tools/Art/boundary308_segments.py --check    -> re-builds in memory, compares with the file on disk (exit 1 when different)

Why: plan/boundary308_p4.json (308.plan.4, stage 1b) has no "segments" list, so the two readers of the old shape cannot take it:
    Content308.SceneChecks.cs PlanLines308   keep-out lines: segments[].{id, name_ko, name_long, border, stage, polyline,
                                             technique.closed_ring, technique.params.{upper, low_ref, up_ref, top_w, back, cut_w}}
                                             + gate_wall.{west, east}.line + gate_wall.gate.{A, B}; version / date for the report
    Tools/Art/cartography308.py              map cliff strokes: segments[].{id, stage, status ('IMPLEMENTED' = built), class, polyline}
This file is those keys and nothing else, made from ops308_v6.json (every op line, its stage, its numbers with the 1b
stage_overrides applied) and boundary308_p4.json (the wall lines and the gate as decided in D308-9d). No clock, no RNG: the same
two inputs give the same bytes. Read-only on the Unity project.
"""
import argparse, hashlib, json, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
PLAN = ROOT / 'Art/World/Compact/Rebuild/CliffBoundary308/plan'
OPS = PLAN / 'ops308_v6.json'
P4 = PLAN / 'boundary308_p4.json'
OUT = PLAN / 'boundary308_1b_segments.json'
STAGE = '1b'
BUILT = ('1a', '1b')                                            # stages whose ops are in height_p1b.bytes (scarp308: stage <= 1b)
NOT_PARAMS = ('id', 'points', 'name', 'ko', 'role', 'border', 'stage', 'stage_overrides', 'reference_only', 'reference_reason', 'replaces')


def sha(p):
    return hashlib.sha256(Path(p).read_bytes()).hexdigest()


def rel(p):
    return str(Path(p).resolve().relative_to(ROOT)).replace('\\', '/')


def seg_class(role):
    """the 'class' of the old shape (plan.3: inner / outer / wall-anchor) from the op's role."""
    r = (role or '').upper()
    return 'outer' if r.startswith('OUTER') else 'wall-anchor' if r.startswith('WALL-ANCHOR') else 'inner'


def build():
    ops = json.loads(OPS.read_text(encoding='utf-8')); p4 = json.loads(P4.read_text(encoding='utf-8'))
    if p4['inputs']['ops']['sha256'] != sha(OPS):
        raise SystemExit(f"REFUSED: {rel(P4)} was made from ops sha {p4['inputs']['ops']['sha256'][:12]}, {rel(OPS)} is {sha(OPS)[:12]}")
    segs = []
    for o in ops['segments']:
        params = {k: v for k, v in o.items() if k not in NOT_PARAMS}
        over = (o.get('stage_overrides') or {})
        for st in ops['stages']:                                 # overrides of every stage up to 1b, in stage order
            if st in over: params.update(over[st])
            if st == STAGE: break
        built = not o.get('reference_only') and o.get('stage') in BUILT
        segs.append(dict(
            id=o['id'], name_ko=o.get('ko', ''), name_long=o.get('name', ''), stage=o.get('stage', ''), border=o.get('border', ''),
            **{'class': seg_class(o.get('role'))},
            status=f"IMPLEMENTED offline (ops v{ops['version'].rsplit('.', 1)[-1]}, stage {STAGE} height)" if built else 'REFERENCE ONLY (not built)',
            technique=dict(closed_ring=bool(o.get('closed')), params=params),
            polyline=[[float(p[0]), float(p[1])] for p in o['points']]))
    gw = p4['gate_wall']
    return dict(
        id='boundary308', version=p4['version'] + '+segments.1', date=p4['date'], stage=STAGE,
        status='TEST - generated file: the stage-1b plan lines in the shape of boundary308.json (308.plan.3). Offline data, nothing measured in the engine',
        doc='made by Tools/Art/boundary308_segments.py from the two inputs below; do not edit by hand. Readers: Content308 keep-out '
            '(content308_scene.json keepout.plan_files["1b"]) and the map baker (map308_lib.STAGES["1b"]). boundary308.json stays the stage-1a file',
        spec=p4['spec'], decisions=p4['decisions'],
        inputs=[dict(role='ops', file=rel(OPS), sha256=sha(OPS), version=ops['version']), dict(role='plan', file=rel(P4), sha256=sha(P4), version=p4['version'])],
        segments=segs,
        gate_wall=dict(decision=gw['decision'], gate=dict(A=gw['gate']['A'], B=gw['gate']['B'], opens_on=gw['gate']['opens_on'], floor_y=gw['gate'].get('floor_y')),
                       west=dict(line=gw['lines']['west']), east=dict(line=gw['lines']['east'])))


def main():
    try:
        sys.stdout.reconfigure(encoding='utf-8')
    except Exception:
        pass
    ap = argparse.ArgumentParser(); ap.add_argument('--check', action='store_true'); ap.add_argument('--out', default=str(OUT))
    a = ap.parse_args()
    data = (json.dumps(build(), ensure_ascii=False, indent=1) + '\n').encode('utf-8')
    out = Path(a.out).resolve()
    try:
        out.relative_to((ROOT / 'Oheangbu').resolve()); raise SystemExit(f'REFUSED: {out} is inside the Unity project')
    except ValueError:
        pass
    h = hashlib.sha256(data).hexdigest()
    if a.check:
        same = out.exists() and out.read_bytes() == data
        print(f"[boundary308_segments] check {rel(out)}: {'IDENTICAL' if same else 'DIFFERENT'} (sha {h[:12]})"); sys.exit(0 if same else 1)
    out.write_bytes(data)
    d = json.loads(data)
    built = [s['id'] for s in d['segments'] if s['status'].startswith('IMPLEMENTED')]
    print(f"[boundary308_segments] wrote {rel(out)} sha {h[:12]}: {len(d['segments'])} segments ({len(built)} built: {', '.join(built)}), "
          f"wall west {len(d['gate_wall']['west']['line'])} pts / east {len(d['gate_wall']['east']['line'])} pts, version {d['version']}")


if __name__ == '__main__':
    main()
