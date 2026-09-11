"""Final evidence index, scoped to the twenty assigned fire VFX."""
import json,hashlib,urllib.request
from pathlib import Path
o=Path('Art/SpellVFX120');d=json.loads((o/'NIEUN_PROGRESS.json').read_text(encoding='utf-8'))
rows=[r for r in d['rows'] if r['assigned']]
assert len(rows)==20 and all(r['status']=='TECHNICAL_PASS_USER_REVIEW_PENDING' for r in rows)
a=json.loads((o/'nieun_final_play_audit.json').read_text(encoding='utf-8'))
assert a['status']=='PASS_REAL_UPDATE_LIFETIME_ONLY' and a['completed']==20 and a['failed']==0 and a['errors']==0 and a['restored'] and a['sourceUnchanged'] and a['sceneUnchanged'],a['status']
assert set(r['glyph'] for r in a['rows'])==set(r['glyph'] for r in rows)
assert all(r['rootGone'] and r['childrenGone'] for r in a['rows'])
b=json.loads((o/'na45_before_profiles.json').read_text());p=Path('Oheangbu/Assets/_Project/Art/SpellVFX120/Profiles')
changed=sorted(x.name for x in p.glob('*.asset') if hashlib.sha256(x.read_bytes()).hexdigest()!=b[x.name])
assert changed==sorted(r['id']+'.asset' for r in rows)
evidence=[]
for r in rows:
    old=json.loads((o/r['audit']).read_text(encoding='utf-8'))
    assert old['status']=='PASS_REAL_UPDATE_LIFETIME_ONLY'
    for k in ['IntegratedStills','ExternalStills','IntegratedFrames','ExternalFrames']:
        capture=json.loads((o/f'KTP{k}{r["version"]}'/(r['id']+'_capture.json')).read_text(encoding='utf-8'))
        assert capture['loadedRuntimeAssemblyMvid']==old['appMvid']
    for k in ['Integrated','External']:
        folder=o/f'ClipsKTP{k}{r["version"]}'
        encoding=json.loads((folder/(r['id']+'_encoding.json')).read_text(encoding='utf-8'))
        assert encoding['status']=='VERIFIED_ENCODING_AND_TIMING_ONLY' and (folder/(r['id']+'.mp4')).stat().st_size>0
    with urllib.request.urlopen('http://127.0.0.1:8771/'+r['review']) as response: assert response.status==200
    evidence.append(dict(glyph=r['glyph'],review=r['review'],version=r['version'],captureMvid=old['appMvid']))
result=dict(status='PASS_TECHNICAL_DELIVERY_ONLY',completed=20,reviewPages=20,encodedClips=40,unchangedProfiles=100,changedProfiles=changed,latestRuntimeMvid=a['appMvid'],latestSequentialAudit='nieun_final_play_audit.json',visual='USER_REVIEW_PENDING',gameplay='NOT_FULLY_INTEGRATED',evidence=evidence)
(o/'nieun_final_delivery.json').write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
lines=['# ㄴ 계열 VFX 제작 결과','', '배정된 20종의 VFX 제작·개별 촬영·기술 검사를 마쳤다. 비주얼 판정은 사용자 검토 대기다. 눅·눔·눗·눙은 의도적 공백으로 유지했다.','', '[검토 페이지](http://127.0.0.1:8771/NIEUN_REVIEW.html)','', '## 구현','', '- 화염·불티·연기를 Particle System으로 분리했다. KTP 문양은 발동·영역·접촉 피드백에 사용한다.', '- 놈의 소환수는 기존 Meshy 7 모델을 재사용했다. 신규 유료 생성은 없다. 정적 모델이며 AI·공격 애니메이션은 추가하지 않았다.', '- 마지막 농은 화염 뒤 수증기, 누는 패링 없는 잔존 화벽, 눈은 명시적 경로를 따르는 비전투 연소 표현이다.','', '## 기술 확인','', '- 최신 공통 런타임에서 20종을 하나씩 실제 C2 Update/Destroy로 재생했다. 20종 통과, 오류 0. 씬·참조 자산·카메라 복귀 및 효과 계층 제거를 확인했다.', '- 변경 프로필 20개가 배정 목록과 정확히 일치한다. 나머지 100개는 초기 해시와 동일하다. 공유 코드 전체의 게임 규칙 회귀를 의미하지 않는다.', '- 개별 검토 페이지 20개, 1280×720·24fps 영상 40개. 각 제작 당시 촬영 DLL과 해당 단독 검사 DLL이 일치한다. 이전 영상은 최신 DLL 재촬영본이 아니며 최신 회귀 검사는 별도 파일로 보존했다.','', '## 미완료·미검증','', '- 실제 불처럼 보이는지와 색·밀도·문양 크기는 사용자 검토 대상이다.', '- 검사 신호는 진단용이다. 기존 SpellBook에 없는 술식의 피해·상태·회복·소환 AI·덩굴 제거와 전체 전투 연결은 완료하지 않았다.', '- 실제 필드 상호작용, 다중 동시 시전 GPU 비용, 목표 FPS 달성은 미검증이다. 유체 시뮬레이션이나 열 굴절을 구현한 결과가 아니다.','', '## 개별 결과','', '| 술식 | 제작 버전 | 보고서 |','|---|---:|---|']
lines += [f'| {r["glyph"]} | {r["version"]} | [{r["report"]}]({r["report"]}) |' for r in rows]
lines += ['', '근거: `nieun_final_delivery.json`, `nieun_final_play_audit.json`, `NIEUN_PROGRESS.json`.']
(o/'NIEUN_FINAL_REPORT.md').write_text('\n'.join(lines),encoding='utf-8')
print(json.dumps({k:v for k,v in result.items() if k not in ['evidence','changedProfiles']},ensure_ascii=False))
