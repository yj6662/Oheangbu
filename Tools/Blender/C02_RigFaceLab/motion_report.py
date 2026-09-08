"""Summarize recorded B1 results and strictly decode its actual continuous video."""
import hashlib,json,re,subprocess
from pathlib import Path
ROOT=Path('C:/Users/yj666/Oheangbu');MOTION=ROOT/'Art/PlayerPhase1/C02_RigFaceLab/Motion';B1=MOTION/'B1'
FFMPEG='C:/Users/yj666/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/Lib/site-packages/imageio_ffmpeg/binaries/ffmpeg-win-x86_64-v7.1.exe'
report=json.loads((B1/'B1.json').read_text(encoding='utf8'));numeric=json.loads((B1/'motion_validation.json').read_text(encoding='utf8'))
render=json.loads((B1/'render_progress_final.json').read_text(encoding='utf8'));video=B1/'B1_Original_vs_Edited_Sequence.mp4'
if render.get('status')!='VIDEO_ENCODED' or not video.is_file():raise RuntimeError('Final framed video is not ready')
process=subprocess.run([FFMPEG,'-hide_banner','-loglevel','info','-xerror','-i',str(video),'-f','null','-','-progress','pipe:1','-nostats'],capture_output=True,text=True,encoding='utf8',errors='replace',timeout=120)
frames=[int(v) for v in re.findall(r'^frame=(\d+)',process.stdout,re.M)];dimension=re.search(r'(\d{3,5})x(\d{3,5})[^\r\n]*?([\d.]+) fps',process.stderr)
metadata={'width':int(dimension[1]),'height':int(dimension[2]),'fps':float(dimension[3])} if dimension else {}
ok=process.returncode==0 and frames and frames[-1]==331 and metadata=={'width':1280,'height':720,'fps':30.0}
validation={'status':'PASS' if ok else 'FAIL','scope':'Complete MP4 decode, exact frame count, dimensions and frame rate only. Not character naturalness or Unity playback.',
            'file':str(video),'sha256':hashlib.sha256(video.read_bytes()).hexdigest(),'decoded_frames':frames[-1] if frames else None,'expected_frames':331,'metadata':metadata,
            'returncode':process.returncode,'decoder_error_tail':process.stderr[-1000:] if process.returncode else None}
(B1/'video_validation.json').write_text(json.dumps(validation,ensure_ascii=False,indent=2),encoding='utf8')
if not ok:raise RuntimeError('Continuous video encoding verification failed')
report.update({'status':'NUMERIC_IMPROVEMENTS_WITH_RESIDUAL_LIMITS','video':'Motion/B1/B1_Original_vs_Edited_Sequence.mp4',
               'numeric_validation':'Motion/B1/motion_validation.json','video_validation':'Motion/B1/video_validation.json',
               'animation_naturalness':{'status':'UNVERIFIED','reason':'Measured stance improvements; Run whole-mesh minimum -24.32mm and Attack right-foot inferred support-window extent 147.56mm remain. Full naturalness and actual Unity speed/contact need further judgment.'}})
