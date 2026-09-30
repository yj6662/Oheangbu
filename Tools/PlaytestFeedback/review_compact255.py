from pathlib import Path
import json,html,os
root=Path('C:/Users/yj666/Oheangbu');out=root/'Art/World/Compact/Rebuild/NPC_AUDIO_REVIEW.html'
audio=root/'Art/Audio/Compact255';chars=root/'Art/Characters/Principal255'
h=['<!doctype html><html lang="ko"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>주요 인물 · 효과음 검토</title><style>body{margin:0;background:#ece8df;color:#272922;font:16px/1.7 system-ui,sans-serif}main{max-width:1120px;margin:60px auto;padding:0 24px}h1{font-size:36px;font-weight:600}h2{margin-top:56px;font-weight:600}p{max-width:880px}.models{display:grid;grid-template-columns:1fr 1fr;gap:28px}img{width:100%;display:block}figure{margin:0}figcaption{padding:12px 0}.poses{display:grid;grid-template-columns:repeat(3,1fr);gap:10px}.sound{display:grid;grid-template-columns:repeat(3,1fr);gap:20px}.sound article{border-top:1px solid #bab7ac;padding:14px 0}audio{width:100%}details{margin:20px 0}summary{cursor:pointer;font-weight:600}pre{white-space:pre-wrap;font:13px/1.6 monospace;background:#dedbd2;padding:20px}a{color:#3d5348}@media(max-width:700px){.models,.sound{grid-template-columns:1fr}}</style><main><p>2026.09.24 · #255</p><h1>주요 인물과 효과음</h1><p>이전 EA·시스템 자동 구현은 중단했습니다. 호송 #252까지의 검증 기록을 보존하며, 남문 #253은 마지막 회전 수정 뒤 새 물리·Play 검증을 하지 않은 상태입니다. <a href="SouthGate253/STOP_REPORT.md">중단 보고서</a></p><h2>왕소 · 정담</h2><p>Meshy 7.1 모델·텍스처·리그와 native 걷기/달리기를 생성했습니다. 아래는 생성 리그의 보행 샘플입니다. 팔이 들린 자세가 남아 현장 NPC 교체는 보류했습니다. 사용자 미술 승인 및 게임용 리그 통과 상태가 아닙니다.</p><div class="models">']
for id,label in [('wangso','왕소 — 상인 위장'),('jeongdam','정담 — 병조 도사')]:
 h.append(f'<figure><img src="../../../Characters/Principal255/{id}/walk_12.png"><figcaption>{label}</figcaption><details><summary>다른 보행 시점</summary><div class="poses">'+''.join(f'<img src="../../../Characters/Principal255/{id}/walk_{f}.png">' for f in [1,12,24])+'</div></details></figure>')
h.append('</div><h2>새 효과음 69종</h2><p>UI·작도·전투·이동·상호작용·환경을 나눠 생성했습니다. 고역과 순간 피크를 줄이고 루프 접합부를 다듬었습니다. 아래는 게임 믹서 이전 파일이며, 현장에서는 거리·차폐·설정 음량이 추가로 적용됩니다.</p>')
rows=json.loads((audio/'inventory.json').read_text());analysis={x['id']:x for x in json.loads((audio/'analysis.json').read_text())}
labels={'core':'작도 · 전투 · 기본 상호작용','ui':'UI · 장비 · 발견','movement':'보행 · 차량','world':'문 · 화물 · 전투 상태','ambience':'환경'}
for group,label in labels.items():
 rs=[r for r in rows if r['group']==group];h.append(f'<details><summary>{label} — {len(rs)}종</summary><div class="sound">')
 for r in rs:
  ident=r['id'];rel=os.path.relpath(root/f'Oheangbu/Assets/_Project/Audio/Compact255/{ident}.wav',out.parent).replace('\\','/')
  h.append(f'<article><strong>{ident}</strong><br><small>{analysis[ident]["seconds"]:.2f}초 · peak {analysis[ident]["peak_db"]:.1f}dBFS</small><audio controls preload="none" src="{rel}"></audio></article>')
 h.append('</div></details>')
h.append('<h2>검증 기록</h2><p>구매·장착·강화 및 저장 실패/잘못된 드롭, 보행·동행 관찰, 일시정지와 출력 검사를 통과했습니다. 마지막 음소거 캡처는 319,488샘플의 peak/RMS가 0이었습니다. 이전 무효 시도와 검사 기준 수정도 아래 원문에 보존했습니다.</p><p>자동 UI handler·저장 실패·물리 이동·DSP 출력 검사와 실제 연속 플레이·사용자 청감 판정을 구분합니다. 모든 음원 생성이 모든 자연 플레이 사건의 통과를 의미하지 않습니다.</p>')
for p,label in [(audio/'runtime-checks.txt','이번 자동 검사'),(chars/'unity-import.txt','Unity 모델 가져오기')]:
 h.append('<details open><summary>'+label+'</summary><pre>'+html.escape(p.read_text(encoding='utf-8-sig') if p.exists() else '검증 진행 중')+'</pre></details>')
h.append('<p><a href="../../../Audio/Compact255/attachment.txt">효과음 참조 목록</a> · <a href="../../../Audio/Compact255/COVERAGE.md">필요 지점과 연결표</a> · <a href="../../../../Docs/Specs/SPEC-PRINCIPAL-NPC-SFX-255.md">작업 범위와 남은 검증</a></p></main></html>')
out.write_text(''.join(h),encoding='utf-8')
print(out)
