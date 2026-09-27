"""Snapshot/assemble the woodland detail review, without manipulating Unity UI."""
from pathlib import Path
import hashlib, json, shutil, sys, html

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild'
D=OUT/'Detail261'
C=ROOT/'Oheangbu/Assets/_Project/Art/World/WorldCompact/Rebuild/slice-5e82ecd76d2a'
digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
D.mkdir(exist_ok=True)
if '--before' in sys.argv:
    if (D/'protected-before.json').exists():
        raise SystemExit('Existing baseline retained')
    files=[ROOT/'Oheangbu/Assets/_Project/Scenes/World/W_Demo_Compact.unity',OUT/'Cartography/heights.f32',OUT/'Branches259/route-plan.json']
    files+=list(C.glob('Terrain244/Terrain_*.asset'))
    save=Path.home()/'AppData/LocalLow/DefaultCompany/Oheangbu/world-demo-compact-cave-v4.json'
    files += [save,Path(str(save)+'.bak')]
    (D/'protected-before.json').write_text(json.dumps({str(p):digest(p) for p in files if p.exists()},indent=2),encoding='utf-8')
    for p in [OUT/'art_placements.json',OUT/'Cartography/placements.json',OUT/'Cartography/terrain.png',OUT/'Cartography/region.png',OUT/'Cartography/explored.png',C/'Cartography.png',C/'PaintedTerrain_Cheongrim.png',C/'ExploredTerrain.png']:
        if p.exists():
            dest=D/'BeforeMaps'/p.relative_to(ROOT);dest.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,dest)
    print('Saved terrain, route, canonical scene/save hashes and map backups')
    raise SystemExit

baseline=json.loads((D/'protected-before.json').read_text())
protected={p:Path(p).exists() and digest(Path(p))==h for p,h in baseline.items()}
(D/'protected-after.json').write_text(json.dumps(protected,indent=2))
assert all(protected.values()),'Protected data changed'
audits=[]
for name in ['audit.txt','nav.txt','branch-audit.txt','progression-audit.txt']:
    p=D/name
    if p.exists():audits.append((name,p.read_text(encoding='utf-8-sig')))
walks={}
for mode in ['walk','reverse']:
    p=D/('collision-'+mode+'.json')
    if p.exists():walks[mode]=json.loads(p.read_text())
titles=['산길의 숲 가장자리','물증 주변의 바위·작업 흔적','심부 접근 숲','산허리의 암반과 군락']
cards=''.join(f'<section><h2>{title}</h2><div class="pair"><figure><img src="Detail261/before-{i}.png"><figcaption>이전</figcaption></figure><figure><img src="Detail261/after-{i}.png"><figcaption>적용</figcaption></figure></div></section>' for i,title in enumerate(titles))
checks=''.join(f'<details><summary>{html.escape(n)}</summary><pre>{html.escape(t)}</pre></details>' for n,t in audits)
counts=(D/'build.txt').read_text().replace(' NavMesh rebake/map forest update required.','')+'\n'+(D/'litter.txt').read_text()
walk_text=' / '.join(f'{mode}: {v["status"]} {v["distance"]:.2f}m' for mode,v in walks.items())
page=f'''<!doctype html><html lang="ko"><meta charset="utf-8"><title>청림 산허리와 숲 — 261</title>
<style>body{{margin:40px auto;max-width:1500px;background:#e7e2d5;color:#292c28;font:16px/1.7 sans-serif;padding:0 24px}}h1{{font-size:34px}}h2{{font-size:21px;margin-top:48px}}.pair{{display:grid;grid-template-columns:1fr 1fr;gap:12px}}figure{{margin:0}}img{{width:100%}}figcaption{{font-size:13px;color:#555}}pre{{white-space:pre-wrap}}details{{border-top:1px solid #aaa;padding:12px 0}}@media(max-width:800px){{.pair{{grid-template-columns:1fr}}}}</style>
<h1>청림 산허리와 숲</h1><p>약초길 갈림–물증–심부 주변. 산허리에 묻힌 암반과 너덜, 소나무·활엽수 군락, 숲 가장자리의 낮은 식생, 소규모 작업 흔적을 후보 씬에 배치했다.</p>
<p>기존 산 지형 높이와 길 좌표를 유지한 주변 미술 작업이다. 전체 맵 제작 완료나 새 높이맵 제작을 뜻하지 않는다. 새 충돌에 맞춰 NavMesh를 재생성하고 지도 식생도 같은 원장으로 갱신했다.</p>
<p>1920×1080, 동일 카메라·조명·후처리의 오프스크린 비교. Computer Use·창 전환·Play·입력 조작 없음.</p>
{cards}<h2>구현과 검사</h2><pre>{html.escape(counts)}</pre><p>{html.escape(walk_text)}</p><p>원본 씬·일반 저장·기존 지형 및 경로 보호 해시 {len(protected)}개 일치. 자동 캡슐 이동은 실제 입력 플레이가 아니다. 사람의 길찾기, 이동 중 식생/LOD, CPU·GPU 시간, 최종 미술 판단은 미검증이다.</p>{checks}</html>'''
(OUT/'DETAIL_REVIEW.html').write_text(page,encoding='utf-8')
print('DETAIL_REVIEW.html written; protected files:',len(protected))
