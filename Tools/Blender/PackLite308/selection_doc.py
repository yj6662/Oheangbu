"""#308 PackLite: write Tools/Unity/Stage308_hanyang/packlite_selection.md from packlite308.json + the manifest entries.
  python Tools/Blender/PackLite308/selection_doc.py
"""
import json
from pathlib import Path
HERE = Path(__file__).resolve().parent
REPO = HERE.parents[2]
OUT = REPO / 'Art/World/PackLite308'
cfg = json.loads((HERE / 'packlite308.json').read_text(encoding='utf-8'))
ent = {}
for jp in OUT.glob('*/*.json'):
    try: e = json.loads(jp.read_text(encoding='utf-8'))
    except Exception: continue
    if 'lods' in e: ent[e['name']] = e

HEAD = '''# packlite_selection — 한양 계획 요소 → 받은 에셋 원본 (2026-10-12, TEST)

편집기 없이(프리팹 YAML · .meta · FBX를 직접 읽어) 고른 목록이다. 경량본 = `Art/World/PackLite308/`(리포 제외), 수치 정본 = 그 안의 `manifest.json`,
짜는 법 = `packlite_compose.md`, 들여오기 = `Art/World/PackLite308/README_import.md`. 다시 만들기 = `python Tools/Blender/PackLite308/run.py`.

## 0. 먼저 읽을 것 (앞선 조사와 달라진 점)

1. **완성된 성문이 있다.** `Assets/HwaseongForteressGate`에는 부품 프리팹 284개뿐이지만, 캐시의 같은 팩(`KCISA - Hwaseong Fortress Gates.unitypackage`) 안
   `Scenes/Demo.unity`가 그 부품 2,539개를 **장안문과 팔달문 두 채로 배치**해 놓았다(프로젝트에는 씬이 안 들어와 있다). 그 배치를 읽어 문루 · 석축 · 옹성 · 적대 · 성벽 구간을 따로 구웠다(3장).
   장안문 문루는 정면 5칸 · 측면 2칸 중층 우진각 — **숭례문 · 흥인지문과 같은 형식**이다.
2. **캐시의 무료 팩 둘이 빈 종류를 메운다.** 창원의 집 = 기와 반가(안채 · 사랑채 · 솟을대문 · 중문 · 협문 · 팔각정 · 담 · 우물), 낙안읍성 = 완성 초가 29채(13종) + 장터 · 살림 소품 약 170종.
   둘 다 프로젝트에는 없다 — `Art/World/PackLite308/_src/`에 풀어서 원본으로만 썼다. **들이는 승인(D308-55)은 사용자 몫**이다(PLAN T9).
3. **여전히 없는 것**: 시전 줄행랑, 종묘 정전 꼴(한 줄 긴 맞배), 중층 정전(근정전), 돌기둥 누각(경회루), 육각 2층 정자(향원정), 돌다리, 해태 · 석수, 사직단, 토담 · 막돌담, 품계석. → 조각(해태)만 빼고 평이한 모듈로 만들었다(`gen/`, 4장).
4. 원본 무게: 전각 한 채 26만 – 186만 삼각형의 6 – 9할이 **기와 낱장**(예: 귤림당 26.3만 중 19.9만, 중양문은 문고리 · 못머리 재질만 12.5만)이다.
   모서리 접기로는 기와가 찢어져서, 기와 재질만 위에서 본 높이장 껍질로 바꾸고 기와 무늬는 텍스처 · 법선으로 구웠다(README 4장).

## 1. 계획 요소 → 원본 → 경량본

크기 = Unity X × Y × Z (m, 경량본 LOD0 실측). 재질 = LOD0 재질 수. 텍스처 = 그 재질들이 쓰는 원본 알베도(1024로 줄여 복사, 지붕은 새로 구운 것).

'''

