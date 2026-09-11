# KTP 원본 프리팹 재사용 어댑터 — 제작 중

`Vfx120TraditionalMotif`는 KTP의 실제 프리팹 또는 프리팹 내부 연출을 복제하는 독립 컴포넌트다. 현재 `Vfx120Effect`에 연결하지 않았으며 Unity 재생·미술·성능 검증도 아직 하지 않았다. 기존 절차적 메시를 KTP 원본 연출로 위장하는 경로가 아니다.

## 확인한 원본 구조

- `Assets/KoreanTraditionalPattern_Effect/Prefabs`: Bottom 160개, Fly 90개, 총 250개. 프리팹당 PS 9~124개. 패키지 C# 파일 및 프리팹 MonoBehaviour는 모두 0개다.
- Fly 90개에는 각각 Animator가 있다. 컨트롤러 89개는 단일 state, `Animations/Fly2-07.controller`만 2 state이다. 이 예외도 transition·parameter·StateMachineBehaviour 없이 기본 `Fly2-unity-07`을 재생하며 다른 state는 연결되지 않았다.
- 90개 clip 모두 2.3333333초, animation event 0개, Animator update mode Normal이다. 단순 root 회전이 아니라 `Projectile` 위치와 `Projectile`/`Explosion` 활성 상태를 제어한다. `Fly1-01.anim`은 투사체를 1.3333초에 켜고 X -4.806→4.813으로 이동시킨 후 2.3333초에 Explosion을 켠다. 이 시연 시계는 실제 명중 판정의 근거가 아니다.
- 선정된 `Fly01-01.prefab/Explosion`은 PS 13개, 원본 localPosition `(4.822,1.236,0)`이다. `Fly06-01.prefab/Charge`는 PS 9개, `(-5.047,0,0)`이다. 해당 하위 원본을 직접 복제하면 데모 Animator 없이 실제 발동/명중 시점에 age 0부터 원래 PS 연출을 재생할 수 있다.

## 연결 API

```csharp
var motif = host.AddComponent<Vfx120TraditionalMotif>();
var role = Vfx120TraditionalMotif.Role.Impact;
var options = Vfx120TraditionalMotif.Settings.DefaultFor(role);
options.PreviewControlled = preview;
options.Seed = seed;
options.Lifetime = lifetime;
if (motif.Configure(selectedSourceGameObject, pigment, ink, role, options))
{
    // 추출한 하위 연출의 데모 위치만 제거한다. 내부 자식은 그대로 둔다.
    motif.ContentTransform.localPosition = Vector3.zero;
}
// preview에서만: motif.Sample(age);
// 지속 효과를 끝낼 때: motif.Release();
// 소유자가 끝날 때: motif.Clear(); 또는 host 파괴.
```

`Role`은 Cast/Impact/Summon/Shield이고 기본 lifetime은 각각 1.2/1.6/4/4초, fade 0.4초다. 실제 발동·피격·지속 시계와 host 위치/회전/크기는 외부 소유자가 지정한다. 원시 입력·인식·피해·충돌 이벤트를 발행하지 않는다.

- `MaxSystems=32`, `MaxParticles=400`이 기본이다. 시스템을 임의로 잘라내지 않으며 모든 source PS가 들어가지 못하면 Configure가 false와 Diagnostic을 반환한다. 역할별 예외는 명시 설정으로만 받는다.
- 각 PS의 rate/lifetime/burst 수요를 추정해 입자 상한을 배분하고, 해당 PS의 방출량에 같은 비율을 적용한다. 원본 maxParticles 1000을 일괄 비율로 나눠 문양 한 장짜리 burst까지 없애지 않는다. shape, 속도/크기/색/rotation 곡선, delay, burst 시점·반복·확률, 렌더 방식, mesh와 material은 보존한다. 방출 밀도는 예산에 따라 달라지므로 시각 검수가 필요하다.
- 공유 material/mesh/texture 수정 및 material 복제는 없다. 복제 Renderer의 각 material slot에 MPB로 실제 Color 속성만 적용한다. ShaderGraph의 이름 없는 GUID Color 속성도 읽으며 UV/flow Vector에는 색을 곱하지 않는다. 원래 shader와 blend mode를 보존한다.
- 종료 때 방출을 멈추고 pigment에서 ink RGB로 바꾸며 alpha를 줄인다. 끝에는 복제본 전체를 제거한다. Additive 원본은 어두운 먹의 불투명 실루엣을 만들 수 없으므로, 이 경우 감광/소멸 표현이며 미술 PASS가 아니다.
- `Sustain=true`는 원본 loop를 유지하며 Release 후 fade한다. 원래 단발인 하위 PS는 본래대로 끝난다. 단발 소환진/방벽의 특정 시점 hold, 반복 재생을 통한 유지 확장은 아직 구현하지 않았다.
- runtime은 원본 Animator를 유지한다. preview는 단일 원본 clip을 자동 선택하고, 복수 clip controller는 `Settings.PreviewClip`으로 하나를 명시해야 한다. 각 seek는 60Hz로 애니메이션 활성/위치를 먼저 평가한 뒤 native PS를 진행한다. 복잡한 controller 전체의 preview state-machine 재현은 지원하지 않는다. 전체 Fly는 원본 시스템 수와 데모 시계에 맞춘 명시적 예산/수명이 필요하며 선정 subtree가 실제 타격 연결의 우선 경로다.
- 복제 PS stopAction은 None, runtime PS는 게임 timeScale을 따른다. Collider/물리체/AudioSource/Light의 외부 부작용은 복제본에서 막고 MonoBehaviour/누락 스크립트·animation events·외부 sub-emitter/custom simulation space·legacy Animation은 사전 거절한다. KTP 원본에는 package 스크립트나 animation events가 없다.

## 확인과 남은 검수

2026-09-09에 실제 Unity 6000.3.9f1 라이브러리를 참조한 새 C# 파일 단독 정적 컴파일이 오류 0으로 끝났다. 로그는 `C:/Users/yj666/AppData/Local/Temp/ktp-motif-compile-6ozv9a10/compiler.log`에 있다. 라이브 Unity/씬/원본 프리팹 수정은 하지 않았다. `.meta` 생성과 `Vfx120Effect` 통합은 다음 Unity 단계에서 수행한다.

아직 미검증: 실제 PS 수/입자 동시 예산, native burst·sub-emitter의 preview/runtime 일치, MPB 색·fade의 원본 shader별 결과, Animator 활성 전환, 30/60/120fps/감속 종료·자원 회수, C2 가독성 및 CPU/GPU 비용. `CONFIGURED_NOT_VISUALLY_VERIFIED`와 `PREVIEW_SAMPLE_ONLY`는 검사 통과 판정이 아니다.
