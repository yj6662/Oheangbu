// 전체화면 먹선·원경 씻김 포스트 — SPEC-SPIKE-WORLD-LOOKDEV §5-3.
// URP 내장 FullScreenPassRendererFeature가 이 셰이더로 Blit한다(커스텀 RenderFeature/Pass 코드 0줄).
// 주입점 = 450 BeforeRenderingTransparents: 깊이·법선·SSAO가 유효하고, 작도 획(InkSpray/InkDust 투명)은 이 포스트 밖에 남는다(§5-3·A4).
// 순서: 깊이 소벨 먹선 → 법선 소벨 먹선 → lerp(color,_OhInkColor,edge) → 원경 소지 씻김(광원 마커 면제) → _OhWorldPost 게이트.
// _OhWorldPost < 0.5 이면 원본을 그대로 반환한다(NaN 항등 — 드라이버 없는 씬 픽셀 불변, A5). 색은 전부 팔레트 전역.
// 세피아·그레인·비네트·종이결 없음(§5-3). 이식: 레거시 MandateOfInk S_InkPostTemp:72-88 · S_InkWashV1:80-111.
Shader "Oheangbu/InkWorldPost"
{
    Properties
    {
        // FSPRF 인스펙터에 노출(전부 무색 수치 — 색은 _Oh* 전역)
        [Header(Ink Line TEST)]
        _DepthEdgeStrength("Depth Edge Strength", Range(0, 4)) = 1.4
        _DepthEdgeBias("Depth Edge Bias (기울기 정규화 하한)", Range(0.001, 0.2)) = 0.02
        _NormalEdgeStrength("Normal Edge Strength", Range(0, 4)) = 1.0
        _NormalEdgeFadeDistance("Normal Edge Fade Distance (m — 이 거리 밖 법선 먹선 감쇠)", Float) = 140
        _EdgeJitter("Edge Jitter (먹선 떨림 폭 — 지글 방지 해시)", Range(0, 2)) = 0.6

        [Header(Paper Wash TEST)]
        _PaperFadeStart("Paper Fade Start (m — 원경 소지 씻김 시작)", Float) = 90
        _PaperFadeEnd("Paper Fade End (m — 소지 씻김 포화)", Float) = 420
        _WashStrength("Wash Strength (씻김 세기)", Range(0, 1)) = 0.9
        _WashKeep("Wash Keep (어둠 보존 — 먹은 덜 씻긴다)", Range(0, 1)) = 0.55

        [Header(Debug)]
        _DebugView("Debug View (0 off / 1 depthEdge / 2 normalEdge / 3 normals / 4 wash)", Float) = 0

        // 떨림 해시 좌표계: 0 = 복원 월드 좌표(기본) / 1 = 화면 좌표(A/B 토글)
        [ToggleUI] _JitterScreenSpace("Jitter Screen Space", Float) = 0
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off ZTest Always Cull Off

        Pass
        {
            Name "InkWorldPost"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"   // Luminance
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            // Blit.hlsl = FSPRF의 정점(Vert) + Varyings(positionCS·texcoord) + _BlitTexture/_TexelSize + sampler_LinearClamp
            // (URP FullScreenPassRendererFeature가 쓰는 core RTHandle Blit — ShaderLibrary가 아니라 core/Runtime/Utilities)
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            // 법선 텍스처 — DepthNormals 프리패스 산출. 알파 −1 = 광원 실체 마커(InkLightSource가 씀, §5-2·B10).
            TEXTURE2D_X(_CameraNormalsTexture);
            SAMPLER(sampler_CameraNormalsTexture);

            // 팔레트 전역(드라이버가 매 프레임 SetGlobalColor) — 재질 CBUFFER 밖(전역)
            float4 _OhInkColor;
            float4 _OhPaperColor;   // 원경 씻김 목적지 = 소지(하늘과 같은 팔레트, §5-8)
            float _OhWorldPost;

            CBUFFER_START(UnityPerMaterial)
                half _DepthEdgeStrength;
                half _DepthEdgeBias;
                half _NormalEdgeStrength;
                half _NormalEdgeFadeDistance;
                half _EdgeJitter;
                half _PaperFadeStart;
                half _PaperFadeEnd;
                half _WashStrength;
                half _WashKeep;
                half _DebugView;
                half _JitterScreenSpace;
            CBUFFER_END

            // 화면 좌표 해시(떨림) — 0~1
            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }

            float3 SampleNormal(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv).xyz;
            }

            float SampleNormalMarker(float2 uv)
            {
                // 알파 < −0.5 = 광원 실체(씻김 면제). 표준 표면 a=0, 하늘 a=1(클리어).
                return SAMPLE_TEXTURE2D_X(_CameraNormalsTexture, sampler_CameraNormalsTexture, uv).w;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);

                // 게이트 — 드라이버 없는 씬(전역 기본 0)에서는 원본 그대로(A5 NaN 항등)
                if (_OhWorldPost < 0.5) return color;

                float2 texel = _BlitTexture_TexelSize.xy;

                // ── 중심 깊이(선형, 미터) + 하늘 가드 ──
                float rawD = SampleSceneDepth(uv);
                float centerLin = LinearEyeDepth(rawD, _ZBufferParams);
                bool isSky = centerLin >= (_ProjectionParams.z - 1.0);   // far 근처 = 하늘/여백(먹선·씻김 면제)

                // ── 깊이 소벨(4이웃) — gradient/max(depth*0.08, bias) ──
                float dL = LinearEyeDepth(SampleSceneDepth(uv + float2(-texel.x, 0)), _ZBufferParams);
                float dR = LinearEyeDepth(SampleSceneDepth(uv + float2( texel.x, 0)), _ZBufferParams);
                float dD = LinearEyeDepth(SampleSceneDepth(uv + float2(0, -texel.y)), _ZBufferParams);
                float dU = LinearEyeDepth(SampleSceneDepth(uv + float2(0,  texel.y)), _ZBufferParams);
                float depthGrad = abs(dL - dR) + abs(dD - dU);
                float depthEdge = saturate(depthGrad / max(centerLin * 0.08, _DepthEdgeBias)) * _DepthEdgeStrength;

                // ── 법선 소벨 — hasNormal 가드 + 원경 감쇠 ──
                float3 nC = SampleNormal(uv);
                float hasNormal = step(0.25, dot(nC, nC));      // 0벡터(하늘/미기록) 배제
                float3 nL = SampleNormal(uv + float2(-texel.x, 0));
                float3 nR = SampleNormal(uv + float2( texel.x, 0));
                float3 nD = SampleNormal(uv + float2(0, -texel.y));
                float3 nU = SampleNormal(uv + float2(0,  texel.y));
                float normalGrad = length(nL - nR) + length(nD - nU);
                float normalFade = 1.0 - saturate((centerLin - 0.0) / max(_NormalEdgeFadeDistance, 1.0));
                float normalEdge = saturate(normalGrad) * _NormalEdgeStrength * hasNormal * normalFade;

                // ── 떨림 해시(먹선 지글 방지) — 월드/화면 좌표 토글 ──
                float2 jitterCoord = (_JitterScreenSpace > 0.5)
                    ? input.positionCS.xy
                    : ComputeWorldSpacePosition(uv, rawD, UNITY_MATRIX_I_VP).xz * 4.0;
                float jitter = (Hash21(jitterCoord) - 0.5) * _EdgeJitter * 0.02;

                float edge = saturate((depthEdge + normalEdge) + jitter);
                edge = isSky ? 0.0 : edge;                       // 하늘엔 먹선 없음(§5-3·B4)

                // ── 먹선 합성 ──
                half3 rgb = lerp(color.rgb, _OhInkColor.rgb, edge);

                // ── 원경 소지 씻김 — 광원 마커 면제 ──
                float washFade = saturate((centerLin - _PaperFadeStart) / max(_PaperFadeEnd - _PaperFadeStart, 1.0));
                float darkness = 1.0 - Luminance(color.rgb);     // 어두운(먹) 픽셀은 덜 씻긴다
                float lightMarker = step(SampleNormalMarker(uv), -0.5);   // a<−0.5 = 광원 실체 → 씻김 0
                float wash = washFade * _WashStrength * (1.0 - _WashKeep * darkness) * (1.0 - lightMarker);
                wash = isSky ? 0.0 : wash;                       // 하늘은 이미 소지 — 씻김 무의미
                // 원경일수록 소지로 수렴 — 먹(어두운 픽셀)은 _WashKeep로 덜 씻겨 능선 실루엣이 남는다(§5-3·B3).
                rgb = lerp(rgb, _OhPaperColor.rgb, wash);

                // ── 디버그 뷰 ──
                if (_DebugView > 0.5)
                {
                    if (_DebugView < 1.5) return half4(depthEdge.xxx, 1);
                    if (_DebugView < 2.5) return half4(normalEdge.xxx, 1);
                    if (_DebugView < 3.5) return half4(nC * 0.5 + 0.5, 1);
                    return half4(wash.xxx, 1);
                }

                return half4(rgb, color.a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
