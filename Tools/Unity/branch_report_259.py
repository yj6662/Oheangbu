"""Report source-bound edit-mode evidence; never turn an automated probe into a play pass."""
from pathlib import Path
import hashlib,html,json
from datetime import datetime,timezone
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Branches259'
def run():
    walks=[json.loads((OUT/f'collision-{mode}.json').read_text(encoding='utf-8-sig')) for mode in ['walk','reverse']]
    counts={}
    for name in ['audit','facts-edit']:
        lines=(OUT/f'{name}.txt').read_text(encoding='utf-8-sig').splitlines()
        counts[name]={status:sum(line.startswith(status+' ') for line in lines) for status in ['PASS','FAIL']}
    assets=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a'
    sources=[assets/'W_Demo_Compact_MigrationCheck.unity',assets/'Branches259/Placements.asset',assets/'Branches259/Footpath.asset',ROOT/'Art/World/Compact/Rebuild/layout.json']
    receipt={'utc':datetime.now(timezone.utc).isoformat(),'status':'IMPLEMENTED_WITH_VALIDATION_REMAINING',
             'scope':'Edit-mode actual collision capsule and production campaign/atomic file contracts. No computer use or Play entry.',
             'checks':counts,'walks':[{k:w[k] for k in ['status','distance','simulatedSeconds','scope']} for w in walks],
             'live_session_rest_death':'NOT_RETESTED','real_input':'NOT_TESTED','human_wayfinding':'NOT_VERIFIED','art':'NOT_APPROVED',
             'source_hashes':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}}
    (OUT/'delivery.json').write_text(json.dumps(receipt,ensure_ascii=False,indent=2),encoding='utf-8')
    rows=''.join(f'<tr><td>{direction}</td><td>{w["distance"]:.2f}m</td><td>{w["status"]}</td></tr>' for direction,w in zip(['본선→약초길→심부','심부→약초길→본선'],walks))
    figures=''.join(f'<figure><img src="Branches259/view-{i}.png"><figcaption>{label}</figcaption></figure>' for i,label in enumerate(['약초길 분기','남겨진 바구니','심부 합류']))
    page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>약초꾼 우회로 · #259</title>
<style>body{{max-width:1180px;margin:40px auto;padding:0 24px;background:#eeeade;color:#262921;font:17px/1.7 system-ui}}h1{{font-size:30px}}h2{{font-size:23px;margin-top:40px}}img{{width:100%}}figure{{margin:26px 0}}td,th{{padding:8px 20px;border-bottom:1px solid #b8b3a6;text-align:left}}a{{color:#315a58}}</style>
<h1>약초꾼 우회로 · #259</h1><p>벌목장 전투를 피해서 물든 심부로 돌아오는 길과 바구니 물증을 후보 씬에 연결했다. 먼저 물증을 살펴도 주막 약초꾼의 후속 대사에 반영된다. 별도 보상이나 필수 진행 조건은 추가하지 않았다.</p>
<p>지도 길·배치 원장·식생 제외 영역은 같은 경로를 따른다. 원본 축소맵과 일반 저장 슬롯은 유지한다.</p>
<h2>새 검사</h2><p>씬 참조 {counts['audit']['PASS']} PASS, 진행·원자 파일 저장 {counts['facts-edit']['PASS']} PASS. 임시 파일 잠금 실패에서 이전 발견 상태가 보존되고 재시도/재로드 시 사실이 한 번만 남는 것을 확인했다.</p>
<table><tr><th>방향</th><th>충돌 해결 이동 거리</th><th>결과</th></tr>{rows}</table>
<p>플레이어와 같은 크기의 Capsule을 Editor에서 이동한 검사다. 실제 키 입력·수동 길찾기·휴식/사망의 라이브 재검사·CPU/GPU 실측은 아니다. 사용자의 다른 작업을 방해하지 않도록 창 활성화와 커서 조작 없이 수행했다.</p>
<h2>화면 확인</h2>{figures}<p>동굴 바닥 재질을 공유해 생겼던 검은 길 띠를 전용 가장자리 혼합 재질로 교체했다. 현재 주변 숲 밀도와 발견 지점의 공간 구성은 후속 미술 작업이 남아 있다. 최종 사용자 미술 승인은 미완료다.</p>
<p><a href="Branches259/delivery.json">원시 결과</a> · <a href="CONTINUATION_REVIEW.html">공통 시스템 #258</a></p></html>'''
    (OUT.parent/'BRANCH_REVIEW.html').write_text(page,encoding='utf-8')
    print(json.dumps({'checks':counts,'status':receipt['status']},ensure_ascii=False))
if __name__=='__main__':run()
