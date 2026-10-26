// 2026-10-27 common material pass (showcase120_plan.md step 2): showcase stone / earth (boulders, slabs, pillars, chunks).
// Batches 1 - 4 read as dark backlit boxes (마 모 막 목 묵 먼 만 몬 문 머): URP Lit stone seen from behind the caster faces away from the sun.
// Stays OPAQUE on purpose (stone belongs to the world: InkWash297 still grades it and draws its ink lines from the depth / normals passes,
// which come from URP Lit unchanged). Its own lighting:
//  * wrapped sun light (half Lambert: the shadow side is dim, never black) + the scene's ambient,
//  * a soft fill from the camera side (the faces we look at are the ones turned away from the sun),
//  * a warm rim at the silhouette (stone edges catch light: reads the chipped form against the ground),
//  * triplanar rock texture (no stretched uv on the chipped facets),
//  * optional emission (the crack / vein glow of the glyph), through the same triplanar.
Shader "Oheangbu/FreeShowcase/Stone"
{
    Properties
    {
        _BaseMap ("Rock", 2D) = "white" {}
        _BaseColor ("Tint", Color) = (1, 1, 1, 1)
        _EmissionMap ("Glow mask", 2D) = "black" {}
        _EmissionColor ("Glow (HDR)", Color) = (0, 0, 0, 0)
        _TriScale ("Triplanar scale (1 / m)", Float) = 1.7
        _Wrap ("Light wrap", Range(0, 1)) = 0.55
        _Fill ("Camera fill", Range(0, 1)) = 0.32
        _RimColor ("Rim (HDR)", Color) = (0.95, 0.78, 0.6, 1)
        _Rim ("Rim strength", Range(0, 2)) = 0.85
        // read by the URP Lit passes used below (depth, depth-normals, shadows)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _Smoothness ("Smoothness", Range(0, 1)) = 0.1
        _Metallic ("Metallic", Range(0, 1)) = 0
        _BumpScale ("Normal scale", Float) = 1
        _BumpMap ("Normal map", 2D) = "bump" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "FsStone"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                float4 _BaseColor, _EmissionColor, _RimColor;
                float _TriScale, _Wrap, _Fill, _Rim, _Cutoff, _Smoothness, _Metallic, _BumpScale;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 positionWS : TEXCOORD0; float3 normalWS : TEXCOORD1; float3 normalOS : TEXCOORD2; float3 positionOS : TEXCOORD3; float4 color : COLOR; };

            Varyings vert (Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS; o.positionWS = p.positionWS;
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.normalOS = v.normalOS; o.positionOS = v.positionOS.xyz;
                o.color = v.color;
                return o;
            }

            float4 Tri(TEXTURE2D_PARAM(tex, smp), float3 p, float3 n)
            {
                float3 w = pow(abs(n), 4.0); w /= max(1e-4, w.x + w.y + w.z);
                return SAMPLE_TEXTURE2D(tex, smp, p.zy) * w.x + SAMPLE_TEXTURE2D(tex, smp, p.xz) * w.y + SAMPLE_TEXTURE2D(tex, smp, p.xy) * w.z;
            }

            half4 frag (Varyings i) : SV_Target
            {
                float3 N = normalize(i.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
                // object-space triplanar (the texture travels with a tumbling chunk), scaled by the object's size
                float3 tp = i.positionOS * _TriScale * length(float3(UNITY_MATRIX_M[0].x, UNITY_MATRIX_M[1].x, UNITY_MATRIX_M[2].x));
                float3 alb = Tri(TEXTURE2D_ARGS(_BaseMap, sampler_BaseMap), tp, i.normalOS).rgb * _BaseColor.rgb;
                // r2 (material pass r1 read rust-red with big leopard blotches on the red ground): a stonier grey-brown
                alb = lerp(dot(alb, float3(0.3, 0.55, 0.15)).xxx * float3(1.02, 0.98, 0.92), alb, 0.55);
                float4 sc = TransformWorldToShadowCoord(i.positionWS);
                Light L = GetMainLight(sc);
                float ndl = dot(N, L.direction);
                float wrap = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                float3 sun = L.color * wrap * lerp(1.0, L.shadowAttenuation, 0.75);
                float3 amb = SampleSH(N);
                float fill = saturate(dot(N, V)) * _Fill;
                float f = 1.0 - saturate(dot(N, V));
                float3 rim = _RimColor.rgb * pow(f, 3.0) * _Rim * (0.4 + 0.6 * saturate(dot(N, float3(0, 1, 0)) * 0.5 + 0.5));
                float3 col = alb * (sun + amb * 1.1 + fill * float3(1.0, 0.95, 0.88)) + rim * (0.35 + alb);
                col += Tri(TEXTURE2D_ARGS(_EmissionMap, sampler_EmissionMap), tp, i.normalOS).rgb * _EmissionColor.rgb;
                return half4(col, 1.0);
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/SHADOWCASTER"
        UsePass "Universal Render Pipeline/Lit/DEPTHONLY"
        UsePass "Universal Render Pipeline/Lit/DEPTHNORMALS"
    }
}
