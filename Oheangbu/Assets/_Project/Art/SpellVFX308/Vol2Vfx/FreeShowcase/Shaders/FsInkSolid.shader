// D308-150 V10-V11 free showcase: SOLID ink strokes (burst strokes, the sweeping arc, the spear line). Alpha-clipped and depth-writing (queue AlphaTest)
// so the world post treats the ink as a thing in the scene (a transparent stroke over the sky was washed to paper by the ink-wash post), with DepthOnly /
// DepthNormals passes for the prepass. Erosion like the particle shader: vertex alpha = life left; a burning rim (_EdgeColor, HDR) just inside the cut.
Shader "Oheangbu/FreeShowcase/InkSolid"
{
    Properties
    {
        _BaseMap ("Shape (alpha)", 2D) = "white" {}
        _NoiseMap ("Noise", 2D) = "gray" {}
        _Tint ("Ink colour", Color) = (0.03,0.03,0.03,1)
        _EdgeColor ("Burning rim (HDR)", Color) = (0,0,0,0)
        _EdgeWidth ("Rim width", Range(0.01,1)) = 0.12
        _NoiseAmt ("Noise amount", Range(0,1)) = 0.5
        _NoiseScale ("Noise scale", Float) = 1
        _Cutoff ("Cutoff", Range(0,1)) = 0.35
        _Sheen ("Wet sheen", Range(0,1)) = 0.25
    }
    SubShader
    {
        Tags { "RenderType"="TransparentCutout" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NoiseMap); SAMPLER(sampler_NoiseMap);
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST; float4 _Tint; float4 _EdgeColor; float _EdgeWidth, _NoiseAmt, _NoiseScale, _Cutoff, _Sheen;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; };
        struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float3 uv : TEXCOORD0; float3 posWS : TEXCOORD1; };
        Varyings vert (Attributes v)
        {
            Varyings o;
            VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
            o.positionCS = p.positionCS; o.posWS = p.positionWS; o.color = v.color;
            o.uv = float3(TRANSFORM_TEX(v.uv.xy, _BaseMap), v.uv.z);
            return o;
        }
        float Over(Varyings i)
        {
            float4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv.xy);
            float n = SAMPLE_TEXTURE2D(_NoiseMap, sampler_NoiseMap, i.uv.xy * _NoiseScale + i.uv.z * 7.31).r;
            float mask = tex.a * (1.0 - _NoiseAmt + _NoiseAmt * n);           // r3: never saturated - low-noise pockets inside the ink burn (rim) first
            return mask - (1.0 - i.color.a) - _Cutoff;
        }
        ENDHLSL
        Pass
        {
            Name "FsInkForward"
            Tags { "LightMode"="UniversalForward" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            half4 frag (Varyings i) : SV_Target
            {
                float over = Over(i);
                clip(over);
                float rim = 1.0 - saturate(over / _EdgeWidth);
                float3 col = _Tint.rgb * i.color.rgb;
                // a little wet sheen in the thick middle of the stroke (the ink is a liquid, not paper)
                float mid = 1.0 - abs(i.uv.y * 2.0 - 1.0);
                col += _Sheen * 0.12 * pow(saturate(mid), 6.0);
                col += _EdgeColor.rgb * rim;
                return half4(col, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On ColorMask R Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth
            half fragDepth (Varyings i) : SV_Target { clip(Over(i)); return 0; }
            ENDHLSL
        }
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragNormals
            half4 fragNormals (Varyings i) : SV_Target
            {
                clip(Over(i));
                float3 n = normalize(_WorldSpaceCameraPos - i.posWS);
                return half4(n, 0);
            }
            ENDHLSL
        }
    }
}
