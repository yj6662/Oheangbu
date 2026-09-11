"""Write the scoped handoff from the individual assets and observed input runs."""
import json
from pathlib import Path
import finish_giyeok_review as final

OUT = final.OUT

def main():
    inputs = []
    for glyph in '가고거':
        candidates = []
        for path in (OUT/'PlayerInputBatches').glob('batch04_*.json'):
            report = json.loads(path.read_text(encoding='utf-8'))
            if report.get('requestedGlyphs') == glyph:
                candidates.append((report['runId'], path, report))
        _, path, report = max(candidates)
        assert report['status'] == 'PASS_OBSERVED_SELECTED_BATCH_INPUT_CASTS', report
        assert report['errors'] == report['failed'] == report['residualEffects'] == 0
        assert report['completed'] == 1 and report['sceneFileUnchanged']
        assert all(report[k] for k in ('inputFilterRestored','virtualDevicesRemoved','loopRemoved','drawingExited','cameraPropertiesRestored','inputSettingsRestored','currentDevicesRestored','actionMapStatesRestored','drawingBindingsRestored','cameraPoseReturned','cursorRestored','timeScaleReturned'))
        inputs.append(dict(glyph=glyph,report=path.relative_to(OUT).as_posix(),appMvid=report['appMvid'],status=report['status'],configuredTargets=report['configuredTargets'],case=report['cases'][0],cleanup='PASS'))
    evidence=dict(status='PASS_THREE_SEPARATE_INPUT_RUNS',glyphs='가고거',rows=inputs,scope='Registered template strokes through virtual devices into actual C2 input/recognition/casting/VFX; zero configured enemies. No direct recognition-result or cast injection.',unverified=['physical handwriting','targetful damage and parry outcomes','bit-for-bit raw-input regression','performance','user visual approval'])
    (OUT/'giyeok_player_input_audit.json').write_text(json.dumps(evidence,ensure_ascii=False,indent=2),encoding='utf-8')
    lines=['# 오행부 — ㄱ 술식 VFX 제작 결과', '', '2026-09-10 · 사용자 요청 범위: ㄱ까지만 완료 후 보고', '',
        '배정된 ㄱ 술식 20종의 개별 VFX 자산과 기술 재생 기록을 정리했다. 기본·외부 시점 영상은 각 20개, 합계 40개다. 한 술식씩 선택하는 [검토 페이지](GIYEOK_REVIEW.html)에서 확인할 수 있다. 비주얼 최종 판정은 **사용자 검토 대기**다.', '',
        '군·굼·굿·궁 4칸은 원래 의도한 미배정 상태로 유지했다. 다른 초성의 추가 제작으로 넘어가지 않는다.', '',
        '## 완료 범위', '',
        '- KoreanTraditionalPattern_Effect의 문양·발동·타격 계층과 실제 식물 메시·텍스처를 활용해 각 술식의 본체와 동작을 구성했다.',
        '- 현재 게임 경로에 연결된 ㄱ 술식은 **가·고·거 3종**이다. 나머지 17종은 프리팹·카탈로그와 표현 신호를 제공한 상태이며, 신규 피해·회복·소환 AI 등의 게임 규칙 구현을 뜻하지 않는다.',
        '- 각·검·공은 채택 버전15의 영상과 당시 Play 검사 기록을 보존했다. 이번에는 관련 원본44개 파일의 해시 일치를 다시 확인했다. 나머지17종은 개별 후속 버전의 기록을 사용한다.',
        '- 곰은 기존 Meshy 7 사슴 모델을 사용한 정적 소환·소멸 표현이다. 이동·공격 애니메이션과 소환수 AI는 포함하지 않는다. 이번 ㄱ 마무리의 추가 Meshy 생성 요청은 0건이다.', '',
        '## 술식별 전달물', '',
        '삼각형 수는 고정 본체 인스턴스와 기록된 문양 메시 기준이다. KTP 파티클, 선 렌더러가 만드는 면, 그림자·다중 패스와 동시 시전 비용을 포함한 프레임 총비용은 아니다. 강의 46tris는 고정 메시만이며 뿌리선 3개를 별도로 사용한다.', '',
        '| 술식 | 시안 | 본체 tris | 연결 상태 | 개별 결과 |',
        '|---|---|---:|---|---|']
    for i,(glyph,key,version,tris) in enumerate(final.CONFIG,1):
        profile=final.read_asset(final.ASSETS/'Profiles'/f'{i:03}_{ord(glyph):04X}.asset')
        state='기존 게임 연결' if glyph in '가고거' else '카탈로그 표현'
        if glyph=='곰':state+=' · 정적 소환수'
        lines.append(f'| {glyph} | {profile["Title"]} | {tris:,} | {state} | [영상·비교]({key}_REVIEW.html) · [보고서]({key}_REPORT.md) |')
    lines += ['', '## 기술 검증', '',
        '| 항목 | 결과 | 범위 |', '|---|---|---|',
        '| 개별 Play 재생·수명 종료 | 통과 | 20종의 해당 제작 버전 기록. 각·검·공은 버전15 기록을 재사용 |',
        '| 검사 중 오류·셰이더 오류 | 통과 | 선택한 20종 기록 각각 0 |',
        '| 프로필·프리팹 참조 및 시연 플래그 | 통과 | 20종 매핑, 생산 프리팹의 PreviewControlled/DemonstrationCues OFF |',
        '| 영상 인코딩·타이밍·출력 해시 | 통과 | 기본20 + 외부20. 촬영과 해당 Play 기록의 어셈블리 식별자 일치 |',
        '| 범위 밖 프로필 보존 | 통과 | ㄱ 24칸 밖의 96개 프로필 해시 불변 |',
        '| 미술 품질 | 사용자 검토 대기 | 에이전트의 미술 PASS 없음 |',
        '| 적이 있는 전투·피해·회복·패링 결과 | 미검증 | 실제 입력 검사의 C2 설정 적 수 0 |',
        '| 동시 시전 CPU/GPU·실제 FPS | 미검증 | Editor 전환 대기 시간을 게임 성능 수치로 사용하지 않음 |', '',
        '### 실제 작도 입력 경로', '',
        '가·고·거를 한 종씩 독립 실행했다. 등록된 XML 획을 가상 마우스·키보드로 재생하여 기존 DrawingInputController, 인식, 채널/DI, 시전 계획, 어댑터, VFX Update와 소멸을 통과했다. 인식 결과나 시전 결과를 직접 주입하지 않았다. 각 실행 뒤 입력 장치·필터·바인딩·카메라·시간 배율 복귀와 잔존 VFX 0을 확인했고 씬 파일은 바뀌지 않았다.', '',
        '| 술식 | 인식 | 원시 점/획 | 공격 계획/VFX | 결과 |', '|---|---|---:|---|---|']
    for row in inputs:
        case=row['case']
        lines.append(f'| {row["glyph"]} | {case["recognized"]} | {case["rawPoints"]}/{case["rawStrokes"]} | {case["plans"]}/{case["spawned"]} | [통과]({row["report"]}) |')
    lines += ['', '거는 공격 계획을 발행하지 않는 패링 경로라 계획 수 0이 정상이다. 기존 가드 지속시간 4초를 읽은 VFX 1개를 확인했다.', '', '이 검사는 적이 없는 C2의 공중 시전 전달 검사다. 손글씨 전반의 인식률, 물리 입력 장치 전달, 적이 있는 명중 시각·피해·패링 성공, 애니메이션 활성 여부의 비트 단위 입력 회귀를 입증하지 않는다.', '',
        '## 남은 작업', '',
        '- 사용자 비주얼 검토와 피드백 반영.',
        '- 카탈로그 17종의 게임 규칙·대상 탐색·회복·피해·이동·소환 AI 연결. 간의 전이, 감의 격발, 갓의 처형 조건, 강의 방어 관통, 걱·건의 회복, 것의 무기 교체, 겅의 동반탄, 곡·곳의 장판, 곤의 쳐올리기, 국의 수직 발판 등은 현재 표현 신호만 구현되어 있다.',
        '- 곰의 움직임·공격 애니메이션, 적이 있는 실제 전투와 동시 시전 성능 검증.', '',
        '## 결과 파일', '',
        '- [전체 검토](GIYEOK_REVIEW.html) · [자산·영상·검사 목록](GIYEOK_MANIFEST.json) · [최종 전달 검사](GIYEOK_DELIVERY_CHECK.json)',
        '- [입력 검사 합본](giyeok_player_input_audit.json) · [범위 밖 프로필 보존](giyeok_outside_scope_check.json) · [각·검·공 원본 재확인](giyeok_botanical_source_recheck.json)',
        '- Unity 자산: `Oheangbu/Assets/_Project/Art/SpellVFX120/{Profiles,Prefabs}`의 001~020. 원본 KTP·식물 에셋과 기존 비교 영상은 보존했다.', '']
    (OUT/'GIYEOK_REPORT.md').write_text('\n'.join(lines),encoding='utf-8')
    print(json.dumps(dict(status='REPORT_WRITTEN',inputRuns=len(inputs)),ensure_ascii=False))

if __name__=='__main__': main()
