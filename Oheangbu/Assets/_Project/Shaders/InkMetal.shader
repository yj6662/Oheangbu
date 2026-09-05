// 금 광택질 셰이더 — SPEC-SPELL-FX-REWORK §3.2(소 벼린 쇠 송곳) · SPEC-SPELL-FX-ASSETS §4.2-4 예외·§9 예외 3 이행
// (림·그라디언트 트릭 — 실PBR 아님: Metallic/Smoothness/큐브맵/텍스처 없음).
// 환경 큐브맵을 샘플하지 않는다: 반사 = 반사 벡터의 세계 상향 성분으로 읽는 해석적 3단 명도(상단 밝음·지평선 급변·하단 먹).
// 모든 출력 = _BaseColor × 무채 명도 배율 — 색은 팔레트 하나(SpikeVolleyEffect·PatternEffectLifetime.TintHierarchy의
// 정본 슬롯 _BaseColor). 광원색을 곱하지 않아 하늘색·광원색이 비칠 통로가 구조적으로 없다.
// _EmissionColor 프로퍼티 자체가 없다(발광 정적 검사 = 프로퍼티 부재). 피크 클램프로 백색 포화·Bloom threshold 1.0 절대 미달.
// 패스: UniversalForwardOnly + DepthOnly + DepthNormalsOnly — PC_Renderer SSAO(Source=DepthNormals) 프리패스 계약
// (실체가 깊이·법선 텍스처에서 빠지지 않게). ShadowCaster 없음(송곳 shadowCastingMode Off·receiveShadows false).
// 전 프로퍼티 UnityPerMaterial CBUFFER(HLSLINCLUDE 공유 — 세 패스 동일 레이아웃) = SRP Batcher 호환.
// 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkMetal"
{
    Properties
    {
        _BaseColor("Base Color (팔레트 틴트 슬롯)", Color) = (1,1,1,1)
        _ShadeMul("Shade Mul (그늘 면 명도 — 먹 쪽)", Range(0,1)) = 0.32
        _WrapDiffuse("Wrap (반 램버트)", Range(0,1)) = 0.5
        _EnvStrength("Env Strength (지평선 반사 비중)", Range(0,1)) = 0.55
        _SkyLift("Sky Lift (반사 상단 명도 배율)", Range(0.5,1.6)) = 1.20
        _HorizonMul("Horizon Mul", Range(0,1.5)) = 0.95
        _GroundMul("Ground Mul (하단 — 먹)", Range(0,1)) = 0.40
        _HorizonSharp("Horizon Sharpness (클수록 크롬)", Range(1,16)) = 6
        _SpecPower("Spec Power", Range(4,128)) = 48
        _SpecStrength("Spec Strength", Range(0,1)) = 0.28
        _RimPower("Rim Power", Range(1,8)) = 3
        _RimStrength("Rim Strength (가장자리 광택 띠)", Range(0,1)) = 0.18
        _InkEdge("Ink Edge (최외곽 먹선 폭 — NdotV)", Range(0,0.3)) = 0.10
        _HammerScale("Hammer Scale (두드린 결 밀도)", Float) = 16
        _HammerStrength("Hammer Strength", Range(0,0.3)) = 0.06
        _PeakClamp("Peak Clamp (백색 포화·Bloom 차단)", Range(0.5,1)) = 0.90
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 전 프로퍼티 — 세 패스가 같은 레이아웃을 공유해야 SRP Batcher가 잡는다
        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half  _ShadeMul;
            half  _WrapDiffuse;
            half  _EnvStrength;
            half  _SkyLift;
            half  _HorizonMul;
            half  _GroundMul;
            half  _HorizonSharp;
            half  _SpecPower;
            half  _SpecStrength;
            half  _RimPower;
            half  _RimStrength;
            half  _InkEdge;
            half  _HammerScale;
            half  _HammerStrength;
            half  _PeakClamp;
        CBUFFER_END
        ENDHLSL

        // ---------------------------------------------------------------
        // 포워드 — 금 광택질 본체
        Pass
        {
            Name "InkMetal"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

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
                float3 posOS      : TEXCOORD2; // 두드린 결 도메인 — 오브젝트 공간(회전·비행에도 결이 몸에 붙어 있다)
            };

            // 정수 해시 value noise 3D — InkStroke.shader의 2D 판을 3D로(텍스처 없이 두드린 결)
            float Hash3(float3 p)
            {
                p = frac(p * float3(123.34, 456.21, 789.13));
                p += dot(p, p.yzx + 45.32);
                return frac(p.x * p.y * p.z);
            }

            float ValueNoise3(float3 p)
            {
                float3 i = floor(p);
                float3 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(
                    lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x),
                    lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(
                    lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x),
                    lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.posWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.posWS);
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                o.posOS = v.positionOS.xyz;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 N = normalize(i.nWS);
                // 두드린 결(從革) — 오브젝트 공간 노이즈로 법선 미세 흔듦: 플랫 면 안에서도 광택이 고르지 않다
                float3 h = float3(
                    ValueNoise3(i.posOS * _HammerScale),
                    ValueNoise3(i.posOS * _HammerScale + 7.3),
                    ValueNoise3(i.posOS * _HammerScale + 19.1)) - 0.5;
                N = normalize(N + h * (_HammerStrength * 2.0));

                float3 V = normalize(GetWorldSpaceViewDir(i.posWS));
                Light L = GetMainLight();
                float3 Ld = normalize(L.direction);

                half3 base = _BaseColor.rgb;
                half3 shade = base * _ShadeMul;

                // 면 음영(반 램버트) — 플랫 법선이라 면마다 한 톤. 광원색은 쓰지 않는다(색 단일 출처 = 팔레트)
                float nl = saturate(dot(N, Ld) * _WrapDiffuse + (1.0 - _WrapDiffuse));
                half3 diffuse = lerp(shade, base, nl);

                // 해석적 지평선 반사 — 반사 벡터의 상향 성분으로 상단/지평/하단 3단 명도(전부 base 배율). 큐브맵 미샘플
                float3 R = reflect(-V, N);
                float up = pow(saturate(R.y), 1.0 / _HorizonSharp);
                float dn = pow(saturate(-R.y), 1.0 / _HorizonSharp);
                half envMul = _HorizonMul * (1.0 - up - dn) + _SkyLift * up + _GroundMul * dn;
                half3 rgb = lerp(diffuse, base * envMul, _EnvStrength);

                // 정반사 — Blinn-Phong, 좁은 로브(광원색 미사용)
                float3 H = normalize(Ld + V);
                rgb += base * (pow(saturate(dot(N, H)), _SpecPower) * _SpecStrength);

                // 가장자리 광택 띠, 그 바깥 최외곽은 먹선 — 밝은 띠 안쪽에 어두운 윤곽(붓이 칼날을 그리는 법).
                // _InkEdge 하한 가드: 0이면 smoothstep 하한=상한이라 미정의(심판 반박 반영)
                float ndv = saturate(dot(N, V));
                float inkEdge = max(_InkEdge, 1e-3);
                rgb += base * (pow(1.0 - ndv, _RimPower) * _RimStrength * smoothstep(0.0, inkEdge * 2.0, ndv));
                rgb = lerp(shade, rgb, smoothstep(0.0, inkEdge, ndv));

                // 피크 클램프(색상 보존 — 최대 채널 기준 스케일): 백색 포화 절대 금지·Bloom threshold 1.0 절대 미달
                float peak = max(rgb.r, max(rgb.g, rgb.b));
                rgb *= min(1.0, _PeakClamp / max(peak, 1e-4));
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------
        // 깊이 전용 — 깊이 프리패스·_CameraDepthTexture 계약(URP Unlit.shader DepthOnly 패스와 동형)
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthOnlyVert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half DepthOnlyFrag(Varyings i) : SV_Target
            {
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------
        // 깊이+법선 — SSAO(Source=DepthNormals) 프리패스 계약(URP DepthNormalsPass.hlsl 출력 규약과 동형).
        // 기하 법선만 쓴다(두드린 결은 표현층 — 스크린 공간 AO엔 불필요)
        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode" = "DepthNormalsOnly" }

            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 nWS        : TEXCOORD0;
            };

            Varyings DepthNormalsVert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.nWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            // URP DepthNormalsPass.hlsl 출력 규약 — 렌더링 레이어 키워드 시 SV_Target1 동반(InkBody 동형)
            void DepthNormalsFrag(
                Varyings i
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                float3 n = normalize(i.nWS);
                float2 oct = PackNormalOctQuadEncode(n);          // [-1,1]
                float2 remapped = saturate(oct * 0.5 + 0.5);      // [0,1]
                half3 packed = PackFloat2To888(remapped);         // [0,1]
                outNormalWS = half4(packed, 0.0);
                #else
                outNormalWS = half4(NormalizeNormalPerPixel(i.nWS), 0.0);
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
}