GATE = '''
## 3. 성문 팩 — 있는 부품과 쌓이는 높이 (Demo 씬 배치 실측, 땅 = 0)

원본 씬 좌표에서 땅은 Y 10.5, 문 축은 X(바깥 = +X), 성벽은 Z로 달린다. 아래 높이는 땅에서 잰 값이다.

| 층 | 부품 프리팹 (개수) | 높이 · 크기 |
|---|---|---|
| 석축(육축) + 홍예 | `SM_G_Arch` 1 · `SM_Fortification` 2 · `SM_G_RoofBoard` 1(홍예 천장) · `SM_G_MetalDoor` 2(철엽문 2.6 × 6.0 m) · `SM_DoorStoper` 1 | 석축 윗면 6.7 m (`SM_Pedestal` 초석이 6.7 m에 놓임). `SM_G_Arch` 7.8 × 7.8 × 13.0 m = 홍예 통로 깊이 13 m |
| 석축 위 여장 · 계단 · 누조 | `SM_GH_Parapet` 22 · `SM_G_Stair` 8 + `SM_G_Stair_Newel` 2 · `SM_Gargoyle` 8 | 여장 밑 6.1 – 7.4 m, 등성 계단 0 → 5.3 m |
| 문루 1층 | `SM_Pedestal` 18 · `SM_Column` 52 · `SM_Lintel` 58 · `SM_FlatLintel` 20 · `SM_ComplexBracket` 84 · `SM_BracketSupport` 84 · `SM_ColumnTopSupport` 100 · `SM_Floor` 23 · `SM_SideDoor` 4 | 마루 7.7 m, 기둥 7.1 → 약 10.5 m, 평면 7.6 × 22.3 m (기둥 중심) |
| 문루 2층 | `SM_Window` 40 + `SM_Window_Frame` 14 · `SM_WoodStair` 2 · `SM_Crossbeam` 28 · `SM_Ceiling` 2 | 마루 12.6 m, 판문 13.1 m |
| 지붕 2단 | `SM_Roof` 8 · `SM_RoofBoard` 16 · `SM_Rafter` 8 · `SM_RafterExtension` 8 · `SM_HipRafter` 16 · `SM_Purlin` 18 · `SM_Ridge` 30 · `SM_RoofStatue` 58 · `SM_Heartwood` 8 · `SM_RammedEarth` 8 | 아래 처마 11.5 m, 위 용마루 21.0 m. 처마 끝 기준 15 × 30 m |
| 옹성 | `SM_BarbicanWall` 28 · `SM_BarbicanParapet` 34 · `SM_BarbicanFloor` 16 · `SM_BarbicanArch` 2 · `SM_B_MetalDoor` 2 · `SM_BarbicanStair` 2 + 옹성 문루 `SM_B_GateHouse*` 15 · `SM_B_SmallArch` 2 | 벽 높이 5.0 m(걷는 길), 여장 위 7.3 m, 반원 안지름 약 38 m(Z −23 – +17), 문 = 가운데 |
| 적대 | `SM_GateGuardPost` 2 · `…_Parapet` 10 · `…_Stair` 2 | 8.6 × 6.8 × 15.0 m, 문에서 Z ±70 m |
| 성벽 | `SM_CW` 97(면석 7.8 × 5.6 m) · `SM_CW_Stone` 77(지대석) · `SM_CW_Parapet` 50(여장 7.8 × 1.7 m) · `SM_CW_Floor` 56 · `SM_CW_Stair` 4 · `SM_RammedEarth` | 모듈 간격 7.75 m, 바깥면 높이 5.1 m, 여장 위 6.9 m, 두께(바깥면 – 안쪽면) 6.2 m |
| 그 밖 | `SM_Cannon` 2 · `SM_Flag_Wind` 3 · 현판 `SM_Signboard_Janganmun` · `…_Paldalmun` | |

- 부품은 각자 원점이 제각각이라(씬 좌표로 놓임) 프리팹만 보고는 쌓을 수 없다. **배치의 정본은 Demo 씬**이고, 풀어 둔 사본은 `Art/World/PackLite308/_src/HwaseongForteressGate_scenes/`.
- 장안문과 팔달문은 부품 구성이 같다(1,265 / 1,274개, 현판과 성벽 굽이만 다름) — 장안문 하나만 구웠다.
- 문루만 떼면 원본 171만 삼각형이다(다포 공포 `SM_ComplexBracket` 84개가 40만, 기와 91만).

## 4. 받은 것이 없어 새로 만든 모듈 (`gen/`)

평이한 덩이 + 미터 UV, 재질 이름만 있다(`PLG_*`). 치수 근거와 EST 표시는 `packlite_compose.md`.

'''

