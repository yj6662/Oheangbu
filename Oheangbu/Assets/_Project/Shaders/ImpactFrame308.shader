// 임팩트 프레임 · 화면 먹 덮임 — SPEC-SPELL-DEPLOY-308 §8 · §12 (D308-10 / D308-10b) [TEST].
// ImpactFramePass308이 켜진 프레임에만 톤매핑 뒤(AfterRenderingPostProcessing)의 LDR 화면에 건다. 값만 바꾼다 — 발광이 아니다:
//   모든 패스의 결과는 화면(sRGB) 값으로 최대 채널 ≤ .90, Emission·가산 블렌드·HDR 출력 없음.
// 패스:
//   0 Invert      두 값으로 자르고 뒤집는다(종이 → 먹, 먹선·획 → 종이색). 임팩트 점에서 종이색이 방사형으로 번지고 바늘 선이 뻗는다.
//                 이 번짐이 값으로 표현한 "광원"이며 HUD 반응의 방향 기준이다(_OhImpact308.xy).
//   1 Silhouette  뒤집지 않는다. 배경을 종이 한 값으로 누르고 임계보다 어두운 것(획·적·먹선)만 먹으로 남긴다 + 깊이 윤곽선.
//   2 Return      패스 1의 꼴과 평소 화면을 50 % 섞는다(다시 어두워지지 않는다).
//   3 Local       줄임 설정 · 빈도 초과 · 판정 창 근접: 임팩트 점 둘레에서만 패스 1의 꼴, 바깥은 그대로.
//   4 Flood       화면 먹 덮임과 걷힘(세로 결, 구멍이 넓어지고 남은 먹이 기둥으로 흘러내린다). 1단계는 미리보기만 쓴다.
//   5 LocalInvert Mobile: 색 복사 없이 국소 반전 쿼드(Blend OneMinusDstColor OneMinusSrcAlpha).
// 전역 _OhImpact308 = (화면 u, v, 세기, 장 번호). 색 버퍼가 선형이면(_SrgbTarget 0) 화면 값으로 바꿔 계산하고 되돌린다.
Shader "Hidden/Oheangbu/ImpactFrame308"
{
    Properties
    {
        _FloodMask("Flood mask (R clearing order, G vertical grain; linear)", 2D) = "gray" {}
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        ZWrite Off
        ZTest Always
        Cull Off

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
        #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
        #include "InkCommon308.hlsl"

        TEXTURE2D(_FloodMask);   // sampled with sampler_LinearRepeat: the seed slides it sideways, whatever the import wrap mode is

        float4 _OhImpact308;        // global: screen u, v, strength, frame number
        float4 _ImpactValues;       // threshold, ink value, paper value, radial reach (share of the screen height)
        float4 _ImpactNeedles;      // count, width (share of the screen height), outline strength, local radius
        float4 _ImpactFlood;        // cover 0..1, clear 0..1, reduced edge share (0 = whole screen), 0
        float _ImpactSeed;
        float _SrgbTarget;

        float3 ToDisplay(float3 c) { return _SrgbTarget > 0.5 ? c : LinearToSRGB(saturate(c)); }
        float3 FromDisplay(float3 c) { c = min(saturate(c), OH_PAPER_CEILING); return _SrgbTarget > 0.5 ? c : SRGBToLinear(c); }

        float3 PaperColor() { return min(_ImpactValues.z, OH_PAPER_CEILING) * float3(1.0, 0.977, 0.92); }
        float3 InkColor() { return _ImpactValues.y * float3(1.0, 0.93, 0.86); }

        // aspect-corrected offset from the impact point, in screen heights
        float2 FromImpact(float2 uv)
        {
            float2 d = uv - _OhImpact308.xy;
            d.x *= _ScreenParams.x / max(_ScreenParams.y, 1.0);
            return d;
        }

        // needle lines from the impact point to the screen edge: 1 on a line
        float Needles(float2 d)
        {
            float count = max(_ImpactNeedles.x, 0.0);
            float dist = length(d);
            if (count < 0.5 || dist < 0.0001) return 0.0;
            float k = (atan2(d.y, d.x) / 6.2831853 + 0.5) * count;
            float id = floor(k);
            float r = OhHash(float2(id, _ImpactSeed + 3.1));
            float r2 = OhHash(float2(id + 17.0, _ImpactSeed + 9.7));
            // angular distance from the line's own angle, turned into a screen distance
            float across = abs(frac(k) - (0.25 + 0.5 * r)) / count * 6.2831853 * dist;
            float begin = 0.03 + 0.16 * r2;
            float width = _ImpactNeedles.y * (0.5 + r) * saturate(1.6 - dist * 0.9);
            return step(across, width) * step(begin, dist) * step(0.18, r + r2 * 0.6);
        }

        float DepthEdge(float2 uv)
        {
            float2 px = 1.5 / _ScreenParams.xy;
            float c = LinearEyeDepth(SampleSceneDepth(uv), _ZBufferParams);
            float l = LinearEyeDepth(SampleSceneDepth(uv - float2(px.x, 0.0)), _ZBufferParams);
            float r = LinearEyeDepth(SampleSceneDepth(uv + float2(px.x, 0.0)), _ZBufferParams);
            float t = LinearEyeDepth(SampleSceneDepth(uv + float2(0.0, px.y)), _ZBufferParams);
            float b = LinearEyeDepth(SampleSceneDepth(uv - float2(0.0, px.y)), _ZBufferParams);
            float e = (abs(l - c) + abs(r - c) + abs(t - c) + abs(b - c)) / max(c, 0.05);
            return saturate((e - 0.06) * 14.0) * step(c, 120.0);
        }

        // pass 1 look: paper ground, everything darker than the cut stays ink, depth outline, thin ink needles
        float3 Silhouette(float2 uv, float3 scene)
        {
            float dark = step(OhLuma(scene), _ImpactValues.x);
            float mark = max(dark, DepthEdge(uv) * saturate(_ImpactNeedles.z));
            mark = max(mark, Needles(FromImpact(uv)) * 0.9);
            return lerp(PaperColor(), InkColor(), saturate(mark));
        }

        half4 FragInvert(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float3 scene = ToDisplay(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
            float dark = step(OhLuma(scene), _ImpactValues.x);
            // flipped: the paper goes to ink, ink lines and strokes go to paper
            float value = dark;
            float2 d = FromImpact(uv);
            float reach = max(_ImpactValues.w, 0.01) * saturate(_OhImpact308.z);
            // the paper value spreads from the impact point in a hard-edged ragged disc: the "light", as a value
            float rag = OhValueNoise(float2(atan2(d.y, d.x) * 5.0, _ImpactSeed)) * 0.18;
            float spread = step(length(d), reach * (0.55 + rag));
            value = max(value, spread);
            value = max(value, Needles(d));
            float3 o = lerp(InkColor(), PaperColor(), saturate(value));
            return half4(FromDisplay(o), 1.0);
        }

        half4 FragSilhouette(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float3 scene = ToDisplay(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
            return half4(FromDisplay(Silhouette(uv, scene)), 1.0);
        }

        half4 FragReturn(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float3 scene = ToDisplay(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb);
            // never darker than the scene's own paper: the frame does not flip a second time
            float3 o = lerp(Silhouette(uv, scene), scene, 0.5);
            return half4(FromDisplay(o), 1.0);
        }

        half4 FragLocal(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float4 raw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            float2 d = FromImpact(uv);
            float rag = OhValueNoise(float2(atan2(d.y, d.x) * 6.0, _ImpactSeed + 5.0)) * 0.25;
            float inside = step(length(d), max(_ImpactNeedles.w, 0.01) * (0.75 + rag));
            float3 o = FromDisplay(Silhouette(uv, ToDisplay(raw.rgb)));
            return half4(lerp(raw.rgb, o, inside), 1.0);
        }

        half4 FragFlood(Varyings i) : SV_Target
        {
            float2 uv = i.texcoord;
            float4 raw = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
            half2 mask = SAMPLE_TEXTURE2D(_FloodMask, sampler_LinearRepeat, uv * float2(1.0, 0.5) + float2(_ImpactSeed * 0.137, 0.0)).rg;
            float cover = saturate(_ImpactFlood.x);
            float clearing = saturate(_ImpactFlood.y);
            // covering: the sheet comes down from the top in vertical streaks
            float sheet = step(1.0 - uv.y, cover * 1.25 - mask.g * 0.25);
            // clearing: holes open where the order is low and widen; what is left drains as columns
            float hole = step(mask.r, clearing * 1.15 - 0.05);
            float pillar = step(uv.y, (1.0 - clearing) * (0.35 + 0.65 * mask.g)) * step(0.001, 1.0 - clearing);
            float ink = sheet * max(1.0 - hole, pillar * step(clearing, 0.999));
            // reduced setting: the screen edge only
            float2 e = min(uv, 1.0 - uv);
            float edgeShare = _ImpactFlood.z;
            ink *= edgeShare > 0.001 ? step(min(e.x, e.y), edgeShare * 0.5) : 1.0;
            float3 o = FromDisplay(InkColor() * (0.85 + 0.3 * mask.g));
            return half4(lerp(raw.rgb, o, step(0.5, ink)), 1.0);
        }

        struct QuadVaryings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

        QuadVaryings VertQuad(uint vertexID : SV_VertexID)
        {
            QuadVaryings o;
            o.positionCS = GetFullScreenTriangleVertexPosition(vertexID);
            o.uv = GetFullScreenTriangleTexCoord(vertexID);
            return o;
        }

        half4 FragLocalInvert(QuadVaryings i) : SV_Target
        {
            float2 d = FromImpact(i.uv);
            float rag = OhValueNoise(float2(atan2(d.y, d.x) * 6.0, _ImpactSeed + 5.0)) * 0.25;
            float inside = step(length(d), max(_ImpactNeedles.w, 0.01) * (0.75 + rag));
            float m = max(inside, Needles(d) * step(length(d), _ImpactNeedles.w * 2.0));
            // OneMinusDstColor / OneMinusSrcAlpha: m = 1 inverts the destination, m = 0 leaves it.
            // The inverted value is scaled so that black never turns brighter than the paper ceiling (.90 display = .787 linear).
            float k = m * (_SrgbTarget > 0.5 ? OH_PAPER_CEILING : 0.787);
            return half4(k, k, k, m);
        }
        ENDHLSL

        Pass
        {
            Name "Invert"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment FragInvert
            ENDHLSL
        }

        Pass
        {
            Name "Silhouette"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment FragSilhouette
            ENDHLSL
        }

        Pass
        {
            Name "Return"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment FragReturn
            ENDHLSL
        }

        Pass
        {
            Name "Local"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment FragLocal
            ENDHLSL
        }

        Pass
        {
            Name "Flood"
            Blend Off
            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex Vert
            #pragma fragment FragFlood
            ENDHLSL
        }

        Pass
        {
            Name "LocalInvert"
            Blend OneMinusDstColor OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.5
            #pragma editor_sync_compilation
            #pragma vertex VertQuad
            #pragma fragment FragLocalInvert
            ENDHLSL
        }
    }
    FallBack Off
}
