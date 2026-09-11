# KTP 완성 프리팹 재사용 후보 16개

**원본 250개 프리팹의 실제 계층·ParticleSystem·재질 연결을 조사하고, 타격·발동·소환진·방어막 전환에 각 4개를 선별했다.** 이번 선택은 문양 텍스처만 꺼내 쓰는 방식이 아니다. 원래 프리팹의 여러 파티클 층, 메시, Noise/Trail, 크기·회전·색 곡선과 Animator를 함께 활용한다. 아직 Unity에서 후보를 렌더하지 않았으므로 외형 합격이나 성능 통과를 뜻하지 않는다.

[실행용 선택 JSON](KTP_REWORK_SELECTION.json)의 `selected`에 정확한 `prefabPath`, `reuseRoot`, GUID, 활성 계층, 파티클별 duration/loop/playOnAwake/Emission, 재질·메시 경로, 곡선 진단 표본, Animator·클립 경로가 있다. `inventory`에는 250개 원본의 경로·해시·활성 계층 요약이 있다.

## 우선 촬영할 네 후보

1. [Fly01-01](../../../Oheangbu/Assets/KoreanTraditionalPattern_Effect/Prefabs/Fly/Fly01-01.prefab)의 `Explosion`: 회전 잎 문양 Pattern_8, 여러 Aura Trail, 두 방사선이 결합한 타격.
2. [Fly06-01](../../../Oheangbu/Assets/KoreanTraditionalPattern_Effect/Prefabs/Fly/Fly06-01.prefab)의 `Charge`: Star와 두 RingEnergy Trail, 광점의 집중형 발동.
3. [Bottom12-01](../../../Oheangbu/Assets/KoreanTraditionalPattern_Effect/Prefabs/Bottom/Bottom12-01.prefab) 전체: 여러 하위 Aura 층과 Pattern_147, Light_02.FBX를 가진 다층 소환진. 먼저 간결한 비교본이 필요하면 Bottom03-01을 함께 본다.
4. [Bottom04-01](../../../Oheangbu/Assets/KoreanTraditionalPattern_Effect/Prefabs/Bottom/Bottom04-01.prefab) 전체: 실제 Aura.FBX를 사용한 입체 외곽과 Pattern_51. 방어막으로 전환할 수 있는지 볼 첫 후보.

**Fly 원본 첫 촬영은 Animator를 유지한다.** 확인한 선택 8종의 원본 클립은 2.333초이며 `Projectile` 위치와 `Projectile/Explosion`의 활성화를 제어한다. Projectile 활성 키는 1.333초, Explosion 활성 키는 2.333초다. 1초까지만 보거나 PS만 일괄 Simulate하면 완제품의 폭발을 놓칠 수 있다. 원본 전체는 0.3·0.8·1.4·2.0·2.4·2.7·3.2초를 우선 확인한다. 이후 검수 복제본에서 해당 하위 연출을 분리하되 원래 자식 계층·재질·파티클 곡선을 보존한다. 실게임에서는 예시 투사체 타이밍을 실제 명중 판정으로 오인하지 않는다.

## 전체 선택표

아래 경로의 공통 접두사는 `Assets/KoreanTraditionalPattern_Effect/Prefabs/`다. `활성/Emission`은 **해당 재사용 하위 계층의 ParticleSystem 컴포넌트 수 / Emission을 켠 시스템 수**다. 실제 동시에 살아 있는 입자 수는 Unity를 실행하지 않아 미측정이다. duration은 PS 설정이며 실제 화면의 지속 시간이나 Animator 길이와 같지 않다.

