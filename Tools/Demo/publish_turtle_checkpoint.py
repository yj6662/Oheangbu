"""Publish the provisional fifth summon checkpoint; no build, upload or git mutation."""
from pathlib import Path
import hashlib
import json
import shutil

root = Path(__file__).resolve().parents[2]
base = root / 'Art/Demo/Summons'
out = base / 'WaterTurtle'
read = lambda p: json.loads(p.read_text(encoding='utf-8-sig'))
records = {n: read(base / f'{n}.json') for n in
           ('water_tests', 'water_visual_tests', 'water_scene_audit', 'runtime_water_tests')}
for name, data in records.items():
    assert data['status'] == ('PASS_RUNTIME_API' if name.startswith('runtime') else 'PASS'), (name, data)
    assert not data['failed']
restore = read(root / 'Art/Demo/Chapter2/runtime_tests.json')
assert restore['suffixRestored'] and not restore['active'] and not restore['holdingPlay']
fbx = read(out / 'fbx_verification.json')
assert fbx['status'] == 'PASS'
source = root / 'Art/SpellVFX120/WaterTurtle/WaterTurtle_Working.blend'
assert hashlib.sha256(source.read_bytes()).hexdigest() == '10028219ea048162f4bb706f04b45971b47386dcb18b1dff5881dc3b795a7c93'
for name in records:
    shutil.copy2(base / f'{name}.json', out / f'{name}.json')
for name in ('compile.json', 'compile.log'):
    shutil.copy2(base / name, out / name)
shots = [('om_water_external_v1', '외부 시점: 실제 명중 직후 물줄기와 포말. 관처럼 보이는 표현은 보완 필요'),
         ('om_water_player_v1', '플레이 시점: 등껍질에 공격이 가리는 문제가 남음')]
for name, _ in shots:
    shutil.copy2(root / f'Art/Demo/Foundation/{name}_1920x1080.png', out / f'{name}_1920x1080.png')
