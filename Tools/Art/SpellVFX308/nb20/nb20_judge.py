#!/usr/bin/env python3
"""NB20 (D308-150 V6) Codex judge of the engine stills + a motion strip (copied from vol2vfx9a_judge.py).
  python Tools/Art/SpellVFX308/nb20/nb20_judge.py P0 [A0 F0 R0 | all] --round r2 [--tag r2a]
Runs `codex exec` through subprocess with the prompt read as BYTES from a file (a PowerShell pipe to a native exe turns Korean into '?').
Images: sheets/<T>_red_a008_p15, _red_b018_p15, _red_b030_p15, _green_b018_p15, _red_b018_p15_grey (5 bodies each, 960 x 540 native crops) + strips/<T>_strip.png
(8 consecutive moments of one cast, the motion). Output: Art/SpellVFX308/Vol2Vfx/nb20/<round>/judge/<tag>/<T>.out.txt + summary.txt."""
import re
import shutil
import subprocess
import sys
from pathlib import Path

REPO = Path(__file__).resolve().parents[4]
VERB = {'P0': 'ㅏ 한 번 (경로 투사체)', 'A0': 'ㅓ 한 번 (맨 패링: 붓대·소매의 짧은 먹 맥, 성공 패링 때만 튐)', 'F0': 'ㅗ 한 번 (순간 부채 다발)', 'R0': 'ㅜ 한 번 (속 빈 띠의 순간 획)'}
ROWS = {'P0': '가나마사아', 'A0': '거너머서어', 'F0': '고노모소오', 'R0': '구누무수우'}
NOTE = {
    'P0': '붓끝 점화(얼룩+바늘 선) → 종이색 마른 테를 두른 속 빈 먹 머리가 36 m/s로 날아가고(뒤에 1.5 m 갈필 꼬리) → 첫 몸 접촉 프레임에 세운 주획 + 보조 획 2(+33/+67 ms) + 바닥으로 던진 획 3 + 먹 방울 3–5 → 접촉 0.12 s 뒤부터 0.15 s 동안 균열·조각으로 마름(잔존 없음). 표적 곰은 4.6 m.',
    'A0': '패링 창 0.5 s 동안 붓대 끝과 소매(손)에 짧은 먹 맥 → 창이 닫히면 제자리 마름. 0.22 s에 LAB-SIMULATED 성공 패링(적 공격을 받아낸 것으로 모델에 알림) → 받은 지점에 큰 바늘 선 별 + 접촉 획 + 방울. 표적 곰은 1.6 m.',
    'F0': '바닥에 순간 부채 다발: 양쪽 방사선(0 ms) → 먼 호(+33 ms) → 안쪽 획 2(+67/+100 ms), 채운 검은 면 없음. 부채 안의 곰 2마리에 접촉 획. 0.12 s 뒤부터 0.15 s 동안 마름 → 0.30 s 판은 거의 다 마른 것이 정상(잔존 없음).',
    'R0': '시전자 둘레 반경 2 m 띠(폭 0.6 m, 가운데 비움)에 60° 획 6개(앞 → 옆 → 뒤 순), 띠 앞 ±20°에 선 획 한 쌍(1.1 m). 띠 안의 곰에 접촉 획. 0.12 s 뒤부터 0.15 s 동안 마름 → 0.30 s 판은 거의 다 마른 것이 정상.',
}

