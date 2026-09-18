# 에셋 스토어 재임포트 매니페스트

**목적: 유료·서드파티 에셋 스토어 팩은 git에 커밋하지 않는다**(재배포 불가·리포 비대, CLAUDE.md "Odin=퍼블릭 리포 금지"의 확장). 새 클론이나 디스크 복구 시 이 목록을 **에셋 스토어에서 재임포트**해야 프로젝트가 정상 열린다.

`.gitignore`가 이 팩들을 제외한다(2026-09-18 추가). 전부 미추적 상태였으므로 이력 재작성은 없었다.

## 재임포트 없이는 깨지는 것

`_Project`가 아래 12개 팩을 **GUID로 참조**한다. 재임포트 전에는 해당 씬·프리팹에서 핑크 머티리얼·누락 메시가 뜬다. 괄호 = `_Project` 참조 수(2026-09-18 감사).

| 팩 | 참조 | 성격 |
|---|---|---|
| KoreanTraditionalPattern_Effect | 158 | 전통 문양 이펙트 |
| HwaseongHaenggung | 67 | 화성 행궁 건축 |
| HwaseongForteressGate | 35 | 화성 성문 |
| SeyeonjeongPavilion | 28 | 세연정 정자 |
| House_2 | 24 | 한옥 |
| House_1 | 22 | 한옥 |
| Korea_TreasureProps | 16 | 전통 소품 |
| KoreanTraditionalFestival | 9 | 전통 축제 |
| BillemotdonggulLavaTubePack | 8 | 용암 동굴(폐광 활용 추정) |
| KTinteractiveProp | 6 | 상호작용 소품 |
| YongmeoriCoast | 4 | 용머리 해안 |
| JejumokGwana | 2 | 제주목 관아 |

## 참조 0건 (재임포트 우선순위 낮음)

씬에서 GUID 참조가 없어, 재임포트해도 즉시 화면에 나타나지 않는다. 소재·굽기용이거나 미사용.

- KTP_Normal / KTP_Normal_Vol.2 — 노멀맵 세트
- KoreanTraditional_SmartMaterials Vol.1 / 2 / 3 / 5 — Substance 스마트 머티리얼(텍스처 굽는 재료)
- KT_SmartMat_Vol_6 — 스마트 머티리얼
- KTP_Decal — 데칼
- PolyOne, GabrielAguiarProductions — 소품·VFX
- Aura 2 — 볼류메트릭 라이팅 플러그인 (발광 상한 규칙상 방향 재검토 대상)
- Plugins/Sirenix — **Odin Inspector** (에디터 전용, 런타임 핵심은 Odin-독립이어야 함)

## 에디터 도구 (기능용, 씬 무관)

- BuildReport — 빌드 리포트 생성기. 실제 사용 중(`~/Documents/UnityBuildReports/`에 리포트 39개, 최근 2026-09-14).
- Hierarchy Designer — 하이어라키 정리 에디터 확장.

## 유지된 서드파티 (커밋됨)

- `ThirdParty/PDollar` — $P 제스처 인식기. **작도 인식의 핵심**이고 경량·무료이므로 추적 유지.
- `Plugins/NuGet` — MCP 브리지 DLL. 기존 `Oheangbu/.gitignore`가 이미 제외(변경 없음).

## 절차

새 환경에서:
1. 저장소 클론.
2. Unity Hub로 프로젝트 열기(첫 임포트 오래 걸림 — `Library` 재생성).
3. 위 팩들을 에셋 스토어에서 계정으로 재임포트. **참조 12개 팩 우선.**
4. 콘솔의 누락 GUID 경고가 사라지는지 확인.
