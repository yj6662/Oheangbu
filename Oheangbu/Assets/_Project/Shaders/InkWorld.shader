// 환경 재질 셰이더 — SPEC-SPIKE-WORLD-LOOKDEV §5-1 (수묵담채 월드 룩 실증 · 환경 불투명 1종).
// 출력 = lerp(먹, 소지, 톤) — 색은 전부 팔레트 전역(_OhInkColor·_OhPaperColor·_OhWoodColor)에서 오고 재질엔 무색 수치만 있다(#153).
// 톤 = 풀 램버트 주광 × 그림자 + 부가 광원(Forward+ 클러스터 루프) + 앰비언트 스칼라 → SSAO → 먹-종이 램프(smoothstep).
// 광원 hue는 곱하지 않는다 — Luminance(light.color) 스칼라만(#153). 앰비언트는 스칼라(스카이박스 색 유입 0).
// 지속 발광 없음(Emission 프로퍼티 자체 부재 — ArtAudio:29·31). 역번짐 = 이웃 광원(InkLightSource + Point Light)이 이 램프를
// 먹에서 소지로 물러나게 하는 것뿐(#152) — 벽이 빛나는 게 아니라 어둠이 물러난다.
// 이식: 레거시 MandateOfInk S_ToonLitTemp(톤 램프·셀 요동) · S_ObjectInkWash(밑면 먹 고임) · InkBody(실루엣 먹선·SSAO·3패스 템플릿).
// 전역 기본값이 검정이라 WorldLookDriver 없는 씬에서 이 재질은 검정으로 나온다(#155 결합 명시).
// 한글 [Tooltip] 금지(ShaderLab 파서 — 레거시 교훈). 인식 불가침: 표현 전용.
Shader "Oheangbu/InkWorld"
{
    Properties
    {
        [Header(Tone Ramp TEST)]
        _InkPoint("Ink Point (이 명도 이하 = 먹)", Range(0, 1)) = 0.04
        _PaperPoint("Paper Point (이 명도 이상 = 소지)", Range(0, 1)) = 0.68
        _AmbientLevel("Ambient Level (램프 앞 스칼라 채움광 — 갱도 0.03 / 바위 0.35 / 능선 0.15)", Range(0, 1)) = 0.15
        _AddLightGain("Add Light Gain (Point Light 기여 배율)", Range(0, 4)) = 1
        _AoStrength("AO Strength (SSAO 수용 — 0=무시)", Range(0, 1)) = 0.7

        [Header(Ink Marks TEST)]
        _CelNoiseScale("Cel Noise Scale (월드 m당 셀 수 — 램프 경계 요동 도메인)", Float) = 1.4
        _CelNoiseStrength("Cel Noise Strength (램프 경계 요동 폭)", Range(0, 0.5)) = 0.12
        _RimWidth("Rim Width (실루엣 먹선 폭 — N.V 임계)", Range(0, 1)) = 0.18
        _RimDark("Rim Dark (실루엣 먹선 농도)", Range(0, 1)) = 0.35
        _UndersideInk("Underside Ink (아래를 보는 면의 먹 고임)", Range(0, 1)) = 0.5

        [Header(Color Slot)]
        [ToggleUI] _UseWoodColor("Use Wood Color (램프 밝은 끝 = _OhWoodColor 전역 — 갱목)", Float) = 0
        _TintRetain("Tint Retain (램프 대신 밝은 끝 x 명도를 남기는 비율)", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "InkNoise3D.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        CBUFFER_START(UnityPerMaterial)
            half _InkPoint;
            half _PaperPoint;
            half _AmbientLevel;
            half _AddLightGain;
            half _AoStrength;
            half _CelNoiseScale;
            half _CelNoiseStrength;
            half _RimWidth;
            half _RimDark;
            half _UndersideInk;
            half _UseWoodColor;
            half _TintRetain;
            half _Cull;
        CBUFFER_END

        // 팔레트 전역 — WorldLookDriver가 Shader.SetGlobalColor(sRGB→리니어 변환). CBUFFER 밖(InkStroke _InkNow 선례):
        // 안에 두면 머티리얼 값이 전역을 가린다. 프로퍼티로 선언하지 않는다 — 재질에 색을 저장하지 않기 위해.
        float4 _OhInkColor;
        float4 _OhPaperColor;
        float4 _OhWoodColor;
        ENDHLSL

        // ---- (1) 색 — 먹-소지 톤 램프 ----
        Pass
        {
            Name "InkWorldForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float3 n = normalize(i.normalWS);
                float3 posWS = i.positionWS;
                float3 V = GetWorldSpaceNormalizeViewDir(posWS);
                float2 screenUV = GetNormalizedScreenSpaceUV(i.positionCS);

                // 주광 — 풀 램버트 × 그림자(갱도 어둠은 완전한 먹이어야 한다 — half-Lambert 아님).
                // hue 미곱(#153): 스칼라 = Luminance(색) — 백색 광원이면 Intensity 그 자체
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light mainLight = GetMainLight(shadowCoord);
                float main = Luminance(mainLight.color) * saturate(dot(n, mainLight.direction))
                           * mainLight.shadowAttenuation * mainLight.distanceAttenuation;

                // 부가 광원 — Forward+ 클러스터 루프. 매크로가 inputData.normalizedScreenSpaceUV / positionWS 변수명을 요구한다
                float add = 0.0;
                #if defined(_ADDITIONAL_LIGHTS)
                InputData inputData = (InputData)0;
                inputData.positionWS = posWS;
                inputData.normalizedScreenSpaceUV = screenUV;
                uint pixelLightCount = GetAdditionalLightsCount();
                #if USE_CLUSTER_LIGHT_LOOP
                // 클러스터 밖 부가 방향광(광원 버퍼 앞쪽) — Lighting.hlsl UniversalFragmentPBR 동형
                for (uint dirIndex = 0; dirIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); dirIndex++)
                {
                    Light dl = GetAdditionalLight(dirIndex, posWS, half4(1, 1, 1, 1));
                    add += Luminance(dl.color) * dl.distanceAttenuation * dl.shadowAttenuation * saturate(dot(n, dl.direction));
                }
                #endif
                LIGHT_LOOP_BEGIN(pixelLightCount)
                    Light light = GetAdditionalLight(lightIndex, posWS, half4(1, 1, 1, 1));
                    add += Luminance(light.color) * light.distanceAttenuation * light.shadowAttenuation * saturate(dot(n, light.direction));
                LIGHT_LOOP_END
                add *= _AddLightGain;
                #endif

                // 앰비언트 = 스칼라(스카이박스 색 유입 0) → 명도 → SSAO(AO = 먹 — 맵 가이드 §4 가설)
                float luma = saturate(main + add + _AmbientLevel);
                #if defined(_SCREEN_SPACE_OCCLUSION)
                float ao = GetScreenSpaceAmbientOcclusion(screenUV).indirectAmbientOcclusion;
                luma *= lerp(1.0, ao, _AoStrength);
                #endif

                // 먹-종이 톤 램프 — 경계를 월드 Fbm이 흔든다(붓 자국, 시간항 없음). 레거시 S_ToonLitTemp 이식
                float wobble = (OhFbm3(posWS * _CelNoiseScale) - 0.5) * _CelNoiseStrength;
                float tone = smoothstep(_InkPoint, _PaperPoint, luma + wobble);

                // 실루엣 먹선(InkBody 승계) · 아래를 보는 면의 먹 고임(S_ObjectInkWash 승계)
                float ndv = saturate(dot(n, V));
                float rim = 1.0 - smoothstep(0.0, _RimWidth, ndv);
                tone *= (1.0 - rim * _RimDark);
                tone *= (1.0 - saturate(-n.y) * _UndersideInk);

                // 색 = 팔레트 전역 2색의 lerp — 재질에 색 없음. 갈색 슬롯은 램프의 밝은 끝을 갈아 끼운다(채색 = 재질의 색, 환경 채색 아님)
                float3 lit = lerp(_OhPaperColor.rgb, _OhWoodColor.rgb, _UseWoodColor);
                float3 col = lerp(_OhInkColor.rgb, lit, tone);
                col = lerp(col, lit * luma, _TintRetain);
                return half4(col, 1.0);
            }
            ENDHLSL
        }

        // ---- (2) 그림자 캐스터 — URP ShadowCasterPass.hlsl 동형(바이어스·near 클램프). 갱목·벽이 문턱 햇빛 띠를 만든다 ----
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // ShadowUtils.SetupShadowCasterConstantBuffer가 채운다(URP ShadowCasterPass.hlsl과 같은 선언)
            float3 _LightDirection;
            float3 _LightPosition;

            struct VaryingsShadow
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsShadow ShadowVert(Attributes v)
            {
                VaryingsShadow o;
                float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(v.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = positionCS;
                return o;
            }

            half4 ShadowFrag(VaryingsShadow i) : SV_TARGET
            {
                return 0;
            }
            ENDHLSL
        }

        // ---- (3) 깊이 — 포스트 깊이 소벨·원경 씻김의 원천 ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsDepth DepthOnlyVert(Attributes v)
            {
                VaryingsDepth o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                return o;
            }

            half DepthOnlyFrag(VaryingsDepth i) : SV_TARGET
            {
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // ---- (4) 깊이+법선 — URP DepthNormalsPass.hlsl 계약(InkBody 동형). SSAO(Source=DepthNormals)·포스트 법선 소벨의 원천 ----
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD2;
            };

            VaryingsDN DepthNormalsVert(Attributes v)
            {
                VaryingsDN o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(v.normalOS));
                return o;
            }

            void DepthNormalsFrag(
                VaryingsDN input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                outNormalWS = half4(packedNormalWS, 0.0);
                #else
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(normalWS, 0.0);   // a=0 = 표준(광원 마커 −1과 구분 — InkLightSource)
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
}
