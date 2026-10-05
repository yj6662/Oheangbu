"""#308 baseline bookkeeping (SPEC-WORLD-BUILDING-AUDIT-308 §검증 1, 공통 계약 "기존 도구의 출력 덮어쓰기").

Several baseline commands write to fixed paths that earlier work already used. Copy those files away first, run the baseline
commands one at a time through the editor queue, then collect what they wrote into BuildingAudit308/baseline/.

    python Tools/Art/building308_baseline.py prior        # copy the current fixed-path outputs -> baseline/prior/<utc>/
    (run the baseline commands: Floating307 renderers:min=0.5:all, CompactFinish297 floaters:0.3, GroundFit299 ground-audit,
     TriCensus307 census:name=b308, Escort303Fix plan:main, NaturalSolids306 scan, Enclosure305 audit-scene:<main path>,
     python Tools/Art/protect_finish297.py verify)
    python Tools/Art/building308_baseline.py landscape    # audit_architecture296_landscape.py --out baseline/landscape296-independent-checks.json
    python Tools/Art/building308_baseline.py collect --since <utc of prior>   # copy outputs written after that moment -> baseline/

Read only for everything outside BuildingAudit308/ (files are copied, never moved or edited).
"""
import argparse, datetime, json, shutil, subprocess, sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
AUDIT = ROOT / 'Art/World/Compact/Rebuild/BuildingAudit308'
# fixed-path outputs the baseline commands overwrite (globs relative to the repo root)
FIXED = [
    'Art/World/Compact/Rebuild/GroundFit299/audit-*.json',
    'Art/World/Compact/Rebuild/Finish297/floaters.txt',
    'Art/World/Compact/Rebuild/Architecture296/Landscape/independent-checks.json',
    'Art/World/Compact/Rebuild/Enclosure305/Out/audit-*.txt',
    'Art/Performance/Perf307/tri-census-b308.txt',
    'Art/Playtest306/Checks/path-solids.txt',
]
# outputs written with time stamps (collected when newer than --since)
STAMPED = [
    'Art/Performance/Perf307/Floating/*_renderers.txt',
    'Art/World/Compact/Rebuild/Roadside303/EscortFix/plan-*.json',
]


def utc():
    return datetime.datetime.now(datetime.timezone.utc).strftime('%Y%m%dT%H%M%SZ')


def copy(src, dst_dir):
    dst = dst_dir / src.relative_to(ROOT)
    dst.parent.mkdir(parents=True, exist_ok=True); shutil.copy2(src, dst); return dst


def main():
    sys.stdout.reconfigure(encoding='utf-8', errors='replace')
    ap = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    ap.add_argument('sub', choices=['prior', 'collect', 'landscape']); ap.add_argument('--since', default='')
    a = ap.parse_args()
    if a.sub == 'prior':
        stamp = utc(); out = AUDIT / 'baseline/prior' / stamp; n = 0
        for g in FIXED:
            for f in sorted(ROOT.glob(g)): print('prior', copy(f, out)); n += 1
        (out).mkdir(parents=True, exist_ok=True)
        (out / 'prior.json').write_text(json.dumps({'utc': stamp, 'files': n}, indent=1), encoding='utf-8')
        print('%d file(s) -> %s ; collect later with --since %s' % (n, out, stamp))
    elif a.sub == 'landscape':
        dst = AUDIT / 'baseline/landscape296-independent-checks.json'; dst.parent.mkdir(parents=True, exist_ok=True)
        r = subprocess.run([sys.executable, str(ROOT / 'Tools/Art/audit_architecture296_landscape.py'), '--out', str(dst)], capture_output=True, text=True, encoding='utf-8', errors='replace')
        (AUDIT / 'baseline/landscape296.log').write_text(r.stdout + '\n' + r.stderr, encoding='utf-8')
        print('exit', r.returncode, '->', dst, '(a failure here is the known pre-#308 sheet/ledger SHA drift: Spec Temporary Exceptions)')
    else:
        if not a.since: raise SystemExit('--since <utc> (the prior stamp) is required')
        since = datetime.datetime.strptime(a.since, '%Y%m%dT%H%M%SZ').replace(tzinfo=datetime.timezone.utc).timestamp()
        out = AUDIT / 'baseline'; n = 0
        for g in FIXED + STAMPED:
            for f in sorted(ROOT.glob(g)):
                if f.stat().st_mtime >= since: print('collect', copy(f, out)); n += 1
        print('%d file(s) collected into %s' % (n, out))


if __name__ == '__main__':
    main()
