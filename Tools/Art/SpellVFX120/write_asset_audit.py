"""Promote explicit visual observations into a source manifest. No source mutation."""
from pathlib import Path
import json,re,collections,struct,zlib,math
from PIL import Image,ImageDraw,ImageStat
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Docs/Art/SpellVFX120'
PREVIEW=ROOT/'Art/SpellVFX120/AssetAudit'
raw=json.loads((OUT/'asset_inventory_raw.json').read_text(encoding='utf-8'))
tex_by_name={Path(x['path']).stem:x for x in raw['textures']}
observations={
2:('태극 중심 방사 꽃판','작도 응축·균형 회복의 작은 중심','바깥 꽃잎은 유지하고 장시간 회전 마법진으로 만들지 않는다'),
3:('모란형 꽃·잎 가지','목 계열 회복·피어남','조밀한 선화이므로 작은 파편보다 한 번 피는 큰 실루엣에 적합'),
5:('눈·코·치아가 과장된 괴수 얼굴','위압·막음의 짧은 얼굴 흔적','정확한 문화재명·도깨비 종류는 고증하지 않았음. 모든 공격에 얼굴을 붙이지 않는다'),
8:('굵은 잎사귀 회전 꽃문','목 회전·잎 소용돌이','흰 채움은 알파 마스크이며 흰 발광 재질을 강제하지 않는다'),
16:('측면으로 펼친 연꽃','회복·정화 바닥 전개','연꽃 실루엣으로 사용. 꽃의 여백을 오라로 채우지 않는다'),
25:('원 안 네 방향 직각 회문','금 방어·각진 봉인 가장자리','문살과 닮은 기하 도안이며 실제 창호 구조로 판정한 것은 아님'),
33:('잎과 줄기가 엮인 원형 당초 띠','목 장벽·속박 흔적','가느다란 내부 선을 과도하게 축소하지 않는다'),
47:('육각 벌집/귀갑형 격자가 채워진 원형 띠','토·금 보호막 분할 경계','실제 거북 등 모델이 아닌 육각 격자 도안 관찰. 지나친 반복은 현대 에너지 쉴드처럼 보일 수 있음'),
58:('여섯 방향 곡선 소용돌이와 점 테두리','회오리·번짐 전환','원형 패턴을 그대로 누워 놓기보다 회전 획의 보조 레이어로 사용'),
66:('눈썹·코·웃는 입을 단순화한 탈 얼굴','짧은 혼란·가호 실루엣','탈 계보와 배역은 미고증. 공포 네온 가면으로 바꾸지 않는다'),
67:('겹잎 연화형 꽃판','정화·회복·목 발화','꽃판 회전보다 접혀 있던 잎이 펴지는 타이밍을 우선'),
86:('새와 긴 잎 가지가 선으로 겹친 원형 장면','목 잔상·바람의 생명 흔적','새/잎 형상이 보이나 종 미확인. 죽엽·대나무로 확정하지 않는다'),
95:('가늘고 촘촘한 방사 선이 채워진 원판','금 방사 파편·부채꼴 확산 외곽','실제 접부채 물건이 아닌 방사 선문. 촘촘한 선의 모아레와 축소 가독성 점검'),
97:('큰 다섯 꽃잎과 점 띠','회복 맥동·작은 보호문','자세한 식물 종은 미확인'),
110:('곡선 화염/당초가 반복되는 열린 테두리','화 확산의 잔상·역번짐 경계','의미 없는 영구 발광 링으로 쓰지 않는다'),
118:('사각 틀 안 길고 둥근 방사 꽃잎 원판','방사·부채꼴 외곽','사각 프레임은 선택적으로 제외. 실제 부채 또는 특정 꽃 종류로 단정하지 않는다'),
126:('네 방향으로 맞물린 직선 띠','금 교차·방벽·부적 선','전통 창호 실물 검증이 아닌 선형 무늬 관찰'),
136:('끝이 안으로 감긴 여덟 방향 곡선 띠','속박 해제·막 전개','정확한 전통 명칭은 단정하지 않음'),
139:('중심이 빈 가늘고 길쭉한 방사 띠','금 찌르기·절단 방사 외곽','부채형 읽기의 단순 선재료. 공업용 톱니/기계 바퀴가 되지 않게 먹 손맛과 결합'),
147:('가늘고 긴 꽃잎의 방사 꽃판','금 파편 방사·잔상','국화형으로 보이나 특정 꽃 종 미고증'),
156:('굵은 겹잎 연화형 원판','회복·보호·목 장벽 중심','축소 가독성이 다른 얇은 선화보다 유리'),
185:('세 갈래 태극 중심과 잎','기운 결합·회수 중심','순수 로고를 모든 술식에 반복해 붙이지 않는다'),
192:('긴 잎과 작은 꽃이 선 난초형 가지','목 회복·새순·정화','굵은 덩굴이나 줄기를 대체할 모델은 아님'),
199:('사슴·나무·해/구름이 있는 원판','보호·지속 생명 연출의 드문 상징','동물명 외 구체적인 십장생 구성과 유물 출처는 미고증'),
200:('가느다란 잎 끝이 퍼진 한 송이 연꽃','회복의 한 번 펼침·상승','얇은 선화. 256px 이하 다량 입자보다 중간 크기 사용'),
204:('겹친 반원 물결이 긴 초승달 띠를 이룸','수 파동·흐름·수면 충격','단독 모티프는 동아시아 공통 어휘. 한국적 색·붓 획과 조합'),
210:('꽃잎이 위로 열린 연꽃과 두 꽃봉오리','정화·생명·흡수','세부 선이 많아 중심 주목 레이어로 제한'),
214:('무늬가 채워진 대칭 나비','목 번짐 전달·회복 잔상','공용 Butterfly의 전기 파랑을 그대로 복제하지 않음'),
225:('삼태극 중심과 팔괘 도안','기운 결합·해제·봉인','팔괘는 방향/음양 의미가 있다. 임의 글자처럼 섞지 않고 드물게'),
226:('큰 세 곡선의 삼태극 원','수 회전·기운 회수','알파 평균 약1.3%의 얇은 선. 실루엣을 채우는 새 먹 획과 결합'),
227:('큰 입·눈·눈썹의 탈/괴면 얼굴','위압·격발 순간의 짧은 얼굴 흔적','정확한 문화재/탈 배역 미고증. 신체 소환 모델 아님'),
228:('점 띠 안 다층 매듭형 꽃원','묶음·봉인·갈무리','금속 네온 마법진 대신 먹 끈이 잠깐 조여드는 표현'),
232:('위가 둥글고 양끝이 안으로 감긴 구름','수·목 상승·시전 잔기·회수','굵고 단순하여 잔상/가벼운 메쉬 파편으로 적합'),
233:('꽃줄기 아래 반복 산봉우리형 선','토 파동·땅의 솟음','문양을 산 지형으로 과장하지 않고 보조 경계로 사용'),
236:('좌우 대칭으로 접힌 구름형 선','막의 닫힘·구름 번짐','사각 외곽은 필요할 때만 쓰고 지속 패널 UI로 만들지 않음'),
247:('삼태극 주위의 잎 띠','갈무리·기운의 되돌림','태극 도형의 읽기를 유지하되 회전 속도를 절제'),
}
selected=[]
for n,(motif,use,caution) in observations.items():
    rec=dict(tex_by_name[f'Pattern_{n}']); im=Image.open(ROOT/rec['path']);stats=ImageStat.Stat(im)
    mats=[m for m in raw['materials'] if rec['path'] in m['texture_paths']]
    mp={m['path'] for m in mats}
    pref=sorted([p for p in raw['prefabs'] if mp.intersection(p['material_paths'])],key=lambda x:x['particle_systems'])
    rec.update(unity_asset_path=rec['path'].removeprefix('Oheangbu/'),visual_status='VISUALLY_REVIEWED_SOURCE_THUMBNAIL',evidence=f'Art/SpellVFX120/AssetAudit/traditional_{((n-1)//50)*50+1:03d}_{((n-1)//50+1)*50:03d}.jpg',observed_motif=motif,recommended_use=use,caution=caution,alpha_mean=stats.mean[3],rgba_extrema=im.getextrema(),recommended_material_work='Reuse alpha coverage, tint RGB with new ink shader. Do not use RGB luminance as mask (RGB is solid white).',source_materials=[dict(path=m['path'],guid=m['guid'],shader=m['shader_path']) for m in mats],source_prefab_examples=[dict(path=p['path'],guid=p['guid'],particle_systems=p['particle_systems'],looping_systems=p['looping_systems']) for p in pref[:2]])
    selected.append(rec)

