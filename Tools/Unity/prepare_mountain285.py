"""Preserve the prior prototype and acquire the approved CC0 source assets."""
from pathlib import Path
import hashlib, json, shutil, urllib.request, zipfile
from concurrent.futures import ThreadPoolExecutor
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Art/World/Compact/Rebuild/Mountain285'
ASSET=ROOT/'Oheangbu/Assets/_Project/Art/World/MountainTrail285'
OUT.mkdir(parents=True,exist_ok=True)
ASSET.mkdir(parents=True,exist_ok=True)
if not (OUT/'preserved.json').exists():
    paths=set(json.loads((OUT.parent/'Hybrid284/preserved.json').read_text()))
    for base in ['Oheangbu/Assets/_Project/Art/World/HybridMountain284','Oheangbu/Assets/Settings']:
        paths.update(str(p.relative_to(ROOT)).replace('\\','/') for p in (ROOT/base).rglob('*') if p.is_file())
    baseline={p:hashlib.sha256((ROOT/p).read_bytes()).hexdigest() for p in sorted(paths) if (ROOT/p).is_file()}
    (OUT/'preserved.json').write_text(json.dumps(baseline,indent=2))
    with zipfile.ZipFile(OUT/'Recovery.zip','w',zipfile.ZIP_DEFLATED,compresslevel=1) as archive:
        for p in baseline:
            if 'HybridMountain284' in p or p.endswith('.unity') or 'save' in p.lower():archive.write(ROOT/p,p)
note='''<!-- mountain-trail-285:start -->
**절벽 산길 대표 구간 #285 — 구현 중 (2026-09-26):** 사용자 예상도 검토 후 구현 요청. 보행·탐험 중심, 돌길/돌계단과 짧은 잔도, 100±10m/상승18~25m를 별도 씬에서 제작한다. Terrain 기반 산체 + Blender 절벽/암릉 + 구간별 길 메시를 사용한다. 무료 CC0 암석·표면 에셋 사용 승인. #284는 사용자 평가 약20%인 기술 비교본으로 보존하며 미술 승인을 승계하지 않는다. 구현/자동 검사/수동 플레이/CPU·GPU/사용자 미술 판단을 분리한다. 대표 구간 검토 후 약415m 전체 경로 확장. 현재 검증과 미술 승인은 미완료.
<!-- mountain-trail-285:end -->

'''
for rel in ['Docs/BIBLE_INDEX.md','Docs/PROJECT_STATUS.md','Docs/Specs/SPEC-COMPACT-REBUILD.md','Docs/Plans/PLAN-COMPACT-REBUILD-NEXT.md','Docs/Handoff/HANDOFF-COMPACT-REBUILD.md','Docs/DECISIONS.md']:
    p=ROOT/rel;s=p.read_text(encoding='utf-8-sig')
    if '<!-- mountain-trail-285:start -->' not in s:p.write_text(note+s,encoding='utf-8')
spec_path=ROOT/'Docs/Specs/SPEC-MOUNTAIN-TRAIL-285.md'
if not spec_path.exists():spec_path.write_text('''# 절벽 산길 대표 구간 285

승인된 구현 방향: Terrain 하부 산체 + 연속된 큰 파단면의 Blender 절벽 + 독립 돌길/계단/5m 잔도. 보행 탐험 전용이며 전투·저장·정본 이전은 제외한다. 100±10m, 상승18~25m. 일반 폭1.6~2.2m, 협소1.2m 이상, 전망터3~4m. 계단14~18cm/디딤면30cm 이상.

MountainTrailProfile은 거리 순서의 중심선/폭/구간 유형/검토 시점을 소유한다. 여기서 Blender 메시, 식생 제외, 충돌 검사와 NavMesh를 생성한다. 원경 실루엣/중거리 절리/근경 돌 두께와 표면 노멀을 구별한다. Terrain과 암석은 같은 삼축 노멀·조명 표현을 사용한다. 대표 구간은 세 시점과 역방향/접합/계단 보행으로 검토 후 전체 약415m로 확장한다.

CC0 Poly Haven Rock Face03/Roots/Boulder01을 출처·해시 기록 후 취득한다. 큰 산 전체를 작은 바위 확대/반복으로 만들지 않는다. 기존 공유 에셋/씬/설정을 보존하고 새 경로만 재조립한다. 고정 콜라이더와 LOD, 작은 잔해 군집화를 사용한다. 실제 CPU/GPU와120fps 목표는 자동 접지 검사/미술 승인과 구분한다.
''',encoding='utf-8')
headers={'User-Agent':'Oheangbu-MountainTrail285/1.0'}
def fetch(url):
    return urllib.request.urlopen(urllib.request.Request(url,headers=headers),timeout=120).read()
jobs=[]
for slug in ['rock_face_03','roots','boulder_01','weathered_brown_planks','kloofendal_48d_partly_cloudy_puresky']:
    meta=json.loads(fetch('https://api.polyhaven.com/files/'+slug))
    source=OUT/'Sources'/slug;source.mkdir(parents=True,exist_ok=True)
    (source/'files.json').write_text(json.dumps(meta,indent=2))
    if 'hdri' in meta:
        item=meta['hdri']['4k']['hdr'];jobs.append((slug,'hdri',item,ASSET/'Textures'/Path(item['url']).name));continue
    for channel in ['Diffuse','nor_gl','arm']:
        item=meta[channel]['4k']['jpg'];jobs.append((slug,channel,item,ASSET/'Textures'/Path(item['url']).name))
    if slug=='boulder_01':
        item=meta['fbx']['4k']['fbx'];jobs.append((slug,'source_model',item,source/'boulder_01.fbx'))
    if slug=='rock_face_03':
        item=meta['Displacement']['4k']['png'];jobs.append((slug,'height_source',item,source/Path(item['url']).name))
def download(job):
    slug,channel,item,dst=job;dst.parent.mkdir(parents=True,exist_ok=True)
    if not dst.exists() or hashlib.md5(dst.read_bytes()).hexdigest()!=item['md5']:
        data=fetch(item['url']);assert hashlib.md5(data).hexdigest()==item['md5'];dst.write_bytes(data)
    return dict(asset=slug,channel=channel,path=str(dst.relative_to(ROOT)),url=item['url'],license='CC0-1.0',sha256=hashlib.sha256(dst.read_bytes()).hexdigest(),bytes=dst.stat().st_size)
with ThreadPoolExecutor(max_workers=3) as pool:ledger=list(pool.map(download,jobs))
existing=json.loads((OUT/'sources.json').read_text()) if (OUT/'sources.json').exists() else []
ledger += [x for x in existing if x['asset'] not in {item['asset'] for item in ledger}]
(OUT/'sources.json').write_text(json.dumps(ledger,indent=2))
(ASSET/'SOURCE_LICENSES.md').write_text('# Poly Haven CC0 source assets\n\nPowered by Poly Haven: https://polyhaven.com\n\nAsset license: https://polyhaven.com/license (CC0-1.0). Source geometry/textures preserved; local LODs and muted materials are derivatives. No live API in the game.\n\n'+''.join('- https://polyhaven.com/a/'+s+'\n' for s in ['rock_face_03','roots','boulder_01','weathered_brown_planks','kloofendal_48d_partly_cloudy_puresky']),encoding='utf-8')
print('Preserved previous assets; acquired',len(ledger),'verified source files,',sum(x['bytes'] for x in ledger)//1048576,'MiB. Powered by Poly Haven.')
