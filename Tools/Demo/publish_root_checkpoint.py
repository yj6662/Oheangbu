"""Publish the measured root-attack checkpoint, keeping the earlier horn review intact."""
from pathlib import Path
import html,json,shutil,hashlib

root=Path(__file__).resolve().parents[2]
base=root/'Art/Demo/Summons'
out=base/'RootAttack';out.mkdir(exist_ok=True)
def read(path): return json.loads(path.read_text(encoding='utf-8-sig'))
runtime=read(base/'runtime_root_tests.json'); tests=read(base/'root_tests.json')
assert runtime['status']=='PASS_RUNTIME_API' and not runtime['failed']
assert tests['status']=='PASS' and not tests['failed']
saved=read(root/'Art/Demo/Chapter2/runtime_tests.json')
assert saved['suffixRestored'] and not saved['active'] and not saved['holdingPlay'] and not saved['failures']
preserved=read(base/'root_source_hashes.json')
assert all(hashlib.sha256((root/item['path']).read_bytes()).hexdigest()==item['sha256'] for item in preserved)
for view in ('external','player'):
    for suffix in ('_1920x1080.png','_capture.json'):
        src=root/'Art/Demo/Foundation'/('gom_root_'+view+'_v3'+suffix)
        shutil.copy2(src,out/src.name)
shutil.copy2(root/'Art/Demo/Chapter2/runtime_tests.json',base/'save_isolation_and_restore.json')
support_path=base/'WoodDeer/unity_active_support_diagnostic.json'
support=read(support_path)
gaps=[]
for pose in support['poses']:
    for skin in pose['skins']:
        for foot in skin['feet']:
            if foot['support']['chosen']: gaps.append((foot['name'],foot['support']['rawGap']*1000))