def fbx_arrays(p):
    b=p.read_bytes(); version=struct.unpack_from('<I',b,23)[0]; wide=version>=7500;header='<QQQB' if wide else '<IIIB';hs=25 if wide else 13;arr=[]
    def prop(pos):
        typ=chr(b[pos]);pos+=1
        if typ in 'YCIFDL':
            fmt={'Y':'h','C':'?','I':'i','F':'f','D':'d','L':'q'}[typ];return struct.unpack_from('<'+fmt,b,pos)[0],pos+struct.calcsize(fmt)
        if typ in 'SR':
            size=struct.unpack_from('<I',b,pos)[0];pos+=4;return b[pos:pos+size],pos+size
        count,enc,size=struct.unpack_from('<III',b,pos);pos+=12;block=b[pos:pos+size];pos+=size
        if enc: block=zlib.decompress(block)
        fmt={'f':'f','d':'d','l':'q','i':'i','b':'?','c':'b'}[typ]
        return struct.unpack('<'+str(count)+fmt,block),pos
    def node(pos,stop):
        while pos+hs<=stop:
            end,count,length,nlen=struct.unpack_from(header,b,pos)
            if not end:break
            name=b[pos+hs:pos+hs+nlen].decode('utf-8');pos+=hs+nlen;props=[]
            for _ in range(count): val,pos=prop(pos);props.append(val)
            if name in ('Vertices','PolygonVertexIndex'):arr.append((name,props[0]))
            node(pos,end-hs);pos=end
    node(27,len(b));return arr