(B1/'B1.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
summary={'stage':'B','attempt':1,'attempts_used':{'B':1},'attempt_limit':3,'status':'PARTIAL_UNVERIFIED','selected_for_independent_integration':'B1','best_file':'Motion/B1/Motion_B1.blend',
         'selection_scope':'Measured motion improvement on A2; not full body/cloth, animation naturalness or canonical-player approval',
         'verdicts':{'animation_naturalness':report['animation_naturalness'],'secondary_motion':report['secondary_motion'],'weapon_grip':report['weapon_grip']},
         'source':'Body/A2.blend','model':'Motion/B1/Motion_B1.blend','export':'Motion/B1/C02_B1_Body_Motions.fbx','evidence':['Motion/B1/motion_validation.json','Motion/B1/video_validation.json','Motion/B1/render_progress_final.json'],
         'videos':['Motion/B1/B1_Original_vs_Edited_Sequence.mp4'],'diagnostic_video':'Motion/B1/Diagnostics/B1_Sequence_FramingFail.mp4',
         'additional_paid_credits':0,'paid_requests':0,'historical_autoplayer_credits':123,'canonical_player_replaced':False}
b2path=MOTION/'B2/B2.json'
b2=json.loads(b2path.read_text(encoding='utf8')) if b2path.is_file() else {}
if b2.get('selection_status')=='SELECTED_FOR_FINAL_EXPERIMENT':
    summary.update({'attempt':2,'attempts_used':{'B':2},'selected_for_independent_integration':'B2','best_file':'Motion/B2/Motion_B2.blend',
                    'source':'Motion/B1/Motion_B1.blend','model':'Motion/B2/Motion_B2.blend','export':'Motion/B2/C02_B2_Body_Motions.fbx',
                    'previous_candidate':'Motion/B1/Motion_B1.blend','unity_idle_probe':b2.get('unity_idle_probe'),
                    'final_unity_capture_status':b2.get('unity_final_capture',{}).get('status','AWAITING_FINAL_REVIEW'),'videos_scope':'B1 is the preserved Blender comparison. B2 final Unity evidence, when recorded, is separate from overall naturalness approval.'})
    summary['verdicts']['animation_naturalness']=b2['animation_naturalness']
    summary['evidence']+=['Motion/B2/B2.json','Motion/B2/REPORT.md','Unity/B2_BodyProbe_camera_calibration.json','Unity/B2_BodyProbe_import_audit.json']
    if b2.get('unity_final_capture',{}).get('status')=='COMPLETE':
        summary['unity_final_capture']=b2['unity_final_capture']
        summary['videos'] += [b2['unity_final_capture'][k] for k in ('sequence_video','blink_video')]
        summary['evidence'] += ['Unity/UNITY_REPORT.md','Unity/unity_validation.json']
(MOTION/'experiment_result.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2),encoding='utf8')
lines=['# C02 단계 B — 동작 개선 결과', '', '**B1을 독립 통합 실험에 전달했다. 전체 자연스러움은 UNVERIFIED다.** 추가 시도는 1/3, 추가 유료 사용은 0이다. 이전 AutoPlayerV1의 123크레딧은 새 지출이 아니다.', '',
'원본 A2의 메시·UV·웨이트·정지 골격·원본 4클립을 보존했다. 복제 클립에 길이를 늘리지 않는 다리 회전 IK, 지지 구간 보정과 Attack 끝 골반 중심 복귀를 적용했다. 원본 Idle/Walk/Run 루프는 이미 닫혀 있어 원속도·주기를 유지했다. 팔 스윙은 원본을 유지하며 새 의복 보조 움직임은 만들지 않았다.', '',
'## 파일과 재생', '',
'- [편집용 Motion_B1.blend](B1/Motion_B1.blend), [몸·동작 FBX](B1/C02_B1_Body_Motions.fbx).',
'- [원본/수정 연속 비교 영상](B1/B1_Original_vs_Edited_Sequence.mp4): 왼쪽 원본 C02 동작, 오른쪽 B1. 양쪽 모두 동일 A2 메시와 새로 만든 동일 전환 정책을 사용한다. 기존 게임 플레이 영상이 아니다.',
'- 1280×720, 30fps, 331프레임. Idle → Run 6주기 → Idle → Attack → Idle. Run 비혼합 구간은 43–156프레임이며 원속도를 유지한다. 영상 안에 라벨을 넣지 않았다.',
'- 바닥 격자는 25cm이며 카메라는 기록한 원래 클립 속도로 이동을 추적한다. 게임의 4.5m/s 설정을 바꾸지 않았다. 모든 정수 프레임의 두 메시 경계에서 고정 카메라 크기와 여백을 정해 발끝을 포함했다.',
'- [재생·프레이밍 기록](B1/render_progress_final.json), [전체 MP4 디코드 검사](B1/video_validation.json), [수치 비교](B1/motion_validation.json).', '',
'## 실제 수치와 한계', '',
'아래 수평 범위는 원본에서 정한 지지 **추정** 구간의 신발 밑면 프로브다. 실제 압력·전체 천 관통 검사와 구분한다. 원래 속도는 Walk 약1.554m/s, Run 약5.362m/s로 추정했다.', '',
'| 클립·발 | 지지창 수평 범위: 원본 → B1 |', '|---|---:|']
for clip in numeric['clips']:
    for side,result in clip['support_probe_results'].items():
        a=result['original']['maximum_support_window_horizontal_extent_m'];b=result['edited']['maximum_support_window_horizontal_extent_m']
        lines.append(f'| {clip["edited"]} {side} | {a*100:.2f} → {b*100:.2f}cm |')
lines += ['', 'Idle 오른발 밑면 프로브 높이는 3.3–4.7cm에서 약4.4–5.0mm로 줄었다. Attack 끝의 수평 골반 이동 약37.5cm를 복제 클립에서 중심 복귀시켰다. 원본 4클립의 전체 정수 프레임 본 위치는 Baseline과 차이0m였고 본 scale 차이는 최대4.8e-7이다. 실제 51,760삼각형·24본·정점당 최대5웨이트를 유지했다.', '',
'**남은 한계:** 전체 메시 최저 높이는 Run에서 바닥 아래24.32mm다. 일부 신발 밑면을 골랐을 때의 양수 결과로 전체 지면 통과를 선언하지 않는다. Attack 오른발 지지 추정창 수평 범위도14.76cm가 남는다. 이 구간의 의도된 이동/회전과 미끄러짐은 추가 시각 판단이 필요하다. 단순 임계값으로 합격을 선언하지 않았다.', '',
'Run 원본을 게임4.5m/s 이동에 그대로 붙이면 속도가 맞지 않는다. 참고 재생 배율은 4.5/5.362≈0.839이며 실제 Unity 연결과 접지는 별도 검증 대상이다. 6주기 영상은 원속도1.0배·해당 native 이동 기준이다.', '',
'손가락 본은0개다. 개폐/붓대 피부 접촉·Attack 파지는 NOT_ATTEMPTED이며 빈손 공격을 파지 완료로 취급하지 않는다. A2 의복의 잔여 불확실성이 있어 안정된 보조 영역을 선정하지 않았고 secondary_motion은 NOT_ATTEMPTED다.', '',
'## 검수 범위와 재실행', '',
'대표 Idle·Run·Attack 정지 프레임을 실제 렌더로 확인했으며 최종 연속 MP4 전체 프레임을 디코드 검증했다. 이 검사는 Unity 플레이나 자연스러움 전체 PASS를 뜻하지 않는다. 최초 좁은 프레이밍 영상은 [진단 자료](B1/Diagnostics/B1_Sequence_FramingFail.mp4)로 분리했다. 미완성 Cycles 프레임은 최종 EEVEE 영상과 혼합하지 않았다.', '',
'도구는 `Tools/Blender/C02_RigFaceLab/motion_diagnose.py`, `motion_build_b1.py`, `motion_sequence_export.py`, `motion_render_sequence.py`, `motion_validate.py`, `motion_report.py`다. Blender 도구는 지정 `.blend`를 독립 `--background` 프로세스로 연다. B1 생성기는 같은 원본과 기존 결과가 있으면 재사용하고, 다른 원본이나 미완료 결과의 덮어쓰기는 거절한다. 나머지는 Motion 전용 출력만 재생성한다. 정본 씬·공용 PlayerRig·공유 Blender는 수정하지 않았다.']
if b2.get('selection_status')=='SELECTED_FOR_FINAL_EXPERIMENT':
    complete=b2.get('unity_final_capture',{}).get('status')=='COMPLETE'
    intro=['**최종 실험 선택은 B2다. 전체 품질·자연스러움은 UNVERIFIED다.** B 시도2/3, 추가 유료0. '+('최종 Unity 연속 캡처와 전체 디코딩 검증을 완료했다.' if complete else '최종 Unity 연속 캡처 검수는 대기 중이다.'), '',
           '원본 Idle의 Hips scale≈1.17647 문제를 새 Idle에서1로 복원하고, 고정 루트/지면에서 실제 신발 기준 골반·발목 자세를 다시 계산했다. 조기 Unity Humanoid Idle0은 minY=0.004073918m, maxY=1.626909256m로 측정되어 이전 부유 원인 수정이 확인됐다. 이 한 자세의 수치는 전체 동작·의복 품질 통과를 뜻하지 않는다.', '',
           '[B2 편집본](B2/Motion_B2.blend) · [B2 FBX](B2/C02_B2_Body_Motions.fbx) · [B2 세부 보고](B2/REPORT.md) · [조기 Unity 근거](../Unity/B2_BodyProbe_camera_calibration.json). Walk/Run/Attack은 B1 그대로이고, 아래 영상은 B1 비교 증거다.', '',
           '## B1 비교 이력 — 원본 보존', '']
    if complete:
        intro[2:2]=['실제 Unity 전신444프레임/30fps/14.8초, 눈꺼풀 근접180프레임/30fps의 전체 디코딩 통과·덮어쓰기0. 고정 root/바닥0에서 Idle 첫 두 표본 minY=4.074/4.094mm다. [전신 영상](../Unity/B2_Final_Sequence_Full/B2_Final_Sequence_Full.mp4) · [눈꺼풀 영상](../Unity/B2_Final_Blink_Close/B2_Final_Blink_Close.mp4) · [Unity 최종 보고](../Unity/UNITY_REPORT.md). 전체 미술·접촉·게임플레이 승인을 뜻하지 않는다.', '']
    lines=lines[:2]+intro+lines[2:]
(MOTION/'REPORT.md').write_text('\n'.join(lines)+'\n',encoding='utf8')
print(json.dumps({'report':str(MOTION/'REPORT.md'),'video':str(video),'video_validation':validation['status'],'attempts_used':summary['attempt'],'additional_paid_credits':0},ensure_ascii=False))