TAIL = '''
## 5. 캐시 팩 조사 (`%APPDATA%\\Unity\\Asset Store-5.x\\`)

| 팩 | 크기 | 안에 든 것 | 여기서 쓴 것 |
|---|---|---|---|
| KHS - Naganeupseong Walled Town Suncheon | 0.89 GB, 프리팹 254 | 완성 초가 29채(주막 2 · ㄱ자집 2 · 마루방집 4 · 향리집 2 · 아전집 4 · 서문 옆 집 3 · 들마루집 4 · 대나무 서까래집 3 · 부르는 창 집 2 · 대장간 · 물레방아 · 연자방아), 벽 · 지붕 · 문 · 창 부품 약 90, 살림 · 장터 소품 약 130(독 · 달구지 · 가마니 · 평상 · 지게 · 음식) | 초가 6종 + 소품 6종. FBX가 가볍다(집 한 채 5만 삼각형 안팎) — **경량본 없이 원본을 그대로 써도 되는 유일한 팩** |
| kcisa - House Of Changwon | 4.5 GB, 프리팹 241 | 완성 기와 건물 13(안채 · 사랑채 · 대문채 · 중문채 · 협문 · 뒷문 · 팔각정 · 정자 · 다목적관 · 민속교육관 · 농기구 전시관 · 방앗간 · 창원 마루), 담 2, 소품 46(우물 · 독 · 백자 · 농기구), 부품 약 180 | 건물 8 + 담 + 우물. 건물은 한 채 = 메시 하나라 풀기 쉽다 |
| KCISA - Hwaseong Fortress Gates | 1.27 GB | 프로젝트에 있는 부품 284 + **Demo · Overview 씬** | 씬 배치만 풀어 씀(3장) |
| kcisa - Korean Traditional Props | 2.0 GB, 프리팹 74 | 악기(북 · 가야금 · 종) · 무기(총통 · 활) · 불상 · 석탑 · 대좌 · 당간지주 | 석조 대좌 1. **해태 · 석수 · 석사자는 없다** |
| KCISA - koreanPeddlerCharacters | 0.65 GB | 보부상 캐릭터 2(FBX 2 · 애니메이션 8) | 행인용 — 이 단계(건물)에서는 쓰지 않음. P5 행인 설계 때 |
| kcisa - Korean Traditional Interactive Props Vol2 | 0.24 GB | 가구 27(장 · 함 · 경대 · 촛대) | 이미 `Assets/KTinteractiveProp`에 있음. 실내 소품이라 쓰지 않음 |

.unitypackage는 `<guid>/{asset, asset.meta, pathname}`의 tar.gz다 — `python -I Tools/Blender/PackLite308/unitypackage.py list|extract`로 편집기 없이 푼다.
풀어 둔 것은 `Art/World/PackLite308/_src/`(5.5 GB, 리포 제외). **Assets나 추적 폴더로 옮기지 않는다.**

## 6. 문제 · 주의

- `Assets/StonePagoda`(석탑 팩)는 모델이 깨져 있다고 기록돼 있어 쓰지 않았다(원각사지 석탑은 PLAN대로 `TemplesKor`의 삼층석탑 대역 — 이 단계에서 굽지 않음).
- 제주목 관아 전각은 조각 120 – 280개를 겹쳐 놓은 프리팹이다. 겹 프리팹의 뿌리 변환 재정의는 풀었고, **뿌리가 아닌 자식의 재정의**(재질 바꿈 · 자식 이동)는 풀지 않는다 — 있으면 실행 기록에 `unresolved non-root overrides`로 남는다(이번 목록에서는 0건).
- FBX 하나에 메시가 여럿이면 프리팹이 가리키는 메시를 이름으로 못 찾을 때 전부 합친다(이번 목록에서는 해당 없음 — 팩 FBX가 전부 메시 하나).
- 정면 방향은 원본 그대로다. 시트로 확인한 것만 아래 표의 "정면" 칸에 적었다.
- 닫힌 방 안쪽 면은 지웠다(밖에서 안 보이는 면). 들어가는 건물이 필요하면 `interior: keep`.
- 창원의 집 · 낙안읍성 · 전통 소품 팩에서 온 경량본은 승인 전에 Assets로 들이지 않는다.
'''


def row(it):
    e = ent.get(it['name'])
    if not e: return '| %s | %s | `%s` | %s | (굽지 못함) | | | |' % (it.get('use', ''), it['name'], it['source'], it.get('ref', ''))
    s = e['bounds_unity']['size']; l0, l1, l2 = e['lods']
    tex = sorted({Path(m['albedo_source']).name for m in e['materials'] if m.get('albedo_source') and '_Roof' not in m['name']})
    return '| %s | **%s/%s** | `%s`%s | %s | %.1f × %.1f × %.1f | %s → %s / %s / %s | %d | %s |' % (
        it.get('use', ''), e['class'], e['name'], it['source'].replace('_src/', '(캐시) '), ' + 거름' if it.get('include') or it.get('box') else '', it.get('ref', ''), s[0], s[1], s[2],
        format(e['source_tris'], ','), format(l0['tris'], ','), format(l1['tris'], ','), format(l2['tris'], ','), l0['materials'], ', '.join(tex[:4]) + (' 외 %d' % (len(tex) - 4) if len(tex) > 4 else ''))


out = [HEAD]
groups = [('궁궐 (경복궁 · 종묘 · 사직)', 'palace'), ('관아 (의정부 · 6조 · 한성부 · 사헌부 · 종각)', 'gwana'), ('성곽 (4대문 · 4소문 · 성벽)', 'fortress'), ('민가 · 저자', 'minga'), ('소품', 'props')]
for title, cls in groups:
    out.append('### %s\n\n| 쓰임(계획 요소) | 경량본 | 원본 | 실제 건물 | 크기 (m) | 삼각형 원본 → LOD0 / 1 / 2 | 재질 | 원본 텍스처 |\n|---|---|---|---|---|---|---:|---|' % title)
    out += [row(it) for it in cfg['items'] if it['class'] == cls]
    out.append('')
