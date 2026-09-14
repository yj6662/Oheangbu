"""Assemble a local, evidence-linked review. Does not generate screenshots or run Unity."""
from pathlib import Path
import json, html, os, math

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/PlaytestPolish'
def link(path, label=None):
    p=Path(path)
    rel=Path(os.path.relpath(p, OUT))
    return f'<a href="{html.escape(rel.as_posix())}">{html.escape(label or p.name)}</a>'
def pic(path, label):
    rel=Path(os.path.relpath(Path(path), OUT)).as_posix()
    return f'<figure>{link(path, label)}<img loading="lazy" src="{html.escape(rel)}"><figcaption>{html.escape(label)}</figcaption></figure>'
def foot_note(data):
    """Recompare measured values; never replace the original strict gate status."""
    cases=data.get('cases',[])
    if not cases:return '<p class="note">발 접촉 원장에 측정 조건이 없습니다. 미검증입니다.</p>'
    strict=sum(c.get('status')=='PASS_LIMITS' for c in cases)
    measured=[]
    for case in cases:
        values=[case.get(k) for k in ('maxStanceSlipAfter','maxPenetrationAfter','maxFootSkinPenetrationAfter')]
        if all(isinstance(v,(int,float)) and math.isfinite(v) for v in values):
            measured.append((values[0],max(values[1:])))
    accepted=sum(slip<=.1 and penetration<=.02 for slip,penetration in measured)
    extrema=(f' 최대 미끄러짐 {max(v[0] for v in measured)*1000:.2f}mm, '
             f'관통 {max(v[1] for v in measured)*1000:.3f}mm.') if measured else ''
    missing=f' 측정값 누락·비정상 {len(cases)-len(measured)}개는 미검증입니다.' if len(measured)!=len(cases) else ''
    failed=len(cases)-strict
    return ('<p class="note">'+html.escape(
        f"발 접촉: 원장 엄격 기준 {strict}/{len(cases)}, 사용자 수치 기준 "
        f"(미끄러짐 100mm 이내·관통 20mm 이내) {accepted}/{len(cases)}. "
        f"원장 상태 {data.get('status','미검증')}와 엄격 기준 미통과 {failed}개를 보존합니다."
        +extrema+missing+' 합성 단일 동작·진단 평면 범위이며 실제 입력·지형 보행·전체 옷감은 미검증입니다. '
        'RIG_PASS나 실제 보행 완료로 취급하지 않습니다.')+'</p>')
sections=['<section><h2>통합 보고서와 빌드 후보</h2><p>'+link(OUT/'REPORT.md','기술 검사·실제 확인 범위·남은 문제')+'</p>']
release=ROOT/'Builds/Playtest-20260915/20260914T022453Z'
if (release/'release_build_report.json').exists():
    sections.append('<p>'+link(release/'README_플레이테스트.txt','Windows 후보 실행 안내')+' · '+link(release/'release_build_report.json','빌드 원장')+'</p><p class="note">최종 빌드는 한 번 생성했습니다. 빌드 오류 0, 경고 582. 일반 창 표시·실제 플레이는 미검증이며, 숨김 실행의 검정 PNG를 화면 표시 통과로 취급하지 않습니다. 120fps 목표도 현재 Editor CPU 측정에서 미달입니다.</p>')
