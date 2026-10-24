#!/usr/bin/env python3
"""Codex judge of the vol.2 VFX still sheets (S5-b slice 9a round 2).  python Tools/Art/SpellVFX308/vol2vfx9a_judge.py P0 [P1 ...|all] [--tag r2a]
Runs `codex exec` through subprocess with the prompt read as BYTES from a file (a PowerShell pipe to a native exe is ASCII and turns Korean into '?').
Output: Art/SpellVFX308/Vol2Vfx/9a_r2/judge/<tag>/<template>.prompt.txt and .out.txt (UTF-8) + a score line in summary.txt."""
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[3]
ROOT = REPO / 'Art/SpellVFX308/Vol2Vfx/9a_r2'
VERB = {'P0': '한 번', 'P1': '남긴다', 'P2': '번진다', 'P3': '심는다', 'P4': '가른다', 'P5': '돌아온다'}
ROWS = {'P0': '가나마사아', 'P1': '각낙막삭악', 'P2': '간난만산안', 'P3': '감남맘삼암', 'P4': '갓낫맛삿앗', 'P5': '강낭망상앙'}

PROMPT = """너는 오행부(五行符, 수묵 소울라이크)의 VFX 판정자다. 첨부는 엔진에서 실제로 렌더한 vol.2 술식 VFX 정지 판들이다. 템플릿 = {T} ({verb}, 글자 {letters} = 목·화·토·금·수 순서).
판마다 몸 5종 한 장씩(칸 하나 = 1920x1080 화면의 1280x720 원본 픽셀 잘라냄), 판 이름의 red = 적로 흙 · green = 청림 흙 재질(실제 월드 재질), a008 = 시전 후 0.08 s · b030 = 0.30 s, grey = 같은 판의 흑백(색 없이 몸이 읽히는지 본다). 적은 실제 적 모델(산적)이다.
먼저 저장소의 `Docs/Research/vfx120/plan_v10_final.md`에서 B절 24템플릿 표의 {T} 행, C절(몸 색 · 두 명도층 · 끝 거동 · 몸별 큰 가장자리 단서), E절(소유: 내 것 = 마른 테 · 속 빈 획 다발, 적 = 테 없는 젖은 면), J절(엔진 정지 판정 기준)과 G절 픽셀 최소치(마른 테 ≥3 px · 속 빈 공극 ≥6 px · 몸 형태 경계상자 짧은 변 ≥12 px)를 읽어라. 파일은 만들지 말고 한국어로 답한다.
그림에서 직접 보이는 것만 인정하고 비판적으로 본다. 특히: 획이 하나의 마른 붓 리본(털 가닥 사이 갈라짐)으로 읽히는가, 사다리 · 널빤지처럼 서 있지 않은가, 몸 5종이 흑백에서도 끝 모양으로 구별되는가(목 = 길게 찢긴 섬유 · 화 = 타서 뚫린 구멍 + 톱니 끝 · 토 = 계단식 낮은 판 · 금 = 곧은 절단 끝 + 가로 끊김 · 수 = 열린 갈고리 끝 + 속 빈 평행 물살), 적이 70% 이상 보이는가, 발광 · 가산 · 블룸이 없는가.
채점(100): ① 틀 · 동사 형태 판독 30 ② 몸 5종 구별(색 + 끝 단서) 25 ③ 먹 룩(수묵 · 갈필 · 무광 · 발광 0) 20 ④ 첫 .08 s 단서 · .30 s 정착 15 ⑤ 화면 크기 · 가림 · 구성 10. 통과 97.
답 형식: 칸 관찰(몸별 한 줄) / 점수: ① NN/30 | ② NN/25 | ③ NN/20 | ④ NN/15 | ⑤ NN/10 | 합계 NNN/100 / 가장 큰 문제 3개 / 고칠 것(구현자가 바로 할 수 있는 구체 지시, 최대 6개, 수치 포함).
"""


def judge(t, tag, images):
    out_dir = ROOT / 'judge' / tag
    out_dir.mkdir(parents=True, exist_ok=True)
    pf = out_dir / (t + '.prompt.txt')
    pf.write_bytes(PROMPT.format(T=t, verb=VERB[t], letters=ROWS[t]).encode('utf-8'))
    cmd = [shutil.which('codex.cmd') or shutil.which('codex') or 'codex.cmd', 'exec', '--skip-git-repo-check', '-s', 'read-only', '-C', str(REPO)]
    for im in images:
        cmd += ['-i', str(im)]
    cmd.append('-')
    with open(pf, 'rb') as fh:
        r = subprocess.run(cmd, stdin=fh, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=1500)
    text = r.stdout.decode('utf-8', errors='replace')
    (out_dir / (t + '.out.txt')).write_bytes(r.stdout)
    (out_dir / (t + '.err.txt')).write_bytes(r.stderr)
    m = re.search(r'합계\s*\**\s*(\d+)\s*/\s*100', text)
    return (int(m.group(1)) if m else None), text


def main():
    args = [a for a in sys.argv[1:]]
    tag = 'r2a'
    if '--tag' in args:
        i = args.index('--tag'); tag = args[i + 1]; del args[i:i + 2]
    ts = list(VERB) if args == ['all'] or not args else args
    lines = []
    for t in ts:
        sheets = ROOT / 'sheets'
        imgs = [sheets / ('%s_red_b030_p15.png' % t), sheets / ('%s_red_a008_p15.png' % t), sheets / ('%s_green_b030_p15.png' % t), sheets / ('%s_red_b030_p15_grey.png' % t)]
        imgs = [p for p in imgs if p.exists()]
        score, text = judge(t, tag, imgs)
        line = '%s %s: %s' % (tag, t, score)
        lines.append(line)
        print(line, flush=True)
    with open(ROOT / 'judge' / tag / 'summary.txt', 'a', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
