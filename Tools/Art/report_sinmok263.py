"""Build a review from current Unity output, without driving an application UI."""
from pathlib import Path
import hashlib
import html
import json
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild'
DATA = OUT / 'Sinmok263'

def main():
    protected = json.loads((DATA/'protected-before.json').read_text(encoding='utf-8-sig'))
    hashes = {p: hashlib.sha256(Path(p).read_bytes()).hexdigest() == h for p, h in protected.items()}
    reports = {}
    for name in ['checks', 'combat-checks']:
        path = DATA/(name+'.txt')
        lines = path.read_text(encoding='utf-8-sig').splitlines() if path.exists() else []
        reports[name] = dict(passed=sum(x.startswith('PASS ') for x in lines),
                             failed=sum(x.startswith('FAIL ') for x in lines), available=path.exists())
    snapshot = dict(utc=datetime.now(timezone.utc).isoformat(), protected=hashes, reports=reports,
                    unverified=['Actual input exploration and combat difficulty', 'Animation/hitbox readability in live play',
                                'CPU/GPU frame timing', 'User art approval'])
    (DATA/'delivery-audit.json').write_text(json.dumps(snapshot,ensure_ascii=False,indent=2),encoding='utf-8')
    captions = ['신목 · 대기 자세', '가지 내려치기 · 준비 자세', '가지 휩쓸기 · 타격 자세',
                '뿌리 굴 · 입구', '뿌리 굴 · 내부', '고지대 안개 · 0초', '고지대 안개 · 90초']
    figures = ''.join(f'<figure><a href="Sinmok263/view-{i}.png"><img loading="lazy" src="Sinmok263/view-{i}.png"></a><figcaption>{c}</figcaption></figure>' for i,c in enumerate(captions))
    rows=''.join(f'<tr><td>{html.escape(name)}</td><td>{r["passed"]} PASS / {r["failed"]} FAIL</td><td><a href="Sinmok263/{name}.txt">상세</a></td></tr>' for name,r in reports.items())
    page='''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>청림 — 뿌리 굴과 신목 #263</title>
<style>body{margin:0;background:#eae7dd;color:#292e2b;font:16px/1.7 system-ui,sans-serif}main{max-width:1240px;margin:70px auto;padding:0 28px}h1{font-weight:500;font-size:36px}h2{margin-top:60px;font-weight:500}p{max-width:940px}small,figcaption{color:#575e58}figure{margin:28px 0}img{width:100%;display:block}figcaption{padding:8px 0}a{color:#374f4a}table{border-collapse:collapse;width:100%}td,th{text-align:left;padding:12px 8px;border-bottom:1px solid #b6bbb0}.note{border-left:3px solid #748378;padding-left:18px}.pair{display:grid;grid-template-columns:1fr 1fr;gap:20px}@media(max-width:700px){.pair{display:block}}code{overflow-wrap:anywhere}</style><main>
<small>후보 구현 #263 · 2026-09-25</small><h1>청림 · 뿌리 굴과 신목</h1>
<p>신목은 청룡 전에 접근할 수 있는 선택 보스다. 신목을 지나쳐도 청룡과 본선 진행은 열려 있다. 뿌리 굴의 두 입구는 벌목장 지선과 심부 복귀로를 잇고, 물증·갱도 소리·작업 흔적을 배치했다.</p>
<p>신목은 몸통·뿌리·가지·수관을 Unity 절차 모델링으로 제작하고 45개 뼈와 스킨 웨이트로 조립했다. 대기와 네 공격 클립을 실제 전투 시계에 연결했다. HP 520, 처치 260통보, 동굴 물증 60통보는 TEST 수치다.</p>
<p class="note">이미지는 실제 후보의 Camera.Render 1920×1080 결과다. 공격 장면은 클립의 특정 시간을 샘플한 자세이며, 실시간 전투 영상이나 사용자 미술 승인이 아니다. 수동 플레이·난이도·CPU/GPU 성능은 별도로 확인해야 한다.</p>
<h2>탐험 연결</h2><table><tr><th>구간</th><th>현재 NavMesh 경로</th><th>역할</th></tr><tr><td>벌목장 → 뿌리 굴</td><td>약 261m</td><td>본선에서 선택해 들어오는 지선</td></tr><tr><td>뿌리 굴 내부</td><td>약 99m</td><td>굽이·공동·물증·두 출입구</td></tr><tr><td>뿌리 굴 → 심부</td><td>약 369m</td><td>조사 후 본선으로 복귀</td></tr><tr><td>심부 → 신목</td><td>약 489m</td><td>선택 보스와 전방 휴식 지점</td></tr></table>
<p>위 거리는 자동 길찾기 결과이며 실제 보행 시간은 아니다. 신목 처치로 별도 영구 사실과 일회성 보상을 저장한다. 청룡의 종성·국·仁 해금은 그대로 유지한다.</p>
<h2>현장 화면</h2>'''+figures+'''
<h2>강토와 지도</h2><p>작은 장소는 ‘청림 · 뿌리 굴’처럼 강토와 함께 표시한다. 큰 강토만 진입하면 강토 이름만 나온다. 지도와 도착명은 같은 카탈로그를 사용한다. 미발견 장소의 공개 조건과 걸어간 갱도를 중심으로 드러나는 동굴 지도는 유지한다.</p>
<figure><img src="Sinmok263/map-preview.png"><figcaption>지도 원본 텍스처에 8.5% 오방색 바탕. 실제 UI의 이름·발견 마커는 별도 레이어다.</figcaption></figure>
<p>청림 청색 · 적로 적색 · 황경 황색 · 철옹 백색 · 현강 흑색. 하늘에는 최대 5.5% 색을 혼합하고 경계에서 부드럽게 전환한다. 지면과 프롭의 강토별 색은 후속이다. 구름과 높이 145~300m의 이동·생성·소산 안개는 별도 표현이며, 산안개는 렌더링 깊이 앞의 공간에만 누적한다.</p>
<h2>현재 검사</h2><table>'''+rows+f'''<tr><td>원본 씬·사용자 저장/백업</td><td>{sum(hashes.values())}/{len(hashes)} 해시 일치</td><td><a href="Sinmok263/delivery-audit.json">증거</a></td></tr></table>
<p><a href="migration_navigation.txt">후보 NavMesh·접지 검사</a> · <a href="Sinmok263/arrival-regression.txt">도착/영구 발견 회귀24개</a> · <a href="consistency.txt">정적 정합성 검사</a> · <a href="Sinmok263/recovery-notes.md">중단·수정한 검사 이력</a></p>
<p>별도 PreviewScene에서 실제 신목의 프로필·소켓·PlayerVitals·PlayableGraph를 검사한다. 저장 실패/재시도와 중복 보상도 분리 검사한다. 정적 검사·fixture·수동 플레이·미술 판단은 서로 대체하지 않는다.</p>
<h2>테스트 씬</h2><p><code>Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity</code></p>
<p>기존 <code>W_Demo_Compact</code> 원본은 보존했다. 이번 작업은 Computer Use·Play 진입·실제 저장 변경 없이 후보에 적용했다.</p></main></html>'''
    (OUT/'SINMOK_REVIEW.html').write_text(page,encoding='utf-8')
    print(json.dumps(snapshot,ensure_ascii=False,indent=2))

if __name__=='__main__':
    main()