table='\n'.join(f'| {name} | {gap:.2f} |' for name,gap in gaps)
compile_result=read(base/'compile.json')
assert all(item['exitCode']==0 for item in compile_result['results'])
report=f'''# 곰 직선 뿌리 공격·발 접지 후속

뿔 사거리 밖에서 짧은 직선 뿌리를 사용하는 실제 전투를 연결했다. 기존 목 사슴 리그·재질과 Botanical 뿌리 메시를 재사용했다. 아직 나머지 네 소환수 전투와 전체 데모는 완료하지 않았다.

## 구현과 전투 규칙

- 뿔 공격 2.4m, 뿌리 대상 선택 6m. 앞발 쪽에서 폭0.7m의 고정 지면 경로를 만든다. 시전0.55초 뒤 전선8m/s로 진행한다.
- 시전 시 지면 표본·대상 목록·생명주기를 고정한다. 매 프레임 임의 경로나 목표를 다시 생성하지 않는다. 실제 전선 도착 때 대상의 현재 위치·생존·폭·고도·차폐를 재검사한다.
- 땅이 없거나 큰 단차·벽이 있으면 경로를 자른다. 발사 도중 생긴 벽·지면 제거도 전선에서 확인한다. 옆으로 피한 대상, 이미 지난 전선에 재진입한 대상, 다른 생명주기로 부활한 대상은 늦게 맞지 않는다.
- 피해·접촉은 기존 공통 경로다. 뿌리의 배율1.0과 뿔의0.8을 별도로 기록하며, 시전 필세와 속성 강화는 소환 시 한 번 저장한다. 소환 공격으로 그로기·플레이어5속성 완주를 쌓지 않는다.
- Botanical/Mesh_Root를 최대8개 고정 크기 부품으로 재사용한다. 지면에서 올라와 .45초 유지·.25초 회수한다. 수피 재질 사본의 밝기만 보완했으며 공급자 원본·전역 노출·블룸은 바꾸지 않았다.
- RootCast 동작·지면 외형·판정은 같은 소환 경과 시간을 사용한다. 대기 중·활동 종료·교체·정리에서 남은 공격이 재생되지 않는다.

## 발견한 오류와 수정

정확히 .025초씩 진행하는 검사에서 표시용 float 시각은 .75초였지만 내부 double 시각은 발사 직전이었다. 관리자가 정상 대기를 잘못 취소했다. 같은 공격 ID가 대기 중이면 다음 틱까지 기다리도록 수정했고 원래 검사 간격을 유지해 재검증했다. 실패 원장은 `../root_tests_before_release_boundary_fix.json`에 보존했다.

첫 실제 뿌리 검사에는 보행/뿔 검사에서 가져온 ‘발이 움직여야 함’ 조건이 들어 있었다. 제자리 뿌리 시전은 지지발을 유지하므로, 이후 검사는 머리 시전 모션과 실제 발 접지를 분리했다. 기존 실패 기록도 보존한다.

## 실제 검사

| 범위 | 결과 |
|---|---|
| 뿌리 분리 기술 검사 | {len(tests['passed'])} 통과 / {len(tests['failed'])} 실패 |
| 실제 데모 Play 진단 시전 | {len(runtime['passed'])} 통과 / {len(runtime['failed'])} 실패 |
| 소스 컴파일 | {len(compile_result['results'])}어셈블리 / {sum(a['sources'] for a in compile_result['results'])}파일 오류0 |

실제 데모에서 양수 피해 {runtime['hits']}회, 누적 {runtime['damageApplied']:.2f}, 뿌리 위력 스냅샷 {runtime['rootDamageSnapshot']:.2f}, 먹 {runtime['inkBefore']:.2f}→{runtime['inkAfter']:.2f}. 지면 계획 {runtime['rootPlanPoints']}점, 경로 실패 {runtime['pathFailures']}회. 활동20초 뒤 소멸/잔존0을 확인했다.

30·60·120fps 및0.25배속 검사는 분리된 manager에 해당 scaled dt를 공급하는 기술 검사다. 실제 렌더 성능이나 수동 입력 테스트가 아니다. 데모 Play도 고정한 실제 적에게 진단 도구가 시전해 검증했으며 움직이는 적/손글씨 조작은 미검증이다.

## 발 접지 측정

Blender와 Unity 단독 임포트 모션에서 발바닥은 기준면 약1.5~5.3mm 높이로 유지됐다. 실제 지면에서는 기존3.5cm 배치 여유와 경사 때문에 앞발40.37~45.38mm, 뒷발10.27~12.62mm가 측정됐다. 이를 모델 자체 변형 오류와 구분했다.

실제 스킨 메시의 발바닥을 측정해 제한된 몸통 하강과 본 길이를 유지하는 2본 IK를 추가했다. 애니메이션 평가 직후에만 보정하며 들린 발은 원래 동작을 유지한다. 현재 측정 지점의 몸통 보정은 약4cm다. 지원 지면 없음·과도한 경사·도달 불가는 보정 실패로 남긴다.

최신 실제 지면 BakeMesh 측정값:

| 발 | 실제 지면과 간격(mm) |
|---|---:|
{table}

이 표는 현재 벌목장 한 지점·한 시전 자세의 측정이며 전체 경사, 보행 미끄러짐, 계단 통과의 증거가 아니다. 전체 지형 접지는 별도 확인이 필요하다.

정지 화면에서는 사슴과 지면의 시각적 접지감이 아직 약하다. 위 콜라이더 간격 통과를 렌더 표면·그림자와의 외형 합격으로 간주하지 않는다. 뿌리도 연결·판정용 첫 외형이며 최종 미술 승인은 남아 있다.

## 산출물·잔여 작업

- 1920×1080 외부/플레이 정지 화면, 실제 피해 원장, 소스·프로필·재질 사본. 영상/새 빌드 없음.
- 기존 Blender/FBX는 보존하며 실제 사슴 Unity16,587tris/3재질은 유지한다. 뿌리 표현의 추가 메시 비용은 사슴 자체 삼각형 수에 숨겨 합산하지 않는다.
- 사용자 저장 보존·독립 시험 슬롯과 화면 설정 복원. 원본 뿌리 메시/재질 SHA256 일치.
- 미검증: 손글씨·수동 전투, 이동하는 적 AI·플레이어와의 조합, 지형 전수 접지·성능120fps·최종 외형 승인.
- 미완료: 놈·솜·몸·옴 전투, 심부/청룡·국/仁·호송·검문·남문과 통합 완주. 곰의 지형/동작 보완 후 다음 소환수로 이어간다.
'''
(out/'REPORT.md').write_text(report,encoding='utf-8')
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>곰 — 직선 뿌리 공격</title>
<style>body{{margin:0;background:#191d19;color:#e9e5d7;font:17px/1.7 system-ui,sans-serif}}main{{max-width:1250px;margin:auto;padding:32px 24px}}a{{color:#bed5ac}}img{{width:100%;border-radius:6px}}figure{{margin:28px 0}}figcaption{{color:#bdc1b6}}.note{{background:#303329;padding:18px;border-left:4px solid #b9ba86}}table{{border-collapse:collapse;width:100%}}td,th{{padding:8px;border-bottom:1px solid #535847;text-align:left}}</style>
<main><p>오행부 · 데모 · 곰 전투 후속</p><h1>짧은 직선 뿌리 공격</h1><p class="note">실제 데모 Play의 진단 시전·고정 적 검사입니다. 수동 전투·최종 모션/미술·120fps 검증과 구분합니다.</p>
<p>기존 뿌리 메시를 재사용해 앞발에서 정해진 지면 경로로 전진합니다. .55초 시전, 폭.7m, 길이6m, 전선8m/s의 첫 제작값입니다. 뿔 공격은 가까운 적에게 계속 사용합니다.</p>
<figure><img src="gom_root_external_v3_1920x1080.png" alt="직선 뿌리 공격 외부 시점"><figcaption>외부 시점 · 시전 동작과 고정 지면의 뿌리 전선</figcaption></figure>
<figure><img src="gom_root_player_v3_1920x1080.png" alt="직선 뿌리 공격 플레이 시점"><figcaption>플레이 시점 · 동일 명중 직후. 사슴과 플레이어가 겹쳐 뿌리 전선 가시성은 제한됨</figcaption></figure>
<h2>검사</h2><p>분리 기술 검사 {len(tests['passed'])}개, 실제 Play 검사 {len(runtime['passed'])}개 통과. 양수 명중 {runtime['hits']}회, 누적 피해{runtime['damageApplied']:.0f}, 수명 종료 후 외형 잔존0. 벽·지면 공백·대상 사망·회피·중복과 시간 경계 오류를 확인했습니다.</p>
<p>발 접지는 단독 임포트와 실제 지면의 BakeMesh로 구분해 측정했습니다. 전체 경사면 보행·최종 외형은 후속 검토 대상입니다.</p>
<p><a href="REPORT.md">상세 보고서</a> · <a href="../root_tests.json">기술 검사</a> · <a href="../runtime_root_tests.json">실제 Play 원장</a> · <a href="../WoodDeer/unity_active_support_diagnostic.json">발 접지 원장</a> · <a href="../Versions/horn_first/REVIEW.html">이전 뿔 공격 체크포인트</a></p></main></html>'''
(out/'REVIEW.html').write_text(page,encoding='utf-8')
print(out/'REVIEW.html')