sections.append('</section>')
assessment=OUT/'BuildSmoke/VISUAL_ASSESSMENT.md'
if assessment.exists(): sections.append('<p>'+link(assessment,'숨김 실행 화면의 별도 판정 · 미검증')+'</p>')
for name,title in [('Hands','A 파지'),('Locomotion','이동·점프·착석'),('AudioInk','먹 획득·효과음'),('Vegetation','식생'),('Map','지도')]:
    folder=OUT/name
    refs=[]
    for filename in ('REVIEW.html','REPORT.md','TECHNICAL_REPORT.md','IMPLEMENTATION.md'):
        if (folder/filename).exists():refs.append(link(folder/filename))
    sections.append(f'<section id="{name}"><h2>{title}</h2><p>'+ ' · '.join(refs)+'</p>')
    if name=='Hands':
        sections.append('<p>선택: A — 자루를 감싸는 안정된 파지. 시안 이미지와 실제 Unity 검증 이미지를 구분해 보세요.</p>')
        sections.append('<div class="grid">')
        if (folder/'Grip_A.png').exists():sections.append(pic(folder/'Grip_A.png','선택한 A 참조 시안 · 생성 이미지'))
        candidates=sorted((folder/'Validation/Unity').glob('Screenshots/*synthetic_center.png'))
        if candidates:sections.append(pic(candidates[-1],f'최신 실제 Unity 메시 · 합성 중앙 작도 자세 · {candidates[-1].stem}'))
        seated=sorted((folder/'Validation/Unity').glob('Screenshots/*synthetic_sit-carry.png'))
        if seated:sections.append(pic(seated[-1],f'최신 실제 Unity 메시 · 합성 착석 휴대 자세 · {seated[-1].stem}'))
        sections.append('</div>')
        sections.append('<p class="note">정지 자세의 화면입니다. 최종 손 표면·연속 작도 검사는 손 보고서의 현재 에셋 해시와 결과를 확인해야 하며, 이 화면만으로 완료 처리하지 않습니다.</p>')
    if name=='Locomotion':
        for filename in ('unity_synthetic_motor.json','unity_synthetic_feet.json','unity_validation.json'):
            p=folder/'Reports'/filename
            if p.exists():
                data=json.loads(p.read_text(encoding='utf-8-sig'))
                sections.append('<p>'+link(p,f"{filename} — {data.get('status','검사 원장')}")+'</p>')
        p=folder/'Reports/unity_synthetic_feet.json'
        if p.exists():sections.append(foot_note(json.loads(p.read_text(encoding='utf-8-sig'))))
    if name=='AudioInk':
        p=folder/'InkFeedback/ink_feedback.json'
        if p.exists():
            data=json.loads(p.read_text(encoding='utf-8-sig'))
            sections.append('<p>'+link(p,'실제 먹 증가 이벤트와 붓끝 연결 · 진단 신호 검사')+'</p>')
            for s in data.get('screenshots',[]):
                if s['id']=='02_receiving_ink_at_actual_tip':sections.append(pic(s['path'],'실제 먹 수급 막대와 흡수 연출 · 적 전투 입력 미검증'))
    if name=='Vegetation':
        sections.append('<p>동일 위치 비교. 이전 배치 데이터도 현재 렌더러로 표시한 진단 비교이며 과거 실행 파일의 성능 자료는 아닙니다.</p><div class="grid">')
        for p in sorted((folder/'Screenshots').glob('*.png')):sections.append(pic(p,p.stem))
        sections.append('</div>')
    if name=='Map':
        runs=ROOT/'Art/UIAudio/PaperMapReview/Validation'
        for p in sorted(runs.glob('*/paper_review.json'))[-3:]:
            data=json.loads(p.read_text(encoding='utf-8-sig'))
            sections.append('<p>'+link(p, f"{data.get('runId')} — {data.get('status')}")+'</p>')
            for still in data.get('stills',[]):
                if still.get('id') in ('04_open_flat_100','07_reduced_closed'):
                    q=Path(still['path'])
                    if q.exists():
                        if still['id']=='04_open_flat_100' and data.get('status')=='PASS':
                            sections.append(pic(q,f"실제 펼친 지도 · 합성 UI 조작 검수 · {data.get('runId',p.parent.name)}"))
                        else:sections.append('<p>'+link(q,still['id'])+'</p>')
    sections.append('</section>')
body=''.join(sections)
(OUT/'REVIEW.html').write_text('''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>오행부 플레이테스트 개선 검토</title><style>body{background:#1e2422;color:#e9e3d4;font:16px/1.7 system-ui;margin:0;padding:36px;max-width:1500px;margin:auto}h1,h2{font-weight:600}a{color:#bbd8c2}nav{position:sticky;top:0;background:#1e2422;padding:12px 0}section{padding:24px 0;border-top:1px solid #536056}.grid{display:grid;grid-template-columns:repeat(2,minmax(0,1fr));gap:16px}figure{margin:0}img{max-width:100%;display:block;margin-top:8px}figcaption{font-size:13px;color:#babfad}.note{background:#35403a;padding:16px} @media(max-width:800px){.grid{grid-template-columns:1fr}body{padding:18px}}</style><h1>플레이테스트 개선 검토</h1><p class="note">실제 적용 자료와 진단 결과를 모읍니다. 수치·합성 입력·실제 Game View·사람의 비주얼 판단은 각각 구분합니다. 미검증 항목은 개별 보고서에 기록하며, 영상이나 자동 보행 완주는 제작하지 않습니다.</p><nav><a href="#Hands">A 파지</a> · <a href="#Locomotion">이동</a> · <a href="#AudioInk">먹·소리</a> · <a href="#Vegetation">식생</a> · <a href="#Map">지도</a></nav>'''+body,encoding='utf-8')
print(OUT/'REVIEW.html')
