// 먼지 벽 셰이더 — SPEC-SPELL-FX-REWORK §2·§3.5(모). 로브 메시(SandFront_Mo ×3)의 재질: 「낮게 구르는 모래 벽」의 물성.
// 소비 계약: 메시 축 +Z 높이·+Y 전진·+X 폭, 선단 바닥 피벗, 폭 1.0 정규화(FX-ASSETS §4.2-6) — posOS 도메인은 정규 치수 기준(UV 미사용).
// 색 규약: 색 슬롯은 _BaseColor 하나(코드 SandStormEffect가 팔레트 tint 세팅). _Color는 HasProperty 호환 별칭 — 셰이더는 읽지 않는다(tint² 없음).
//   출력 = _BaseColor × 명도 배율(반 램버트 음영 _ShadeMin~1 · _Value 먹 침전) × 알파 — 색상·채도 불변, 제2 색 출처 없음.
//   알파 = _Opacity × 프레넬 가장자리 침식 × 노이즈 구멍(_Erode — 코드 구동: 형성 1→0.35, 풀림 0.35→1) × 카메라 페이드 × 깊이 페이드.
// 운동: 노이즈 도메인이 _ScrollOS(/s)로 흐른다 — 상단면(xy 도메인)은 전진 방향, 전면(yz 도메인 — 법선 +Y라 xy는 거의 불변)은 아래로 구르는 표면
//   + 상단 정점 워블(_VertexWobble m, 접지선 고정). 결은 스크롤 벡터의 반대로 흐른다(Fbm(p + s·t)) — xy·zw는 독립 도메인이라 4성분 전부 쓴다.
// Emission·Metallic·Smoothness 프로퍼티 없음 — 발광 상한(ART-INK)·무PBR(FX-ASSETS §4.2-4). 텍스처 0(InkStroke 해시 노이즈 이식). 가산 항 없음.
// 패스: DustDepth(SRPDefaultUnlit — ColorMask 0·ZWrite On·같은 알파 clip) → DustColor(UniversalForwardOnly — 프리멀티·ZWrite Off).
//   URP DrawObjectsPass 태그 순서 SRPDefaultUnlit(0) < UniversalForward(1) < UniversalForwardOnly(2)(DrawObjectsPass.cs L91 · InkStroke L12
//   실측 주석 「태그 인덱스 순」)라 깊이가 먼저 깔려 겹치는 로브·오목면의 내부 겹침이 지워진다(실체감). 순서는 감사 컷에서 1회 실측 —
//   뒤집히면 2재질 폴백(Queue 2999 깊이 전용 / 3000 색). 코드는 SetShaderPassEnabled("SRPDefaultUnlit", false)로 프리패스를 끌 수 있다
//   (SetShaderPassEnabled는 패스 Name이 아니라 LightMode 태그를 받는다 — BrushStrokeFeedAdapter L59 선례).
// 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkDust"
{
    Properties
    {
        _BaseColor("Base Color (팔레트 틴트 — 코드 세팅)", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color("Color (호환 별칭 — 미독)", Color) = (1, 1, 1, 1)

        _Opacity("Opacity (최대 불투명도) [TEST]", Range(0, 1)) = 0.85
        _ShadeMin("Shade Min (반 램버트 명도 하한 — 색상·채도 불변) [TEST]", Range(0, 1)) = 0.70
        _RimErode("Rim Erode (프레넬 가장자리 침식 세기) [TEST]", Range(0, 1)) = 0.55
        _RimPower("Rim Power [TEST]", Float) = 2.0
        _NoiseScale("Noise Scale (정규 치수당 결 밀도) [TEST]", Float) = 2.4
        _Erode("Erode (0=온전 → 1=소멸 — 코드 세팅)", Range(0, 1)) = 0.35
        _ErodeSoft("Erode Soft (구멍 가장자리 부드러움) [TEST]", Range(0.01, 0.5)) = 0.12
        _ScrollOS("Scroll OS (xy=xy 도메인 /s · zw=yz 도메인 /s — 결은 스크롤 벡터의 반대로 흐른다: y<0=전진, w>0=전면 하강) [TEST]", Vector) = (0, -0.5, -0.5, 0.35)
        _VertexWobble("Vertex Wobble (상단 정점 흔들림 m) [TEST]", Float) = 0.05
        _WobbleFreq("Wobble Freq (/s) [TEST]", Float) = 1.3
        _WobbleFloor("Wobble Floor (워블 시작 정규 높이 — 접지선 고정) [TEST]", Range(0, 0.5)) = 0.06
        _Value("Value (명도 배율 — 풀림 침전, 코드 세팅)", Range(0, 1)) = 1

        [Toggle(_DEPTHFADE)] _DepthFadeOn("Depth Fade (깊이 텍스처 소프트 접지 — PC 프로파일)", Float) = 1
        _DepthFade("Depth Fade Distance (m) [TEST]", Float) = 0.35
        _CamFadeNear("Cam Fade Near (m — 1인칭 발치 가독) [TEST]", Float) = 0.5
        _CamFadeFar("Cam Fade Far (m) [TEST]", Float) = 1.6
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

        // 기술 상수 — 룩 노브가 아니라 노이즈 도메인 배율·클립 임계(프로퍼티는 Properties 블록이 정본)
        #define NOISE_MIX_XY     0.6   // 전면(xy) 도메인 비중 — 나머지는 측면(yz) 도메인
        #define NOISE_YZ_MUL     1.3   // 측면 도메인 결 밀도 배율
        #define WOBBLE_NOISE_MUL 1.7   // 정점 워블 노이즈 도메인 배율
        #define ALPHA_CLIP       0.004 // 사실상 투명한 프래그먼트 컷(InkStroke §11.1 동류)
        #define DEPTH_CLIP       0.05  // 깊이 프리패스 실루엣 임계 — 옅은 가장자리는 깊이를 쓰지 않는다

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 posWS      : TEXCOORD0;
            float3 nWS        : TEXCOORD1;
            float3 posOS      : TEXCOORD2; // 노이즈 도메인(변위 전 — 침식 패턴이 워블에 끌려다니지 않는다)
            float  viewZ      : TEXCOORD3; // 선형 눈 깊이(m) — 카메라·깊이 페이드
        };

        CBUFFER_START(UnityPerMaterial)
            half4  _BaseColor;
            half4  _Color;       // 별칭 — 미독(SRP Batcher 레이아웃용)
            half   _Opacity;
            half   _ShadeMin;
            half   _RimErode;
            half   _RimPower;
            float  _NoiseScale;
            half   _Erode;
            half   _ErodeSoft;
            float4 _ScrollOS;
            float  _VertexWobble;
            float  _WobbleFreq;
            half   _WobbleFloor;
            half   _Value;
            half   _DepthFadeOn; // 토글 — 키워드가 정본(미독)
            float  _DepthFade;
            float  _CamFadeNear;
            float  _CamFadeFar;
        CBUFFER_END

        // 정수 해시 value noise 2옥타브 — InkStroke.shader L101-121 원문(텍스처 없이 결을 만든다)
        float Hash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float ValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
        }

        float Fbm(float2 p)
        {
            return ValueNoise(p) * 0.6 + ValueNoise(p * 2.13 + 17.7) * 0.4;
        }

        // 정점: 상단(_WobbleFloor 이상)만 월드 법선 방향으로 워블(m) — 매장선(접지)은 떨리지 않는다.
        // 비등방 배율(높이 스케일 애니메이션)은 TransformObjectToWorldNormal이 보정한다
        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
            float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            float hMask = saturate(v.positionOS.z / max(_WobbleFloor, 1e-3));
            float w = (Fbm(v.positionOS.xy * WOBBLE_NOISE_MUL + _Time.y * _WobbleFreq) - 0.5) * 2.0;
            posWS += nWS * (w * _VertexWobble * hMask);
            o.positionCS = TransformWorldToHClip(posWS);
            o.posWS = posWS;
            o.nWS = nWS;
            o.posOS = v.positionOS.xyz;
            o.viewZ = -TransformWorldToView(posWS).z;
            return o;
        }

        // 알파·음영 — 두 패스가 같은 값을 쓴다(깊이 프리패스 실루엣 = 컬러 실루엣)
        half DustAlpha(Varyings i, out half shade)
        {
            half3 N = normalize(i.nWS);

            // 반 램버트 형태 음영 — 명도만(광원색 미곱·그림자 미사용). M_SpellBodyWave 「형태 판독용 음영」 선례의 무PBR 계승
            Light mainLight = GetMainLight();
            half nl = dot(N, half3(mainLight.direction)) * 0.5 + 0.5;
            shade = lerp(_ShadeMin, 1.0, nl);

            // 프레넬 가장자리 침식 — 덩어리 윤곽이 먼지처럼 풀린다(명도 상승 아님 — 알파만)
            half3 V = GetWorldSpaceNormalizeViewDir(i.posWS);
            half fres = pow(1.0 - saturate(dot(N, V)), _RimPower);

            // 노이즈 구멍 — posOS 도메인이 _ScrollOS로 흐른다(xy=상단면 전진 · zw=전면 하강, 게이트 육안 항목). _Erode가 임계를 쓸어올린다
            float t = _Time.y;
            float n = Fbm(i.posOS.xy * _NoiseScale + _ScrollOS.xy * t) * NOISE_MIX_XY
                    + Fbm(i.posOS.yz * _NoiseScale * NOISE_YZ_MUL + _ScrollOS.zw * t) * (1.0 - NOISE_MIX_XY);
            half holes = smoothstep(_Erode, _Erode + _ErodeSoft, n);

            // 카메라 페이드 — 1인칭 발치에서 화면을 덮지 않는다(레티클·텔레그래프 가독)
            half camFade = saturate((i.viewZ - _CamFadeNear) / max(_CamFadeFar - _CamFadeNear, 1e-3));

            // 깊이 페이드 — 지면·벽과 만나는 선이 부드럽게 스민다(PC_RPAsset depth 1에서만 유효)
            half depthFade = 1.0;
            #if defined(_DEPTHFADE)
                float2 uv = GetNormalizedScreenSpaceUV(i.positionCS);
                float raw = SampleSceneDepth(uv);
                float sceneZ = (unity_OrthoParams.w == 0) ? LinearEyeDepth(raw, _ZBufferParams) : LinearDepthToEyeDepth(raw);
                depthFade = saturate((sceneZ - i.viewZ) / max(_DepthFade, 1e-3));
            #endif

            return _Opacity * (1.0 - _RimErode * fres) * holes * camFade * depthFade;
        }
        ENDHLSL

        // 깊이 프리패스 — 로브끼리·오목면의 내부 겹침 제거. 색은 쓰지 않는다
        Pass
        {
            Name "DustDepth"
            Tags { "LightMode" = "SRPDefaultUnlit" }

            ColorMask 0
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            #pragma shader_feature_local _DEPTHFADE

            half4 fragDepth(Varyings i) : SV_Target
            {
                half shade;
                half a = DustAlpha(i, shade);
                clip(a - DEPTH_CLIP);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DustColor"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend One OneMinusSrcAlpha // 프리멀티 — 획·물보라와 같은 합성 계열, 가산 성분 없음
            ZWrite Off
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _DEPTHFADE

            half4 frag(Varyings i) : SV_Target
            {
                half shade;
                half a = DustAlpha(i, shade);
                clip(a - ALPHA_CLIP);
                half3 rgb = _BaseColor.rgb * shade * _Value * a; // 최대 명도 = tint × 1.0 — 백색 포화·네온 불가
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
