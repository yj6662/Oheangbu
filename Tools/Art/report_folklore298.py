"""Build the local #298 review from provider receipts and Unity evidence."""
from pathlib import Path
import html
import json

ROOT = Path(__file__).resolve().parents[2]
OUT = ROOT / 'Art/Characters/Folklore298'
ACTORS = [
    ('dokkaebi','도깨비','씨름꾼으로 각색한 도깨비. 뿔·호피를 고정 도상으로 가정하지 않았습니다.','https://encykorea.aks.ac.kr/Article/E0015527'),
    ('agwi','아귀','큰 배와 좁은 목구멍이라는 불교 도상을 바탕으로 한 전투용 각색입니다.','https://encykorea.aks.ac.kr/Article/E0034218'),
    ('changgui','창귀','호환 희생자의 사람형 잔영. 복장과 얼굴은 게임용 창작입니다.','https://folkency.nfm.go.kr/api/file/download/dictionary/301#page=52'),
    ('bulgasari','불가사리','쇠를 먹는 괴생물의 여러 전승 중 돼지형 변이를 선택했습니다.','https://encykorea.aks.ac.kr/Article/E0024904'),
    ('fox_spirit','여우귀물','여우구슬 설화에서 출발한 한 꼬리의 짐승형 외관입니다.','https://encykorea.aks.ac.kr/Article/E0036434'),
    ('imugi','이무기','깊은 물에 사는 큰 뱀을 바탕으로 합니다. 몰락한 황룡 보스와 별개입니다.','https://encykorea.aks.ac.kr/Article/E0039355'),
    ('cheongryong','청룡','Blender 조형 몸통을 Meshy 원형으로 교체하는 후보. 기존 보스 규칙을 유지합니다.',''),
    ('south_gate_general','남문 장수','플레이어 몸체와 장비의 임시 조립물을 대체할 전용 모델입니다.',''),
]


def read(path, default=None):
    return json.loads(path.read_text(encoding='utf-8-sig')) if path.exists() else default


def figure(path, caption, cls=''):
    if not (OUT/path).exists():
        return ''
    return f'<figure class="{cls}"><a href="{html.escape(path)}"><img loading="lazy" src="{html.escape(path)}" alt="{html.escape(caption)}"></a><figcaption>{html.escape(caption)}</figcaption></figure>'


