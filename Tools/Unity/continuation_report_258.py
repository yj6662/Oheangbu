"""Publish only recorded evidence for continuation 258; never infer a gameplay pass."""
from pathlib import Path
import hashlib
import html
import json
from datetime import datetime, timezone

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/World/Compact/Rebuild/Continuation258'


def run():
    records = []
    for name in ['journey-mine', 'journey-village', 'journey-sanctuary']:
        path = OUT / (name + '.json')
        if path.exists():
            data = json.loads(path.read_text(encoding='utf-8-sig'))
            records.append({k: data.get(k) for k in ['leg', 'status', 'seconds', 'metres', 'scope']})
    checks = []
    names = ['village-runtime', 'frontage-services', 'inn-rest', 'progression-after-road',
             'buff-contracts', 'ward-contracts', 'parry-hold', 'giyeok-contracts', 'giyeok-live',
             'southgate-audit', 'southgate-runtime', 'gate-vehicle', 'boss-save-after-giyeok', 'giyeok-followup260']
    for name in names:
        path = OUT / (name + '.txt')
        if path.exists():
            lines = path.read_text(encoding='utf-8-sig').splitlines()
            checks.append({'file': path.name, 'pass_lines': sum(s.startswith('PASS ') for s in lines),
                           'fail_lines': sum(s.startswith('FAIL ') for s in lines),
                           'sha256': hashlib.sha256(path.read_bytes()).hexdigest()})
    report = {'utc': datetime.now(timezone.utc).isoformat(), 'status': 'IN_PROGRESS',
              'scope': 'Recorded automatic input and fixture evidence only. Pass lines are not a completion percentage or unique test count.',
              'journeys': records, 'checks': checks,
              'manual_combat': 'NOT_VERIFIED', 'human_wayfinding': 'NOT_VERIFIED',
              'art_approval': 'NOT_APPROVED', 'standalone_cpu_gpu': 'NOT_MEASURED'}
    (OUT / 'checkpoint.json').write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
    journey_rows = ''.join(f'<tr><td>{html.escape(str(r["leg"]))}</td><td>{r["metres"]:.2f}m</td><td>{r["seconds"]:.2f}초</td><td>{html.escape(r["status"])}</td></tr>' for r in records)
    check_rows = ''.join(f'<tr><td><a href="Continuation258/{r["file"]}">{r["file"]}</a></td><td>{r["pass_lines"]}</td><td>{r["fail_lines"]}</td></tr>' for r in checks)
    figures = ''.join(f'<figure><a href="Continuation258/{kind}-{element}.png"><img loading="lazy" src="Continuation258/{kind}-{element}.png"></a><figcaption>{label} · {ename}</figcaption></figure>'
                      for kind, label in [('buff', '강화'), ('ward', '고정 방벽')]
                      for element, ename in [('wood', '목'), ('fire', '화'), ('earth', '토'), ('metal', '금'), ('water', '수')]
                      if (OUT / f'{kind}-{element}.png').exists())
    document = f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>청림 공통 시스템 · #258 진행 기록</title>
