// 불혀 실체 셰이더 — SPEC-SPELL-FX-REWORK §2·§3.1.1 (노 — InkFlame).
// 화염을 빛(가산 빌보드)이 아니라 먹 담채의 「불꽃 한 줄기」 실체로 그린다: 불투명 AlphaTest·clip 침식이라
// 경계가 원리적으로 선다(가산 포화 없음). 무텍스처 · _EmissionColor 프로퍼티 자체 없음 · 무PBR(Metallic/Smoothness/큐브맵 없음).
// 출력 = _BaseColor(팔레트 틴트 — 코드 주입 슬롯) × 명도 배율(반램버트·림 먹 테두리·뒷면·먹 침전)뿐.
//   시간 변화는 명도(틴트→먹)와 침식뿐 — 채도 무변경. 점화 창(_Age<_IgniteWindow)만 명도 +_IgniteLift(ART-INK-LOOK §4.1 +10~15%)
//   + 틴트 가산 _IgniteAdd(§4.2 addK ≤0.25 — 주홍 R만 0.12s 동안 threshold 1.0을 스침, G/B<1이라 백·황 포화 불가).
// 메시 규약(FX-ASSETS §4.2-6): 첨단 +Z · 바닥 피벗 · z∈[0,1] — posOS.z가 「끝부터」의 도메인(침전·침식·흔들림).
// 불혀별 값(_Age·_Erode·_Seed)은 MaterialPropertyBlock — 전 프로퍼티는 UnityPerMaterial CBUFFER(SRP Batcher 호환).
// 주광은 방향만 쓴다(GetMainLight().direction) — 색은 곱하지 않는다. 주광 없는 씬(URP 기본 색 흑)은 폴백 방향.
// 3패스(Forward/DepthOnly/DepthNormals)가 같은 정점 흔들림·clip을 공유 — 깊이·법선 버퍼=보이는 형상(InkWater·InkBody와 동형).
// Forward 패스는 SSAO 텍스처를 샘플하지 않는다 — 불은 그늘지지 않는다(AfterOpaque 0 계약).
// 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkFlame"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color (팔레트 틴트 슬롯 — 런타임 세팅)", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color("Color (호환 별칭 — TintHierarchy; 출력에 곱하지 않는다)", Color) = (1, 1, 1, 1)
        _InkColor("Ink Color (먹 침전 종점 — 팔레트 _fallback 먹)", Color) = (0.16, 0.15, 0.13, 1)

        [Header(Ignite TEST)]
        _IgniteLift("Ignite Lift (점화 창 명도 상승 비율 — ART-INK-LOOK §4.1)", Range(0, 0.3)) = 0.12
        _IgniteWindow("Ignite Window (점화 창 — _Age 비율)", Range(0.01, 0.5)) = 0.12
        _IgniteAdd("Ignite Add (점화 창 틴트 가산 — §4.2 addK 상한 0.25)", Range(0, 0.25)) = 0.25

        [Header(Settle TEST)]
        _SettleStart("Settle Start (먹 침전 시작 _Age)", Range(0, 0.99)) = 0.6
        _TipInk("Tip Ink (끝(+Z)부터 침전 가중)", Range(0, 1)) = 0.35

        [Header(Erode TEST)]
        _NoiseScale("Noise Scale (침식 결 밀도 — posOS 단위)", Float) = 6
        _ErodeTipBias("Erode Tip Bias (끝부터 갈라짐 가중)", Range(0, 1)) = 0.2

        [Header(Flicker TEST)]
        _FlickerAmp("Flicker Amp (정점 흔들림 폭 — 끝일수록 z 비례)", Range(0, 0.3)) = 0.06
        _FlickerFreq("Flicker Freq (Hz)", Float) = 9

        [Header(Shade TEST)]
        _ShadeMin("Shade Min (반램버트 하한 — 그늘 면 명도)", Range(0, 1)) = 0.6
        _RimDarken("Rim Darken (림 먹 테두리 농도)", Range(0, 1)) = 0.35
        _BackDarken("Back Darken (뒷면 명도 배율 — Cull Off·VFACE)", Range(0, 1)) = 0.7

        [Header(Per tongue MPB)]
        _Age("Age (0..1 수명 진행 — 불혀별 MPB)", Range(0, 1)) = 0
        _Erode("Erode (0..1 침식 — 불혀별 MPB)", Range(0, 1)) = 0
        _Seed("Seed (불혀별 노이즈 시드)", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // 주광 방향만 쓴다(GetMainLight().direction) — 광원 색은 곱하지 않는다(담채 드리프트 방지)
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 posOS      : TEXCOORD0;  // 변형 전 원좌표 — 침식·침전 도메인(흔들림이 결을 흔들지 않게)
            float3 normalWS   : TEXCOORD1;
            float3 viewDirWS  : TEXCOORD2;
        };

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Color;
            half4 _InkColor;
            half  _IgniteLift;
            half  _IgniteWindow;
            half  _IgniteAdd;
            half  _SettleStart;
            half  _TipInk;
            float _NoiseScale;
            half  _ErodeTipBias;
            half  _FlickerAmp;
            float _FlickerFreq;
            half  _ShadeMin;
            half  _RimDarken;
            half  _BackDarken;
            half  _Age;
            half  _Erode;
            float _Seed;
        CBUFFER_END

        // 정수 해시 value noise 2옥타브 — InkStroke.shader 이식(텍스처 0). 접두는 URP 내장·형제 셰이더와의 충돌 회피
        float FlameHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float FlameNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(FlameHash(i), FlameHash(i + float2(1, 0)), f.x),
                lerp(FlameHash(i + float2(0, 1)), FlameHash(i + float2(1, 1)), f.x), f.y);
        }

        float FlameFbm(float2 p)
        {
            return FlameNoise(p) * 0.6 + FlameNoise(p * 2.13 + 17.7) * 0.4;
        }

        // 정점 흔들림(FX-ASSETS §4.2-5 허용 수단) — 뿌리(z=0) 고정·끝일수록 크게, 시드로 불혀마다 위상이 다르다. 3패스 공유
        float3 FlickerOS(float3 p)
        {
            // 시간 도메인은 fmod로 접는다 — 무한 증가하면 해시 입력(×456)이 float 정밀도를 넘어 frac이 양자화돼 흔들림이 퇴화한다.
            // 128.0은 기술 상수(해시 입력 ≤128×456≈5.8e4 → 정밀도 ≈0.004); 128/_FlickerFreq(≈14s)마다 위상 1회 점프 — 수명 0.5s 불혀에서 비가시
            float2 dom = float2(p.z * 3.0 + _Seed, fmod(_Time.y * _FlickerFreq, 128.0));
            float2 n = float2(FlameFbm(dom), FlameFbm(dom + float2(7.3, 11.9))) - 0.5;
            p.xy += n * (2.0 * _FlickerAmp * p.z);
            return p;
        }

        // 침식 — 끝(+Z)부터 갈라진다. _Erode 0=온전(항상 통과 — 노이즈 ≥0) → 1=전면 소거(노이즈 ≤1). 도메인=변형 전 posOS
        void ClipFlame(float3 posOS)
        {
            float n = FlameFbm(posOS.xz * _NoiseScale + _Seed);
            clip(n + 0.2 - _Erode * 1.25 - posOS.z * _ErodeTipBias * _Erode);
        }

        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 positionWS = TransformObjectToWorld(FlickerOS(v.positionOS.xyz));
            o.positionCS = TransformWorldToHClip(positionWS);
            o.posOS = v.positionOS.xyz;
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.viewDirWS = GetWorldSpaceNormalizeViewDir(positionWS);
            return o;
        }

        // 주광이 없으면 URP는 방향 (0,0,1)·색 흑을 넣는다 — 색 흑/영벡터를 「없음」으로 보고 폴백 방향(감사 씬 등 디렉셔널 없는 씬)
        float3 MainLightDirOrFallback()
        {
            Light mainLight = GetMainLight();
            float3 L = mainLight.direction;
            bool missing = dot(L, L) < 0.25 || (mainLight.color.r + mainLight.color.g + mainLight.color.b) <= 0.001;
            return missing ? normalize(float3(0.3, 0.8, 0.5)) : normalize(L);
        }
        ENDHLSL

        // ---- (1) 색 — 틴트→먹 침전 · 반램버트 · 림 먹 테두리 · 점화 창 · 뒷면 어둡게 ----
        Pass
        {
            Name "InkFlame"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                ClipFlame(i.posOS);
                bool front = IS_FRONT_VFACE(face, true, false);

                // 점화 창(순간 백열)과 먹 침전(끝부터) — 명도만 움직인다, 채도 무변경
                float ignite = 1.0 - smoothstep(0.0, _IgniteWindow, _Age);
                float settle = saturate(smoothstep(_SettleStart, 1.0, _Age) + i.posOS.z * _TipInk * _Age);

                half3 tint = _BaseColor.rgb;
                half3 col = lerp(tint, _InkColor.rgb, settle) * (1.0 + _IgniteLift * ignite);

                // 반램버트 형태 음영(하한 _ShadeMin) — Cull Off라 뒷면은 법선 반전
                float3 N = normalize(i.normalWS);
                N = front ? N : -N;
                float3 L = MainLightDirOrFallback();
                float ndl = saturate(dot(N, L) * 0.5 + 0.5);
                col *= lerp(_ShadeMin, 1.0, ndl);

                // 림 먹 테두리 — 실루엣이 먹선으로 선다(발광 림 아님: 명도 감산만)
                float3 V = normalize(i.viewDirWS);
                float rim = 1.0 - saturate(dot(N, V));
                col *= 1.0 - _RimDarken * rim * rim;

                // 점화 가산(틴트색 × addK ≤0.25, 침전 전 구간만) — 순간만 threshold를 스친다
                col += tint * (_IgniteAdd * ignite * (1.0 - settle));
                col = front ? col : col * _BackDarken;
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ---- (2) 깊이 — URP DepthOnlyPass.hlsl 계약(ColorMask R·positionCS.z 반환), 흔들림·clip 동일 ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            half fragDepth(Varyings i) : SV_Target
            {
                ClipFlame(i.posOS);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // ---- (3) 깊이+법선 — URP DepthNormalsPass 출력 규약(PC_Renderer SSAO Source=DepthNormals), 뒷면 법선 반전 ----
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 fragDepthNormals(Varyings i, FRONT_FACE_TYPE face : FRONT_FACE_SEMANTIC) : SV_Target
            {
                ClipFlame(i.posOS);
                float3 normalWS = normalize(i.normalWS);
                normalWS = IS_FRONT_VFACE(face, normalWS, -normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