def main():
    ledger = read(OUT/'meshy-ledger.json', {'tasks':[]})
    tasks = {t['name']:t for t in ledger['tasks']}
    spent = sum(t.get('consumed_credits') if t.get('consumed_credits') is not None else t['reserved_credits'] for t in tasks.values())
    status = read(OUT/'review-status.json', {'stage':'제작·검증 중','summary':'Meshy 생성 원본과 Unity 검토 증거를 구분합니다.','checks':[]})
    sections = []
    for actor, label, description, source in ACTORS:
        chosen = actor+'-v2' if actor in ('bulgasari','fox_spirit') else actor
        reference = actor+'_v2' if chosen.endswith('-v2') else actor
        task = tasks.get(chosen,{})
        folder = OUT/'Captures'/actor
        pose = read(folder/'pose-review.json',{})
        images = figure(f'References/{reference}.png','생성 입력 시안')
        unity = f'Captures/{actor}/rest-70.png'
        images += figure(unity,'Unity · 측면') if (OUT/unity).exists() else figure(f'Source/{chosen}/preview_left.png','Meshy 제공 측면 · Unity 검증 전')
        images += figure(f'Captures/{actor}/rest-0.png','Unity · 정면')
        images += figure(f'Captures/{actor}/rest-180.png','Unity · 후면')
        clips = ''
        if folder.exists():
            for p in sorted(folder.glob('*.png')):
                if not p.name.startswith('rest-'):
                    clips += figure(p.relative_to(OUT).as_posix(),p.stem)
        observation = ''
        if pose:
            observation = f'<p class="meta">정적 검사: {pose.get("clips",0)}클립 · {pose.get("samples",0)}포즈 표본 · {pose.get("vertices",0):,}정점. 실제 입력 플레이와 별도 검사입니다.</p>'
        film = ''
        if (folder/'animation.mp4').exists():
            film = f'<video controls preload="none" poster="Captures/{actor}/rest-70.png" style="width:min(100%,640px);display:block;margin:20px 0" src="Captures/{actor}/animation.mp4"></video><p class="meta">Unity 스튜디오 동작 미리보기 · 24fps · 대기 → 이동 → 공격 → 피격 → 경직 → 사망. 월드 이동·충돌 검사는 아래 실행 기록과 구분합니다.</p>'
        sfx_actor = 'jangsu' if actor == 'south_gate_general' else actor
        sounds = ''
        for role, role_label in [('alert','발견'),('windup','공격 준비'),('hit_a','피격 A'),('hit_b','피격 B'),('death','사망')]:
            sound = f'Audio/Prepared/{sfx_actor}_{role}.wav'
            if (OUT/sound).is_file():
                sounds += f'<div><label>{role_label}</label><audio controls preload="none" aria-label="{label} {role_label}" src="{sound}"></audio></div>'
        sounds = ('<details><summary>ElevenLabs 효과음 5개 듣기</summary><div class="sounds">'+sounds+'</div><p class="meta">음량을 조정한 WAV 파생본입니다. 파형·파일 검사를 마쳤으며 사람의 청취 검토는 별도입니다.</p></details>') if sounds else ''
        sections.append(f'''<section id="{actor}"><div class="heading"><h2>{label}</h2><span>{'기존 보스 교체' if actor in ('cheongryong','south_gate_general') else '신규 일반 적'}</span></div>
<p>{description} {'<a href="'+source+'">전승 자료 ↗</a>' if source else ''}</p>
<div class="gallery">{images}</div>{film}{sounds}{observation}
{('<details><summary>애니메이션 표본 보기</summary><div class="motion-gallery">'+clips+'</div></details>') if clips else '<p class="meta">Unity 동작 표본 대기</p>'}
<p class="meta">Smart Topology · meshy-t2 · 작업 <code>{html.escape(task.get('id','미제출'))}</code></p></section>''')
    checks = ''.join(f'<li>{html.escape(x)}</li>' for x in status.get('checks',[]))
    navigation = ''.join(f'<a href="#{id}">{label}</a>' for id,label,_,_ in ACTORS)
    runtime = read(OUT/'Runtime/checks.json', {})
    play = ''
    if runtime:
        photos = ''.join(figure(p.relative_to(OUT).as_posix(), p.stem + (' · 인접 벽이 시야를 가리는 카메라 한계가 남아 있습니다' if p.stem == 'folklore298_imugi-attack' else '')) for p in sorted((OUT/'Runtime').glob('*.png')))
        rows = ''.join('<tr>'+''.join(f'<td>{html.escape(str(x))}</td>' for x in (r['id'].replace('folklore298/',''),r.get('alerts',0),r.get('telegraphs',0),r.get('impacts',0),round(r.get('damage',0),2),'통과' if r.get('restored') else '미완료'))+'</tr>' for r in runtime.get('actors',[]))
        audio_rows = ''.join('<tr>'+''.join(f'<td>{html.escape(str(x))}</td>' for x in (r['id'].replace('folklore298/',''),r.get('audioPlayed',0),r.get('audioFallback',0),r.get('audioDropped',0),r.get('audioStatus','미확인')))+'</tr>' for r in runtime.get('actors',[]))
        play = f'<section id="runtime"><h2>후보 맵 실행 기록</h2><p>상태: <strong>{html.escape(runtime.get("status","미확인"))}</strong> · 검사 {len(runtime.get("checks",[]))}개 · 실패 {len(runtime.get("failures",[]))}개. 접근 이동은 가상 WASD를 기존 PlayerMotor에 넣었고, 감지·추적·공격은 실제 AI를 사용했습니다. 피격·경직·사망은 전투 API로 유발했습니다.</p><p>보스 처치, 사람의 직접 조작, 음원 청취, 사용자 미술 승인은 포함하지 않습니다. <a href="Runtime/checks.json">전체 실행 기록</a></p><div style="overflow:auto"><table><thead><tr><th>적</th><th>감지</th><th>공격 예고</th><th>타격 판정</th><th>피해</th><th>휴식 복원</th></tr></thead><tbody>{rows}</tbody></table></div><div class="motion-gallery">{photos}</div></section>'
        play += f'<section id="runtime-audio"><h2>실행 중 효과음 연결</h2><p>40개 클립의 Unity 디코딩과 8종 프로필 연결을 검사했습니다. 아래 수치는 실제 발견·공격 준비·피격·사망 이벤트에 대해 오디오 풀이 받아들인 재생 요청입니다. 스피커 출력 녹음이나 사람의 청음을 뜻하지 않습니다.</p><div style="overflow:auto"><table><thead><tr><th>적</th><th>생성 음원</th><th>공통 대체음</th><th>거부</th><th>상태</th></tr></thead><tbody>{audio_rows}</tbody></table></div><a href="Analysis/audio-import.json">Unity 음원 검사</a></section>'
    source_links = [('Inventory/INVENTORY.md','기존 적·보스 전수 조사'),('../../../Docs/Research/folklore-enemies-298.md','전승 조사'),('meshy-ledger.json','Meshy 사용 원장'),('import-manifest.json','후보 제어 데이터'),('../../../Docs/Handoff/HANDOFF-FOLKLORE-298-20260927.md','최종 기록·한계')]
    # Docs lives three directories above Art/Characters/Folklore298.
    footer = ' · '.join(f'<a href="{href}">{label}</a>' for href,label in source_links)
    content = f'''<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>오행부 #298 · 민속 요괴</title>
<style>*{{box-sizing:border-box}}html{{scroll-behavior:smooth}}body{{margin:0;background:#ece8df;color:#252a29;font:16px/1.65 "Malgun Gothic",sans-serif}}header,main,footer{{max-width:1320px;margin:auto;padding:34px 30px}}header{{padding-top:64px}}h1{{font-family:serif;font-weight:500;font-size:44px;margin:12px 0}}h2{{font-family:serif;font-weight:500;font-size:30px;margin:0}}p{{max-width:950px}}a{{color:#355e5b;text-underline-offset:4px}}nav{{display:flex;flex-wrap:wrap;gap:12px 24px;padding:16px 0}}.eyebrow,.meta{{font-size:13px;color:#616b68}}.status{{padding:16px 20px;border-left:3px solid #62746a;background:#f4f1e9}}section{{padding:38px 0 45px;border-top:1px solid #bebfb6;scroll-margin:10px}}.heading{{display:flex;align-items:baseline;justify-content:space-between;gap:20px}}.heading span{{font-size:13px;color:#616b68}}.gallery{{display:grid;grid-template-columns:repeat(auto-fit,minmax(220px,1fr));gap:16px;align-items:start}}figure{{margin:0;background:#202829}}figure img{{display:block;width:100%;aspect-ratio:1;object-fit:contain}}#runtime figure img{{aspect-ratio:16/9}}#runtime .motion-gallery{{grid-template-columns:repeat(auto-fit,minmax(350px,1fr))}}figcaption{{padding:7px 12px;background:#dfded5;color:#454d49;font-size:12px}}.motion-gallery{{display:grid;grid-template-columns:repeat(auto-fit,minmax(230px,1fr));gap:12px;margin:20px 0}}summary{{cursor:pointer;padding:10px 0;color:#355e5b}}.sounds{{display:grid;grid-template-columns:repeat(auto-fit,minmax(260px,1fr));gap:16px;margin:16px 0}}.sounds label{{display:block;font-size:13px}}audio{{width:100%}}table{{border-collapse:collapse;margin:20px 0;width:100%}}th,td{{text-align:left;padding:9px;border-bottom:1px solid #bebfb6}}code{{font-size:11px;overflow-wrap:anywhere}}footer{{border-top:1px solid #bebfb6;font-size:13px;padding-bottom:70px}}@media(max-width:650px){{header,main,footer{{padding-left:18px;padding-right:18px}}h1{{font-size:32px}}.gallery{{grid-template-columns:1fr 1fr}}.heading{{display:block}}}}</style>
<header><div class="eyebrow">오행부 · FOLKLORE 298 · {html.escape(status['stage'])}</div><h1>한국 민속 요괴와 두 보스</h1><p>{html.escape(status['summary'])}</p><div class="status"><strong>Meshy {spent:g}크레딧</strong> · 신규6종 + 청룡·남문 장수 교체 후보 · 재생성 이력 보존<ul>{checks}</ul></div><nav>{navigation}</nav></header><main>{play}{''.join(sections)}</main><footer>{footer}<p>시안·생성 원본·정적 Unity 검사·실제 플레이·사용자 미술 판단을 각각 기록합니다. 전승에 없는 외형과 전투 방식은 게임용 각색입니다.</p></footer></html>'''
    (OUT/'REVIEW.html').write_text(content,encoding='utf-8')
    print(OUT/'REVIEW.html')


if __name__ == '__main__':
    main()