meshes=[];sheets=Image.new('RGB',(1800,400),(48,46,43))
for index,p in enumerate(sorted((ROOT/'Oheangbu/Assets/KoreanTraditionalPattern_Effect/Meshs').glob('*.FBX'))):
    arrays=fbx_arrays(p);vv=[a for name,a in arrays if name=='Vertices'];pp=[a for name,a in arrays if name=='PolygonVertexIndex'];tris=0;faces=[];vertices=[]
    for v,ps in zip(vv,pp):
        offset=len(vertices);vertices.extend([v[i:i+3] for i in range(0,len(v),3)]);f=[]
        for z in ps:
            f.append((z if z>=0 else -z-1)+offset)
            if z<0:faces.append(f);tris+=max(0,len(f)-2);f=[]
    m=Path(str(p)+'.meta').read_text();g=re.search(r'^guid: (\w+)',m,re.M)[1]
    rec=dict(path=str(p.relative_to(ROOT)).replace('\\','/'),guid=g,source_vertices=len(vertices),source_polygons=len(faces),triangles_if_fan_triangulated=tris,bounds_min=[min(v[k] for v in vertices) for k in range(3)],bounds_max=[max(v[k] for v in vertices) for k in range(3)],validation='FBX_BINARY_GEOMETRY_READ; local geometry only, importer transforms not evaluated')
    meshes.append(rec)
    # Two deterministic geometric projections; no material/light/runtime claim.
    cell=Image.new('RGB',(180,400),(48,46,43));draw=ImageDraw.Draw(cell)
    for iy,axes in enumerate([(0,1),(0,2)]):
        xx=[v[axes[0]]+.24*v[(axes[0]+2)%3] for v in vertices];yy=[v[axes[1]] for v in vertices]
        sx=max(xx)-min(xx);sy=max(yy)-min(yy);scale=158/max(sx,sy,1e-7)
        uv=[(10+(x-min(xx))*scale,iy*190+10+(max(yy)-y)*scale) for x,y in zip(xx,yy)]
        for f in faces:draw.line([uv[j] for j in f]+[uv[f[0]]],fill=(177,170,154),width=1)
    draw.text((3,380),p.stem,fill=(248,243,230));sheets.paste(cell,(index*180,0))
sheets.save(PREVIEW/'meshes_geometry_projections.jpg',quality=92)

