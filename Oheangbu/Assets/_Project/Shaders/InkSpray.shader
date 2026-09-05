// 먹 물보라·먼지 파티클 셰이더(오·모 공용) — SPEC-SPELL-FX-REWORK §2·§3.3(오)·§3.5(모). 텍스처 없이 UV로 소프트 디스크를 만들고
// 가장자리를 노이즈로 흔든다: 스트레치 빌보드 = 물보라 실·모래 알갱이 / 수평 빌보드 = 먹 번짐 자국 / 빌보드 = 앞물·먼지 퍼프.
// 색 = 정점색(startColor × colorOverLifetime — 팔레트 tint 파생값) × _Color(백 중립). 프리멀티 합성(InkStroke 계열),
// HDR 부스트 없음(물·먼지는 Bloom을 집지 않는다), Emission 프로퍼티 없음.
// 정점 스트림 계약: 렌더러가 SetActiveVertexStreams([Position, Color, UV, StableRandomX(, AgePercent)])를 걸면
//   TEXCOORD0 = (uv.x, uv.y, stableRandom.x, agePercent) — 연속 스트림이 TEXCOORD0.xyzw로 채워진다(URP ParticlesInput.hlsl L12-15의
//   UV+UV2→float4 TEXCOORD0 패킹과 같은 규칙). 입자별 노이즈 시드 = uv.z — 수명 동안 불변이라 알파 페이드에 따라
//   가장자리가 기어가지 않는다(알파 기반 시드의 플리커 회피). 스트림 미설정이면 uv.z = 0 → 입자 간 동일 패턴(폴백, 플리커 없음),
//   uv.w = 1(정점 요소 결손 기본값 — 모 확장이 꺼져 있으면 미독).
// 모 확장(2026-09-03 — 오 기본값 불변): _ErodeAmount>0일 때만 나이 침식이 곱해진다 — 임계 thr = (_Cutout>0 ? 고정 : lerp(_AgeErode.xy, 나이)),
//   Fbm 찢김 smoothstep(thr, thr+_EdgeSoftness) → 가장자리부터 바스라진다. 카메라 페이드(_CamFadeFar>_CamFadeNear일 때만)·
//   깊이 페이드(_DEPTHFADE 키워드 — PC_RPAsset depth 1)도 기본 끔. M_InkSpray(오)는 새 프로퍼티를 저장하지 않아 출력이 동일하다.
// 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkSpray"
{
    Properties
    {
        _Color("Color (백 중립 — 색은 startColor 경로)", Color) = (1, 1, 1, 1)
        _Softness("Softness (가장자리 부드러움)", Range(0.01, 1)) = 0.35
        _EdgeNoise("Edge Noise (가장자리 요철)", Range(0, 1)) = 0.25
        _NoiseScale("Noise Scale (디스크당 결 밀도)", Float) = 6

        // ---- 모 확장(기본 = 끔 → 오 기본 동작 불변) ----
        _ErodeAmount("Erode Amount (나이 침식 비중 — 0=끔[오 기본] · 1=전면) [TEST]", Range(0, 1)) = 0
        _Cutout("Cutout (0=나이 침식 임계 _AgeErode · >0=고정 임계) [TEST]", Range(0, 1)) = 0
        _EdgeSoftness("Edge Softness (침식 가장자리 부드러움) [TEST]", Range(0.01, 0.5)) = 0.15
        _AgeErode("Age Erode (x=탄생 임계 · y=소멸 임계 — 나이 0→1 보간) [TEST]", Vector) = (0.15, 0.75, 0, 0)
        _CamFadeNear("Cam Fade Near (m — Far≤Near면 끔) [TEST]", Float) = 0
        _CamFadeFar("Cam Fade Far (m) [TEST]", Float) = 0
        [Toggle(_DEPTHFADE)] _DepthFadeOn("Depth Fade (깊이 텍스처 소프트 접지)", Float) = 0
        _DepthFade("Depth Fade Distance (m) [TEST]", Float) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "InkSpray"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend One OneMinusSrcAlpha // 프리멀티 — 획·모티프와 같은 합성 계열
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _DEPTHFADE
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            #define SEED_SPREAD 37.0 // 입자 시드(0..1)를 노이즈 도메인에서 멀리 떨어뜨리는 배율(기술 상수)
            #define AGE_DRIFT   0.3  // 나이 침식 노이즈의 수명 내 표류(도메인 단위 — 기술 상수, 모 확장)

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;     // startColor × colorOverLifetime
                float4 uv         : TEXCOORD0; // xy = UV, z = StableRandomX, w = AgePercent(스트림 미설정 시 1)
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float4 uv         : TEXCOORD0;
                float  viewZ      : TEXCOORD1; // 선형 눈 깊이(m) — 카메라·깊이 페이드(모 확장)
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half  _Softness;
                half  _EdgeNoise;
                half  _NoiseScale;
                half  _ErodeAmount;   // ---- 모 확장(기본 0 = 끔) ----
                half  _Cutout;
                half  _EdgeSoftness;
                half4 _AgeErode;
                float _CamFadeNear;
                float _CamFadeFar;
                half  _DepthFadeOn;   // 토글 — 키워드가 정본(미독)
                float _DepthFade;
            CBUFFER_END

            // 정수 해시 value noise — InkStroke.shader L101-116 원문
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

            // 2옥타브 — InkStroke.shader L118-121 원문(모 확장: 나이 침식 찢김)
            float Fbm(float2 p)
            {
                return ValueNoise(p) * 0.6 + ValueNoise(p * 2.13 + 17.7) * 0.4;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(posWS);
                o.color = v.color;
                o.uv = v.uv;
                o.viewZ = -TransformWorldToView(posWS).z;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 소프트 디스크: 중심 0 → 가장자리 1. 스트레치 빌보드에선 속도 방향으로 늘어난 타원(물보라 실)
                float d = length(i.uv.xy - 0.5) * 2.0;
                float n = ValueNoise(i.uv.xy * _NoiseScale + i.uv.z * SEED_SPREAD) - 0.5;
                float disc = 1.0 - smoothstep(1.0 - _Softness, 1.0, d + n * _EdgeNoise);

                // ---- 모 확장 — 전부 기본 항등(오 출력 불변) ----
                // 나이 침식: 임계가 나이(uv.w)로 오르며 Fbm 결이 가장자리부터 찢긴다(먼지가 바스라짐)
                float thr = _Cutout > 0.0 ? _Cutout : lerp(_AgeErode.x, _AgeErode.y, i.uv.w);
                float e = Fbm(i.uv.xy * _NoiseScale + i.uv.z * SEED_SPREAD + i.uv.w * AGE_DRIFT);
                float erode = smoothstep(thr, thr + _EdgeSoftness, e);
                disc *= lerp(1.0, erode, _ErodeAmount);

                // 카메라 페이드(1인칭 발치 가독) — Far ≤ Near면 끔
                float camFade = _CamFadeFar > _CamFadeNear
                    ? saturate((i.viewZ - _CamFadeNear) / (_CamFadeFar - _CamFadeNear)) : 1.0;

                // 깊이 페이드(지면·벽 접선 소프트) — 키워드 없으면 1
                float depthFade = 1.0;
                #if defined(_DEPTHFADE)
                    float2 uvSS = GetNormalizedScreenSpaceUV(i.positionCS);
                    float raw = SampleSceneDepth(uvSS);
                    float sceneZ = (unity_OrthoParams.w == 0) ? LinearEyeDepth(raw, _ZBufferParams) : LinearDepthToEyeDepth(raw);
                    depthFade = saturate((sceneZ - i.viewZ) / max(_DepthFade, 1e-3));
                #endif

                half a = disc * i.color.a * _Color.a * camFade * depthFade;
                clip(a - 0.004);

                half3 rgb = i.color.rgb * _Color.rgb * a; // 프리멀티 — 가산 성분 없음
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
}