out.append('''## 2. 계획 요소 가운데 받은 원본이 없는 것

| 계획 요소 | 받은 팩 · 캐시 팩 확인 결과 | 대신 |
|---|---|---|
| 근정전 (중층 팔작 5 × 5칸) | 중층 정전 없음. 가장 큰 단층 정당 = 봉수당 | `palace/Hall_Bongsudang` + `gen/Gen_Woldae_2Tier`, 실루엣이 필요하면 `gen/Gen_Hall_2Tier_5x5` |
| 종묘 정전 (19칸 맞배 101 m) | 없음. 우화관(41 m)은 가운데가 솟은 객사 꼴 | `gen/Gen_Jongmyo_Jeongjeon` · `Gen_Jongmyo_Bay` + `Gen_Woldae_Jongmyo`. 영녕전 대역 = `palace/Hall_Uhwagwan` |
| 사직단 | 건물이 아님 | `gen/Gen_Sajikdan`(두 단 + 유) + 홍살문 |
| 경회루 (돌기둥 위 7 × 5칸) | 없음. 관덕정은 단층 · 6할 크기 | `gen/Gen_Gyeonghoeru` |
| 향원정 (육각 2층) | 없음 | `minga/Pav_Octagonal`(팔각 2층 누정 — 캐시 팩)이 가장 가깝다 |
| 숭례문 · 흥인지문 | **있음(데모 씬)** | `fortress/Fort_Munru2` + `gen/Gen_GateBase_Large` 또는 `fortress/Fort_GateBase` · `Fort_Ongseong` · `Fort_Jeokdae` |
| 소문 (단층 우진각 3 × 2칸) | 없음(받은 단층 문은 맞배 삼문) | `gen/Gen_GateBase_Small` + `gen/Gen_Munru_1F_3x2` |
| 성벽 · 여장 | 화성 꼴(전돌 여장)만 | `gen/Gen_Wall_City10` · `Gen_Wall_Yeojang10`(본선은 PLAN의 `hanyang308_wall.py`) |
| 시전행랑 | 없음(네 팩 모두) | `gen/Gen_Sijeon_Row8` |
| 기와 민가 | 창원의 집(캐시)에만 | `minga/Giwa_*` + `gen/Gen_Giwa_4kan` · `Gen_Giwa_L` · `Gen_Giwa_Shop3` |
| 솟을대문 | 창원의 집(캐시) 대문채 | `minga/Gate_Soseul_Changwon`, `gen/Gen_Soseuldaemun` |
| 토담 · 막돌담 · 궁장 | 사고석담(행궁) · 창원 담만 | `gen/Gen_Wall_Todam4` · `Gen_Wall_Makdol4` · `Gen_Wall_Gungjang6` |
| 돌다리 (광통교 · 수표교 · 영제교) | 없음(세연정 팩의 다리는 판석 보) | `gen/Gen_Bridge_*` |
| 해태 · 석수 | **없음** | 만들지 않음 — 소환수 메시를 돌 재질로(compose 9장) + `gen/Gen_Pedestal_Haetae` |
| 종 | 유물 팩 범종(박물관 크기) | `props/Prop_Bell`(2.3 m로 키움) |
| 품계석 · 하마비 | 없음 | `gen/Gen_Pumgyeseok` · `Gen_Hamabi` |
''')
out.append(GATE)
out.append('| 모듈 | 크기 X × Y × Z (m) | 삼각형 LOD0 / 1 / 2 | 무엇 (치수 근거) |\n|---|---|---|---|')
for e in sorted(ent.values(), key=lambda e: e['name']):
    if e['class'] != 'gen': continue
    s = e['bounds_unity']['size']
    out.append('| `gen/%s` | %.1f × %.1f × %.1f | %s / %s / %s | %s |' % (e['name'], s[0], s[1], s[2], *[format(l['tris'], ',') for l in e['lods']], e['reference']))
out.append(TAIL)
front = [(e['name'], e.get('front')) for e in ent.values() if e.get('front') and 'as source' not in e.get('front', '') and e['class'] != 'gen']
if front:
    out.append('### 정면 확인표\n\n| 경량본 | 정면 |\n|---|---|\n' + '\n'.join('| %s | %s |' % f for f in sorted(front)))
(REPO / 'Tools/Unity/Stage308_hanyang/packlite_selection.md').write_text('\n'.join(out) + '\n', encoding='utf-8')
print('written', len(ent), 'entries')