catalog=json.loads((ROOT/'Docs/Assets/ModelCatalog.reviewed.json').read_text(encoding='utf-8'))
related=[{k:e.get(k) for k in ['path','guid','name','preview','triangles','reviewStatus','notes','brokenMaterials','missingMesh']} for e in catalog['entries'] if e['name'] in ['SM_LineFan','SM_BaeghogiFlag','SM_CheonglyonggiFlag','SM_JujaggiFlag','SM_HyeonmugiFlag','SM_TownFlag1']]
status=dict(schema='oheangbu.spell-vfx120.asset-audit.v1',generated='2026-09-09',scope='Read-only on-disk source and shader graph audit; no live Unity rendering or compile claims.',source_pack='Assets/KoreanTraditionalPattern_Effect',counts=raw['stats'],particle_complexity=dict(min=min(p['particle_systems'] for p in raw['prefabs']),median=sorted(p['particle_systems'] for p in raw['prefabs'])[len(raw['prefabs'])//2],max=max(p['particle_systems'] for p in raw['prefabs']),note='Serialized component counts including inactive children; not measured live particles or drawcalls.'),shader_validation=dict(status='STATIC_URP_TARGET_CONFIRMED_RUNTIME_UNVERIFIED',graphs='All three graphs declare UniversalTarget and UniversalUnlitSubTarget; AlphaBlend two graphs alpha mode0, AdditiveBlend alpha mode2.',external_guid_resolution={'e823cd5b5d27c0f4b8256e7c12ee3e6d':'URP package Runtime/Materials/ParticlesUnlit.mat','933532a4fcc9baf4fa0491de14d08ed7':'URP package Shaders/Lit.shader'},changes='None. No shader repair required by static evidence; actual Unity shader compile/render remains a root-owned check.'),visual_review=dict(source_textures_seen=280,method='Six local thumbnail contact sheets, original RGBA composited onto neutral charcoal. All 250 numbered source motifs and 30 support textures opened; 36 selected motifs described explicitly.',classification_limit='Motif morphology observed; exact cultural artifact/provenance and ethnic exclusivity not asserted.'),recommended_sources=selected,mesh_sources=meshes,related_catalog_sources=related,paid_generation_gap=dict(required_now=False,reason='Cloud, lotus, wave, talisman strips, arcs, streaks and wind/ink surfaces can be assembled from these masks and simple new geometry; none of the currently identified VFX geometry gaps requires a paid generative model.',conditional_gap='A close-up fully three-dimensional culturally specific creature or instrument absent from the current pack could justify Meshy/Blender after a specific spell demands it. Do not replace readable brush geometry with unnecessary high-poly generic fantasy models.'),excluded_from_default_motif_pool=['Pattern_26/183: script/religious-sign-like motif, exact reading not verified','Pattern_52/56/76/78/120/133/134/149/182/211/215/245: character-bearing auspicious or seal motifs; do not assign arbitrary spell meanings','Pattern_162/212: explicit manji/wan-style religious motif; only if intended semantic context supports it','Public Arrow_02 and Tech_*: modern arrow/panel language does not establish Korean aesthetics','Public Ball/Butterfly/Thunder/lighting_00: saturated blue or glossy electric RGB; use independent monochrome coverage only when appropriate'],source_mutation=False,paid_asset_exported=False,evidence_local_only='Art/SpellVFX120/AssetAudit/')
(OUT/'asset_audit.json').write_text(json.dumps(status,ensure_ascii=False,indent=2),encoding='utf-8')
table='\n'.join(f"| {Path(s['path']).stem} | {s['observed_motif']} | {s['recommended_use']} |" for s in selected)
md=f'''# 술식 120 — 한국 전통 무늬 원천 감사

2026-09-09. 원본 에셋과 모델 원장을 읽고 실제 텍스처 280개를 로컬 시트에서 열어 선별했다. Unity 실제 재생·셰이더 컴파일은 이 감사에서 수행하지 않았다. 원본 프리팹·재질·텍스처를 수정하거나 외부 서비스로 업로드하지 않았다.

## 확인 결과

- `KoreanTraditionalPattern_Effect`: **프리팹 250 / 재질 3,079 / 전통 무늬 250 / 공용 텍스처 30 / FBX 10**.
- 직렬화된 ParticleSystem 합계 **11,519**, 프리팹당 **9–124개**, 중앙값 **14개**다. 비활성 자식도 포함한 컴포넌트 수이며 실제 동시 입자·드로콜·프레임 비용으로 바꿔 말하지 않는다.
- ShaderGraph 세 개 모두 **UniversalTarget + UniversalUnlitSubTarget**을 실제 선언한다. AlphaBlend 재질 1,905개, AlphaBlend customdata 267개, Additive 906개, URP Lit 바닥 재질 1개. 정적 자료만으로 핑크색 고장이라고 판정할 근거는 없다. 실제 URP 컴파일과 플레이 결과는 **미검증**.
- 팩 외부 GUID 두 개는 URP 기본 `ParticlesUnlit.mat`, `Lit.shader`로 해소했다. 누락 에셋으로 오진하지 않는다.
- 원본 무늬는 대체로 **RGB 흰색 + Alpha 선화**다. 추천 36종 모두 실제 RGBA 통계를 기록했다. RGB 밝기를 마스크로 쓰면 사각 면 전체가 나온다. 반드시 Alpha를 읽는다.
- 많은 원본은 3천 px 전후이며 Unity 기본 상한 2,048, mipmap OFF다. 원본을 일괄 수정하지 말고 신규 효과의 화면 크기에 맞춘 별도 최적화 도입을 검토한다. 얇은 태극·꽃 선화는 작은 입자에서 사라질 수 있다.

## 재사용할 실제 무늬

정확한 문화재명·시대·민족 독점성까지 고증한 목록이 아니다. 보이는 형태와 사용 맥락을 분리했다. 모든 원본 경로·GUID·원본 연결 재질·가벼운 예시 프리팹은 `asset_audit.json`에 있다.

| 원본 | 실제 관찰 | 추천 위치 |
|---|---|---|
{table}

## 구현 선택

완제품 프리팹의 강한 오라와 여러 번 겹친 파티클을 새 120종의 뼈대로 통째로 복제하지 않는다. **원천 무늬와 단순 메시를 새 먹·담채 연출에 적극 재사용**하고, 동작·실루엣·발생 위치·해소 순서로 술식을 구별한다. 무늬 하나만 바꾼 120개 동그란 마법진은 목표와 다르다.

ART-COLOR의 목 청록·화 주홍·토 황토·금 은백·수 청묵을 중심으로 생죽·쑥·녹청·석간주·연한 황갈·조개빛 회백 등 어울리는 편차를 사용한다. 이 색 이름은 추천 번안이지 새 고증 사실이 아니다. 고채도 강조는 술식 순간에 한정하고 **먹 농도·번짐·획 굵기 → 색의 잔향 → 먹으로 가라앉음** 순서를 유지한다. 에셋의 Additive를 그대로 유지하여 지속 네온으로 만드는 것은 ART-INK와 충돌한다.

한자·팔괘·태극·종교 표식은 실제 의미가 있는 도형이다. 위 목록의 팔괘/태극은 기운 결합·회수에 제한하고, 읽기 미확인 문양·복/수 문자·종교 도형은 일반 공격 풀에서 제외한다. Public의 현대 화살표·UI 패널도 한국적 효과의 중심으로 쓰지 않는다.

## 메시와 주변 팩

10개 FBX의 바이너리 정점·면을 실제 읽었으며 fan 삼각분할 예상치와 두 투영의 wireframe을 남겼다. 이는 원본 로컬 기하 검사이며 Unity 임포트 변환·가시성·애니메이션 검수는 아니다. 모두 일반 오라·띠·방사 표면 계열로 쓰는 가벼운 보조 원천이다.

`ModelCatalog.reviewed.json`의 KoreanTraditionalFestival 85개 항목을 확인했다. 부채(`SM_LineFan`, 4,206 tris)와 사신기·마을기는 기존 시각 검토 자료가 있지만 큰 소품 전체를 빈번한 술식 입자로 생성할 이유는 없다. 실물 소환이 필요한 특정 술식에만 저폴리 파생 후보로 남긴다. SmartMaterials Vol.1/2/3/5는 표면 재질 중심이며 전용 실시간 VFX가 아니다. 이번에는 해당 팩을 새 효과의 검증된 형태로 승격하지 않았다.

현재 식별된 구름·물결·연화·먹 띠·부적·검격·나뭇가지 기하에는 **Meshy 추가 생성이 필수인 공백이 없다**. 이후 특정 술식이 가까이 보이는 완전 입체 신수나 악기를 요구하고 기존 모델이 없다면 그 때 문화적 형태를 명시하여 Blender 또는 Meshy를 사용할 근거가 생긴다. 작은 이펙트 면을 위해 고폴리 범용 판타지 모델을 생성하는 것은 성능과 미감 양쪽에 불리하다.

## 증거와 제한

- 전체 정적 원장: `asset_inventory_raw.json`.
- 명시적 선택과 GUID: `asset_audit.json`.
- 로컬 원본 썸네일 6장과 FBX 기하 투영: `Art/SpellVFX120/AssetAudit/`.
- 원본 시트/유료 콘텐츠는 로컬 검수 전용이며 Git/외부 서비스 배포 대상으로 사용하지 않는다.
- **통과(한정):** 파일/GUID·URP 그래프 선언·텍스처 알파와 실제 보이는 형태 확인.
- **미검증:** 기존 프리팹 전체의 재생 인상, Unity 컴파일/렌더, 시간별 가독성/성능, 역사적 문양 계보.
- **변경 없음:** 모든 원본 에셋. 감사 스크립트와 문서·로컬 미리보기만 추가.
'''
(OUT/'ASSET_AUDIT.md').write_text(md,encoding='utf-8')
print(json.dumps({'selected_sources':len(selected),'meshes':[(Path(m['path']).name,m['triangles_if_fan_triangulated']) for m in meshes],'output':str(OUT/'asset_audit.json')},ensure_ascii=False))
