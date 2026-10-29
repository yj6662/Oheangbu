#!/usr/bin/env python3
"""작업판 사무실 — Unity 편집기 상태 · 재시작 (2026-10-29, 사용자: "재시작도 가능하게 만들어").

  python Tools/AgentBoard/office_editor.py status     # 전용 메모리 · 여유 RAM · 커밋 · 재시작 필요 여부
  python Tools/AgentBoard/office_editor.py restart    # life.ps1 -Minimized (닫기 → 띄우기 → 제목 대기, 죽이지 않음)

재시작은 AI 일꾼이 하지 않는다 — 일꾼은 Art/AgentBoard/RESTART_EDITOR 요청 파일만 쓰고, 야간 감독이 바퀴 사이에(또는 사무실 단추로) 한다.
기준(2026-10-13 사용자 "제한을 조금 더 높여" 이후): 전용 메모리 30 GB 초과 · 여유 RAM 2.5 GB 미만이면서 커밋 88 % 초과 · 편집기 없음(크래시).
"""
import json, subprocess, sys, time
from pathlib import Path

HERE = Path(__file__).resolve().parent
sys.path.insert(0, str(HERE))
import board  # noqa: E402

LIFE = board.REPO / 'Tools' / 'Unity' / 'Stage308_ops' / 'life.ps1'
REQ = board.BOARD.parent / 'RESTART_EDITOR'
LOG = board.BOARD.parent / 'editor_restarts.log'
PRIVATE_GB, FREE_GB, COMMIT_PCT = 30.0, 2.5, 88.0

_PS = r"""
$u=Get-Process Unity -ErrorAction SilentlyContinue | Sort-Object PrivateMemorySize64 -Descending | Select-Object -First 1
$os=Get-CimInstance Win32_OperatingSystem
$c=Get-CimInstance Win32_PerfFormattedData_PerfOS_Memory
@{running=[bool]$u; private_gb=$(if($u){[math]::Round($u.PrivateMemorySize64/1GB,1)}else{0}); title=$(if($u){$u.MainWindowTitle}else{''});
  free_gb=[math]::Round($os.FreePhysicalMemory/1MB,1); commit_pct=[math]::Round($c.PercentCommittedBytesInUse,0)} | ConvertTo-Json -Compress
"""


def status():
    try:
        out = subprocess.run(['powershell', '-NoProfile', '-Command', _PS], capture_output=True, text=True, encoding='utf-8', errors='replace',
                             timeout=30, creationflags=0x08000000).stdout
        d = json.loads(out.strip().splitlines()[-1])
    except Exception as x:
        return {'running': None, 'error': type(x).__name__}
    d['request'] = REQ.read_text(encoding='utf-8', errors='replace').strip()[:200] if REQ.exists() else ''
    d['reason'] = need_reason(d)
    return d


def need_reason(d):
    if d.get('running') is False:
        return '편집기가 꺼져 있다(크래시?)'
    if d.get('private_gb', 0) > PRIVATE_GB:
        return '전용 메모리 %.1f GB > %.0f' % (d['private_gb'], PRIVATE_GB)
    if d.get('free_gb', 99) < FREE_GB and d.get('commit_pct', 0) > COMMIT_PCT:
        return '여유 RAM %.1f GB · 커밋 %d %%' % (d['free_gb'], d['commit_pct'])
    if d.get('request'):
        return '일꾼 요청: ' + d['request']
    return ''


def restart(reason='', minimized=True):
    """life.ps1로 재시작. (ok, 출력) — 대화상자가 떠서 안 닫히면 죽이지 않고 실패로 돌려준다."""
    args = ['powershell', '-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', str(LIFE)] + (['-Minimized'] if minimized else [])
    t = time.strftime('%Y-%m-%d %H:%M:%S')
    try:
        p = subprocess.run(args, capture_output=True, text=True, encoding='utf-8', errors='replace', timeout=900, creationflags=0x08000000)
        ok, out = p.returncode == 0, (p.stdout + p.stderr).strip()
    except subprocess.TimeoutExpired:
        ok, out = False, 'life.ps1 15분 넘음'
    if ok and REQ.exists():
        REQ.unlink()          # 처리한 요청 파일(우리가 쓰는 신호 파일)만 지운다
    with open(LOG, 'a', encoding='utf-8') as f:
        f.write('%s %s · %s · %s\n' % (t, '재시작 OK' if ok else '재시작 실패', reason, ' | '.join(out.splitlines()[-3:])))
    return ok, out


def main():
    a = sys.argv[1:]
    if a and a[0] == 'restart':
        print(restart('수동', minimized='--show' not in a))
    else:
        print(json.dumps(status(), ensure_ascii=False))


if __name__ == '__main__':
    main()