| 분류 | 프리팹 · 재사용 계층 | 활성/Emission | PS duration | 연결·곡선에서 확인한 구성 |
|---|---|---:|---|---|
| 타격 | `Fly/Fly01-01.prefab` · Explosion | 13/12 | 전부 5초 | Pattern_8, 여러 Aura Trail, 두 방사선. Line_00 크기는 정규화 수명 0→0.0698에서 0→1로 급격히 커지고 끝에 0으로 감소. |
| 타격 | `Fly/Fly03-01.prefab` · Explosion | 17/15 | 전부 5초 | Pattern_50, RingWave, Aura·Flare. Impact_Ring이 별도로 성장하는 원형 충격. |
| 타격 | `Fly/Fly07-01.prefab` · Explosion | 13/11 | 전부 5초 | Pattern_162, ParTech 두 층, 두 RingEnergy Trail과 Aura. 각형 파편의 실제 미감은 렌더 후 판정. |
| 타격 | `Fly/Fly10-01.prefab` · Explosion | 11/10 | 전부 5초 | Pattern_250, ShockWave_00/01, 방사선과 Aura. 이중 파면을 중심으로 활용. |
| 발동 | `Fly/Fly05-01.prefab` · Charge | 6/5 | 1·5초 | line.FBX와 회전, LineLight의 속도·노이즈, 광점. 간결한 선형 개시 후보. |
| 발동 | `Fly/Fly06-01.prefab` · Charge | 9/6 | 1·5초 | Star, 두 RingEnergy Trail, 크기·속도 곡선의 집중 구조. |
| 발동 | `Fly/Fly08-01.prefab` · Charge | 14/12 | 1·5초 | energy_ring 네 겹과 Flare, LineLight. 여러 겹으로 형성되는 개시. |
| 발동 | `Fly/Fly09-01.prefab` · Charge | 10/7 | 1·1.4·5초 | Lightning UV 애니메이션, Star와 RingEnergy. 번개 표현이 맞는 술식에 한정. |
| 소환진 | `Bottom/Bottom03-01.prefab` · 전체 | 9/8 | 3~5초 | Pattern_38 회전, 세 Aura의 속도·Noise·Trail, 추가 Aura와 방사점. 비교적 간결한 후보. |
| 소환진 | `Bottom/Bottom05-01.prefab` · 전체 | 11/10 | 3~5초 | Pattern_61, Fire_01의 UV 애니메이션, Fx_Light_02.FBX와 Aura Trail. |
| 소환진 | `Bottom/Bottom12-01.prefab` · 전체 | 25/20 | 3~5초 | Pattern_147, Aura_00/01/02/03의 여러 하위층, Light_02.FBX. 후보 중 구성 비용이 높은 편이므로 실제 입자·오버드로 확인 필요. |
| 소환진 | `Bottom/Bottom18-01.prefab` · 전체 | 16/15 | 3~5초 | Pattern_225, Par_Tech의 Tech_00~03, Aura와 Light_03.FBX. 각형 조각과 원진 구성. |
| 방어막 전환 | `Bottom/Bottom04-01.prefab` · 전체 | 9/8 | 2.9~5초 | Aura.FBX, Pattern_51, 두 Noise/Trail Aura. 메시 외곽과 문양 기반. |
| 방어막 전환 | `Bottom/Bottom09-01.prefab` · 전체 | 10/9 | 3~5초 | Fx_Light_03.FBX가 Light/Aura_03 두 층에 연결, Pattern_125. 광면 두 겹의 구성. |
| 방어막 전환 | `Bottom/Bottom14-01.prefab` · 전체 | 14/13 | 3~5초 | Light_01.FBX 세 면과 Light_03.FBX 한 면, Pattern_175. 여러 메시 층의 성장 곡선. |
| 방어막 전환 | `Bottom/Bottom15-01.prefab` · 전체 | 9/8 | 3~5초 | Fx_Light_01.FBX, Pattern_188, Aura와 광점. 단일 주 메시의 간결한 후보. |

선택 이름의 형태 설명은 실제 직렬화된 메시·파티클·재질과 곡선에 근거한다. 아직 열람하지 않은 최종 렌더를 본 것처럼 돔·꽃·방패의 구체적 실루엣을 확정하지 않았다. 특히 **패키지에 완성 Shield 프리팹이 있다는 뜻이 아니다.** 방어막 4개는 기존 입체 연출을 확인한 후 지지 면의 방향과 유지·해제를 연결할 후보다.

## 조사에서 확인한 실행 주의점

- 250개는 **Bottom 20계열×8변형=160**, **Fly 10계열×9변형=90**이다. 전 파일의 GO 활성 상태, 부모 경로, PS, Renderer, 재질·메시 연결을 읽었다. 최종 선택은 한 계열의 색 변형 8개를 서로 다른 연출로 세지 않고 16계열에서 하나씩 택했다.
- Fly 전체에는 비활성 투사체 변형과 보조 계층도 포함된다. 예를 들어 Fly01-01은 전체 PS 115개지만 직렬화된 활성 계층은 35개, 선택 Explosion은 13개다. 실제 프레임 비용은 이 숫자나 maxParticles 합계로 확정할 수 없다.
- 선택한 **Emission 활성 시스템 자체는 모두 nonloop**다. 표의 일부 루트·Flare·Impact_RingEnergy에 `loop=true`가 있지만 Emission이 꺼진 제어용 PS다. Bottom 원본도 루트 loop만 켜져 있다. 숨은 루트나 Emission을 무작정 켜서 재생 문제를 해결하지 않는다.
- 모든 선택 원본의 startLifetime·startSize·startSpeed, 실제 m_Bursts와 size/color/rotation 모듈의 진단 표본은 JSON에 남겼다. 표본 키는 max/min 곡선 일부를 함께 포함하므로 JSON만으로 새 커브를 복원하지 않는다. **전체 원본 프리팹/클립을 재사용하는 것이 기준**이다.
- 선택 재질은 실제 `AdditiveBlend_Scroll`, `AlphaBlend_Scroll`, `AlphaBlend_Scroll_customdata` Shader Graph에 연결된다. 확인한 Additive Graph에는 UniversalTarget/UniversalUnlitSubTarget이 있다. 이 사실만으로 현재 Unity에서 핑크·과노출·CustomData가 정상이라고 통과시키지 않는다. 원래 재질과 CustomData 스트림을 함께 렌더해야 한다.
- `e823cd5b5d27c0f4b8256e7c12ee3e6d` 재질 GUID는 Assets 안에서 찾지 못했다. 선택본에서는 Emission이 꺼진 제어 PS들에만 연결된다. 렌더에 쓰이는 자식 재질 연결과 구분하고, 이 제어 PS를 강제로 활성화하지 않는다.

## 상태

**완료:** 250개 원본 정적 조사, 16개 실행 후보 선별, 정확한 하위 경로·재질·메시·애니메이션 연결 기록. **미검증:** 실제 프리팹 렌더, 한국적 미감의 최종 선택, 라이브 입자 수·최대 오버드로·성능, 방어막 지속·해제 전환, 실게임 이벤트 연결. 소환수 Meshy 제작과 게임 규칙 변경은 이번 하위 작업 범위에 포함하지 않았다.

원본 에셋·Unity·앱 소스는 변경하지 않았다. 작성 파일은 이 보고서와 `KTP_REWORK_SELECTION.json` 두 개다.
