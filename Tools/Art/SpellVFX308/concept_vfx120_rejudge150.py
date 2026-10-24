#!/usr/bin/env python3
"""vfx120 stage 2: re-judge existing concept pictures under D308-150 V1 (2026-10-25 user decision): a concept sheet is
judged at >=97 only on form grammar, ownership grammar and ink impression; precise geometry (landing point, sizes, fork
positions, gap/rim px, exact colour difference) is the engine-still stage's job and is not deducted here.

  python Tools/Art/SpellVFX308/concept_vfx120_rejudge150.py s2a_1a_1:3 s2a_1a_2:1 ...   # <id>:<round>

Writes Art/SpellVFX308/Concepts/vfx120_s2/<id>_r<N>.judge150.md and prints one summary line per picture."""
import subprocess, sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
import concept_vfx120_s2a as a
import concept_vfx120_s2a_single  # noqa: F401  (registers the single-panel ids and the 1x1 judge prompt)

SCOPE = ("[판정 범위 D308-150 V1 — 2026-10-25 사용자 결정] 컨셉 시안은 형태 문법(어떤 모양인지: 서 있는 가지 / 누운 열린 다발 / 세로점 / 음각 입술 / 작은 폐곡선 등) · "
         "소유 문법(내 것 = 마른 테 · 속 빈 획 다발, 적 = 테 없는 젖은 채운 면) · 먹 인상(무광 · 발광 0 · 몸 첫 단서) · 혼동 쌍 구별 · 적 가림만 본다. "
         "정밀 기하(착점의 정확한 위치 · 미터 치수 · 분기 위치 · 공극/테 px · 정확한 색차 · 거리)는 엔진 정지 렌더 몫이므로 감점하지 않는다. "
         "단, 모양 자체가 다른 동사로 읽히거나 내 것이 채운 면이면 감점·즉시 실패는 그대로다.")


def main():
    procs = []
    for arg in sys.argv[1:]:
        pid, rnd = arg.split(':')
        rnd = int(rnd)
        img = a.OUT / f'{pid}_r{rnd}.png'
        if not img.exists():
            print('NOIMG', pid, rnd)
            continue
        text = a.judge_prompt(pid, max(rnd, 2))   # round>=2 adds the item-4 note
        head, rest = text.split('\n\n', 1)
        pf = a.OUT / f'{pid}_r{rnd}.judge150_prompt.txt'
        pf.write_text('\n\n'.join([head, SCOPE, rest]), encoding='utf-8')
        cmd = ['codex', 'exec', '--skip-git-repo-check', '-s', 'read-only', '-C', str(a.REPO), '-i', str(img), '-']
        out = open(a.OUT / f'{pid}_r{rnd}.judge150_raw.txt', 'w', encoding='utf-8')
        procs.append((pid, rnd, subprocess.Popen(cmd, stdin=open(pf, 'rb'), stdout=out, stderr=subprocess.STDOUT, shell=True)))
        if len(procs) % 4 == 0:   # at most 4 judges at once (another session also runs codex)
            for _, _, p in procs[-4:]:
                p.wait(timeout=1500)
    for pid, rnd, p in procs:
        p.wait(timeout=1500)
        raw = (a.OUT / f'{pid}_r{rnd}.judge150_raw.txt').read_text(encoding='utf-8', errors='replace')
        ans = raw.split('\ncodex\n')[-1]
        (a.OUT / f'{pid}_r{rnd}.judge150.md').write_text(ans, encoding='utf-8')
        print('JUDGED150', f'{pid}:{rnd}', a.summarize(ans), flush=True)


if __name__ == '__main__':
    main()