counts = {name: len(data['passed']) for name, data in records.items()}
audit, runtime = records['water_scene_audit'], records['runtime_water_tests']
report = f'''# 옴 — 수 거북 전투 첫 연결

기존 승인 거북의 메시·UV·텍스처를 보존한 임시 전투 파생본이다. 최종 미술 완성이 아니다. {audit['triangles']:,}tris·{audit['materials']}재질·{audit['bones']}본, 대기/전진/후진/물줄기 준비·회복 4클립을 연결했다. 추가 Meshy 생성, 영상, 플레이어 빌드는 없다.

## 연결 내용

추종 속도0.6m/s, 선호 거리4m와 최소 거리2.5m. 공격은0.6초 준비·0.7초 발사·0.5초 회복이며 시전 때 고정한 방향으로 범위6m·반경0.22m·속도12m/s의 물줄기를 보낸다. 대상·생명주기·접촉 시각을 고정하고, 실제 전선 도착 때 생존·이동·차폐를 다시 확인한다. 각 공격의 대상별 피해는 한 번이며, 도중 진입·부활·범위 밖에는 새 피해를 만들지 않는다. 물줄기는 발사가 끝난 뒤 꼬리가 전진해 정리된다.

처음 입 소켓은 턱 아래에 있었고 실제 입술 높이로 고쳤다. 발사 중 Unity 로컬 입 위치(0,0.954323,1.808349)m, Blender85표본의 변화0, 실제 명중 시 소켓–발생점 오차{runtime['waterSocketError']:.6f}m. 정점당 최대4웨이트·정상화 오차{audit['weightError']:.3g}. FBX 왕복34자세의 최대 표면 오차{fbx['maxSurfaceRoundtripErrorM']:.3g}m.

## 검사 결과

| 항목 | 판정 | 근거와 한계 |
|---|---|---|
| 물줄기 판정 | 통과 | {counts['water_tests']}개: 시간·이동 전선·중복·다수·대상 사망/회피·벽·취소. 30/60/120은 시간 간격 조건이며 측정 FPS가 아님 |
| 시각 메시·포말 | 통과 | {counts['water_visual_tests']}개: 정점5292개·입자512개 범위, 동일 시간 반복, 벽 제한, 배출 종료, 비활성/재활성/삭제. 포말 native 저장소 초기화 누락도 수정 |
| Unity 설정 | 통과 | {counts['water_scene_audit']}개: 스킨·클립·재질·본·입·5종 프로필 단일 연결 |
| 실제 게임 Update | 통과 | {counts['runtime_water_tests']}개. 진단 시전→첫20피해, 먹1→0.7, 입 오차0, 활동20초와 형성/소멸 후 잔존0. 정지한 기존 적을 사용 |
| 다른 소환수 회귀 | 통과 | 곰41·놈35·솜38·몸45·공통22, 총181개. 옴45개 포함 전체226개 |
| 컴파일 | 통과 | 9어셈블리·664소스. 플레이어 빌드를 만들지 않음 |
| 원본·저장 보호 | 통과 | 원본 SHA 유지, UUID 진단 저장 사용, 이전 슬롯 및 Game View 크기 복원 |
| 물의 최종 외형 | 실패·보완 필요 | 움직이는 물 표면은 표시되지만 관처럼 보이는 형상과 과한 흰 농도가 남음 |
| 공격 시야 | 실패·보완 필요 | 플레이어 시점에서 큰 등껍질과 플레이어가 물줄기를 가림 |
| 입·보행 최종 동작 | 미완료 | 입이 닫힌 기존 형태, 약한 준비 동작. Blender 보행 표면의 최대 지면 관통 약1.09cm. 실제 지형 발 접지·미끄러짐은 미검증 |
| 수동 전투·이동 적 AI·성능 | 미검증 | 실제 입력 완주·8.33ms·다수 소환 전투 성능은 검사하지 않음. 검정 시험 표적은 완성 적 외형으로 세지 않음 |

물줄기는 경과 시간을 직접 받는 전용 URP 셰이더와 재사용 KTP Noise/Ball 텍스처로 구성했다. PolyOne Shader Graph를 적용했다고 주장하지 않는다. 공급자 재질, 기존 TurtleJade·소환 문양·소멸 프리팹은 보존했다. 1080p 정지 이미지2장을 순차 캡처하고 열어 확인했다.

## 파일과 다음 단계

Blender: `WaterTurtle_Combat.blend`. FBX·프리팹·설정은 `Oheangbu/Assets/_Project/Art/Demo/Summons/WaterTurtle/`에 있다. FBX 통계는 모델 본체만 계산하며, 별도 런타임 물줄기는1152tris와 최대64입자다.

곰·놈·솜·몸·옴5종의 첫 전투 연결까지 도달했다. 미술·수동 전투·성능 승인은 남아 있다. 다음 캠페인 구현은 심부의 금 속성 선행 학습, 청룡, 국·仁와 재탐험이며 전체 데모는 미완료다.
'''
(out / 'REPORT.md').write_text(report, encoding='utf-8')
figures = ''.join(f'<figure><img src="{name}_1920x1080.png"><figcaption>{label}</figcaption></figure>' for name, label in shots)
(out / 'REVIEW.html').write_text(f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>옴 — 물줄기 전투 첫 연결</title>
<style>body{{background:#1c2427;color:#e3e6df;font:17px/1.8 system-ui;margin:0}}main{{max-width:1240px;margin:auto;padding:32px 24px}}img{{display:block;width:100%}}figure{{margin:28px 0}}figcaption{{color:#bdcbd0}}a{{color:#addde9}}.notice{{background:#303b3f;padding:20px}}</style><main>
<p>오행부 · 옴 수 거북</p><h1>입에서 전진하는 물줄기로 지원</h1><p class="notice">임시 전투 연결 검토입니다. 진단 시전·정지 적·실제 게임 Update 결과이며, 수동 전투·성능·최종 미술은 미검증입니다. 물줄기의 관 같은 형태와 등껍질에 의한 시야 가림은 보완 항목입니다.</p>
<p>16,648tris · 1재질 · 18본 · 4클립. 판정45 · 시각 범위18 · Unity8 · 실제 Play{counts['runtime_water_tests']}검사 통과.</p>{figures}
<p><a href="REPORT.md">기술 검사와 잔여 문제</a> · <a href="WaterTurtle_Combat.blend">Blender</a> · <a href="runtime_water_tests.json">실제 Update 검사</a> · <a href="water_visual_tests.json">물줄기·포말 검사</a> · <a href="../DokkaebiClub/REVIEW.html">이전 몸</a></p></main></html>''', encoding='utf-8')
print('Published provisional turtle checkpoint:', counts)
