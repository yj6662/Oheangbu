// 맞은 적의 피격 반응용 평면 값 — SPEC-SPELL-DEPLOY-308 §7 (D308-10c에서 강화) [TEST].
// ImpactFramePass308이 대상 렌더러를 이 재질로 "다시 그린다"(cmd.DrawRenderer). 적 셰이더·재질·속성 블록은 건드리지 않는다.
// 값: 어두운 몸은 회백, 밝은 몸은 먹색 — 평면 한 값이다. 발광이 아니다(Emission·가산·HDR·발광 항 없음, 최종 ≤ .85).
// 후처리 뒤 색 버퍼에 그리므로 깊이 첨부가 없다. 장면 깊이(_CameraDepthTexture)와 화소에서 직접 비교해 가려진 곳은 그리지 않는다.
// 세 가지 쓰임(HitFlicker308이 재질을 나눠 쓴다):
//   값 튐·깜빡임 = 몸 전체(_StainOn 0, _Knock 0)
//   먹 얼룩      = _StainOn 1: 맞은 점(_StainPoint.xyz, 반지름 w) 둘레만 남긴다. _Stain(1 → 0)이 줄면 가장자리부터 줄어든다(커지지 않는다)
//   윤곽 밀림    = _Knock: 실루엣을 월드에서 옮겨 한 번 더 그린다. _DepthBias를 음수로 주면 몸 위에서는 장면 깊이에 가려지고
//                  몸 바깥으로 나온 얇은 띠만 남는다
Shader "Hidden/Oheangbu/InkFlat308"
{
    Properties
    {
        _FlatColor("Flat value (LDR)", Color) = (0.72, 0.71, 0.68, 1)
        _DepthBias("Depth bias (m)", Float) = 0.06
        _SrgbTarget("Colour buffer already display-encoded", Float) = 0
        _Knock("Silhouette knock (world offset xyz)", Vector) = (0, 0, 0, 0)
        _StainOn("Stain mode (0 = the whole body)", Float) = 0
        _StainPoint("Stain centre (world xyz), radius m (w)", Vector) = (0, 0, 0, 1)
        _Stain("Stain cover (1 = the full radius, 0 = gone)", Range(0, 1)) = 1
        _StainSeed("Stain seed", Float) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }

        Pass
        {
            Name "InkFlat"
            Blend Off
            ZWrite Off
            ZTest Always
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "InkCommon308.hlsl"

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 screen : TEXCOORD0; float3 bodyWS : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                half4 _FlatColor;
                float _DepthBias;
                float _SrgbTarget;
                float4 _Knock;
                float _StainOn;
                float4 _StainPoint;
                float _Stain;
                float _StainSeed;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 ws = TransformObjectToWorld(v.positionOS.xyz);
                o.bodyWS = ws;                                   // the stain is measured on the body where it really is
                o.positionCS = TransformWorldToHClip(ws + _Knock.xyz);   // knock: the silhouette once more, pushed aside
                o.screen = ComputeScreenPos(o.positionCS);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float2 uv = i.screen.xy / max(i.screen.w, 0.0001);
                float scene = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
                // perspective: screen.w is the eye depth of this fragment
                clip(scene + _DepthBias - i.screen.w);
                // stain: a blot round the hit point with a torn rim. _Stain only falls, so the blot only shrinks - down to
                // nothing at 0 (the torn rim scales with the cover).
                float away = distance(i.bodyWS, _StainPoint.xyz) / max(_StainPoint.w, 0.01);
                float torn = OhFbm(i.bodyWS.xy * 9.0 + i.bodyWS.z * 6.1 + _StainSeed);
                float keep = saturate(_Stain) * (1.0 - (torn - 0.5) * 0.9) - away;
                clip(_StainOn > 0.5 ? keep - 0.0001 : 1.0);
                half3 rgb = min(saturate(_FlatColor.rgb), OH_INK_CEILING_OUT);
                rgb = _SrgbTarget > 0.5 ? LinearToSRGB(rgb) : rgb;
                return half4(rgb, 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