<style>body{{background:#eeeade;color:#262921;font:17px/1.7 system-ui,sans-serif;margin:40px auto;max-width:1180px;padding:0 24px}}h1{{font-size:32px}}h2{{margin-top:48px;font-size:23px}}a{{color:#315a58}}table{{border-collapse:collapse;width:100%;font-size:15px}}td,th{{border-bottom:1px solid #b8b3a6;padding:10px;text-align:left}}.gallery{{display:grid;grid-template-columns:1fr 1fr;gap:20px}}figure{{margin:0}}img{{width:100%}}figcaption{{font-size:14px}}@media(max-width:700px){{.gallery{{grid-template-columns:1fr}}}}</style>
<h1>청림 공통 시스템 · #258</h1><p>전체 작업 진행 중. 원본 축소맵을 보존하고 최신 후보에서 기능을 연결한다. 왕소 #257 외형은 유지한다.</p>
<p>테스트 씬: <code>slice-5e82ecd76d2a/W_Demo_Compact_MigrationCheck.unity</code><br>자동 입력·API fixture·수동 플레이·미술 판정·성능을 따로 기록한다.</p>
<h2>보행 연결</h2><table><tr><th>구간</th><th>거리</th><th>시간</th><th>결과</th></tr>{journey_rows}</table>
<p>NavMesh를 아는 자동 조향이 실제 PlayerMotor를 조작했다. 동굴→주막→마을은 연속 이동이며, 마을→성역은 별도 저장의 마을 휴식 fixture에서 시작했다. 성역 첫 시도의 적 중심 접근 정체는 실패 기록을 보존했다. 수동 길찾기나 전투 통과를 뜻하지 않는다.</p>
<h2>검사 기록</h2><table><tr><th>원본 증거</th><th>PASS 행</th><th>FAIL 행</th></tr>{check_rows}</table>
<p>행 수는 고유 테스트 수나 완성도가 아니다. 일부 기록은 반복 호출과 환경 준비 확인을 포함한다. 장비/거래·보스 검사에는 시험 재화, 위치 준비, API 피해가 사용됐다.</p>
<h2>후보에 연결한 표현</h2><div class="gallery">{figures}</div>
<p>강화·방벽 각 5종의 게임뷰 기록이다. 화·수 방벽은 내부에서 경계가 너무 옅어 시각 개선이 남아 있다. 미술 승인은 받지 않았다.</p>
<h2>남문 검증</h2><p>문루 자세·충돌·NavMesh 16개 항목을 새로 검사했다. 보행의 닫힘/열림, 처치 저장 실패/재시도, 중복 보상 차단, 재실행과 사망 복구를 확인했다. 쉼터 검사 첫 실패는 반경 끝에 서던 검사 도구를 반경 안쪽으로 수정한 뒤 통과했으며 실패 이력도 보존한다. 실제 바퀴 물리 기록은 <a href="Continuation258/gate-drive-open.json">개문 통과</a>와 <a href="Continuation258/gate-drive-closed.json">닫힘 차단</a>을 따로 확인한다.</p>
<h2>백그라운드 후속 #259·260</h2><p><a href="BRANCH_REVIEW.html">약초꾼 우회로</a>에 물증·늦은 증언·지도 길을 연결하고 Edit 참조12/진행·파일 저장9개와 왕복 캡슐 충돌을 검사했다. 삭의 직선 관통과 악의 같은 대상 재타격은 PreviewScene35개 검사 뒤 후보 프로필에 연결했다. 창 활성화·커서·입력 장치를 쓰지 않았다. 중앙 해석·먹·이벤트 계약은 검사했고, 실제 손 작도·현장 전투와 미술 검증은 남아 있다.</p>
<h2>남은 작업</h2><p>ㄱ 공격6종(막·곡·녹·목·속·옥), 뿌리 굴·신목, 다른 종성·강화·필드 능력, 강토/서사/출시 연결을 이어간다. 정적100자 원장의 해석 경로 결손은65자이며 경로 존재를 전투 완료로 세지 않는다. 길의 큰 검은 삼각면은 뒤집힌 면 수정 후 사라졌지만 기존 마을의 긴 어두운 두 띠는 남아 있다. 새 약초길의 검은 띠는 전용 혼합 재질로 교체했다. 독립 실행 CPU/GPU와 실제 수동 전투는 미검증이다.</p>
<p><a href="../../../../Docs/Plans/PLAN-CONTINUATION-258.md">진행 원장</a> · <a href="Continuation258/checkpoint.json">기계 판독 기록</a></p></html>'''
    (OUT.parent / 'CONTINUATION_REVIEW.html').write_text(document, encoding='utf-8')
    print(json.dumps({'journeys': len(records), 'evidence_files': len(checks), 'status': 'IN_PROGRESS'}))


if __name__ == '__main__':
    run()