PROMPT = """너는 오행부(五行符, 수묵 소울라이크)의 VFX 판정자다. 첨부는 엔진(Unity URP)에서 실제로 렌더한 vol.2 무받침 술식 VFX다. 템플릿 = {T} ({verb}), 글자 {letters} = 목·화·토·금·수 순서.
이번 판의 먹은 Blender(붓털 리본 메시 · 메타볼 젖은 고임/방울 · 바늘 선)와 After Effects(가장자리 번짐 · 셀 패턴 균열 마름 순서)로 저작해 아틀라스로 구운 것을 엔진이 순서 맵으로 공개·건조한다.
연출 설명: {note}
판마다 몸 5종 한 장씩(칸 하나 = 1920x1080 화면의 960x540 원본 픽셀 잘라냄, 어깨 카메라 피치 15°), 판 이름의 red = 적로 흙 · green = 청림 흙(실제 월드 재질), a008/b018/b030 = 시전 후 0.08/0.18/0.30 s, grey = 같은 판의 흑백. strip = 한 시전의 연속 8순간(움직임 판정용). 적은 실제 적 모델(곰)이다.
먼저 저장소의 `Docs/Research/vfx120/plan_v10_final.md`에서 B절 24템플릿 표의 {T} 행, C절(몸 색 · 두 명도층 · 몸별 큰 가장자리 단서), E절(소유: 내 것 = 마른 테 · 속 빈 획 다발, 적 = 테 없는 젖은 면), G절 픽셀 최소치(마른 테 ≥3 px · 공극 ≥6 px · 몸 형태 짧은 변 ≥12 px)와 `Docs/Research/VFX-REF-INK-BURST-308.md`(사용자가 좋아한 참고: 젖은 머리 + 갈필 꼬리 + 바늘 선 + 방울, 획이 하나씩 나타남)를 읽어라. 파일은 만들지 말고 한국어로 답한다.
그림에서 직접 보이는 것만 인정하고 비판적으로 본다. 특히: 수묵 붓 획(먹 · 갈필 · 번짐)으로 읽히는가, 색 테이프 · 사다리 · 스티커 · 디지털 노이즈로 읽히지 않는가, 틀(경로/부착/부채/띠)이 첫 0.08 s에 읽히는가, 몸 5종이 흑백에서도 끝 모양(목 = 찢긴 섬유 · 화 = 톱니 + 탄 구멍 · 토 = 계단 판 · 금 = 곧은 절단 + 가로 끊김 · 수 = 열린 갈고리 + 물살)으로 구별되는가, 적이 70% 이상 보이는가, 발광 · 가산 · 블룸이 없는가, 마름이 균일 페이드가 아닌가.
채점(100): ① 틀 · 동사 형태 판독 30 ② 몸 5종 구별(색 + 끝 단서) 25 ③ 먹 룩(수묵 · 갈필 · 무광 · 발광 0) 20 ④ 첫 .08 s 단서 · .18/.30 s 정착 · 움직임 15 ⑤ 화면 크기 · 가림 · 구성 10. 통과 97.
답 형식: 칸 관찰(몸별 한 줄) / 점수: ① NN/30 | ② NN/25 | ③ NN/20 | ④ NN/15 | ⑤ NN/10 | 합계 NNN/100 / 가장 큰 문제 3개 / 고칠 것(구현자가 바로 할 수 있는 구체 지시, 최대 6개, 수치 포함).
"""


def judge(root, t, tag, images):
    out_dir = root / 'judge' / tag
    out_dir.mkdir(parents=True, exist_ok=True)
    pf = out_dir / (t + '.prompt.txt')
    pf.write_bytes(PROMPT.format(T=t, verb=VERB[t], letters=ROWS[t], note=NOTE[t]).encode('utf-8'))
    cmd = [shutil.which('codex.cmd') or shutil.which('codex') or 'codex.cmd', 'exec', '--skip-git-repo-check', '-s', 'read-only', '-C', str(REPO)]
    for im in images:
        cmd += ['-i', str(im)]
    cmd.append('-')
    with open(pf, 'rb') as fh:
        r = subprocess.run(cmd, stdin=fh, stdout=subprocess.PIPE, stderr=subprocess.PIPE, timeout=1800)
    text = r.stdout.decode('utf-8', errors='replace')
    (out_dir / (t + '.out.txt')).write_bytes(r.stdout)
    (out_dir / (t + '.err.txt')).write_bytes(r.stderr)
    m = re.search(r'합계\s*\**\s*(\d+)\s*/\s*100', text)
    return (int(m.group(1)) if m else None), text


def main():
    args = list(sys.argv[1:])
    rnd = 'r2'; tag = None
    if '--round' in args:
        i = args.index('--round'); rnd = args[i + 1]; del args[i:i + 2]
    if '--tag' in args:
        i = args.index('--tag'); tag = args[i + 1]; del args[i:i + 2]
    tag = tag or rnd + 'a'
    root = REPO / 'Art/SpellVFX308/Vol2Vfx/nb20' / rnd
    ts = list(VERB) if args == ['all'] or not args else args
    lines = []
    for t in ts:
        s = root / 'sheets'
        imgs = [s / ('%s_red_a008_p15.png' % t), s / ('%s_red_b018_p15.png' % t), s / ('%s_red_b030_p15.png' % t), s / ('%s_green_b018_p15.png' % t),
                s / ('%s_red_b018_p15_grey.png' % t), root / 'strips' / ('%s_strip.png' % t)]
        imgs = [p for p in imgs if p.exists()]
        score, text = judge(root, t, tag, imgs)
        line = '%s %s: %s' % (tag, t, score)
        lines.append(line)
        print(line, flush=True)
    with open(root / 'judge' / tag / 'summary.txt', 'a', encoding='utf-8') as fh:
        fh.write('\n'.join(lines) + '\n')


if __name__ == '__main__':
    main()
