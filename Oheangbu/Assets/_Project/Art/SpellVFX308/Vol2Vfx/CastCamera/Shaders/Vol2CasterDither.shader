// D308-150 V12 cast camera treatment B (LAB-ONLY prototype, TEST): the caster drawn as a screen-door ghost while it covers the line from the brush
// to the target. Vol2CastCameraDirector swaps the caster's materials to copies of this shader only inside one camera's render (base map + colour
// copied from the original), and sets _CastCamVisibility (1 = whole, 0 = gone). Opaque + clip: depth and normals follow the same holes, so the ink
// post still outlines what is left. Simple light (main light + ambient), no emission. The shadow is NOT dithered (the caster keeps its shadow).
Shader "Oheangbu/Vol2/CasterDither"
{
    Properties
    {
        _BaseMap("Base map", 2D) = "white" {}
        _BaseColor("Colour", Color) = (1,1,1,1)
        _Cutoff("Alpha cutoff (0 = off)", Range(0,1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2
        _CastCamVisibility("Visibility", Range(0,1)) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="AlphaTest" "RenderPipeline"="UniversalPipeline" }
        Cull [_Cull]
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST; half4 _BaseColor; half _Cutoff; half _Cull; half _CastCamVisibility;
        CBUFFER_END
        struct Attributes { float4 positionOS:POSITION; float3 normalOS:NORMAL; float2 uv:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings { float4 positionCS:SV_POSITION; float3 normalWS:TEXCOORD0; float2 uv:TEXCOORD1; float3 positionWS:TEXCOORD2; };
        Varyings vert(Attributes v)
        {
            Varyings o; UNITY_SETUP_INSTANCE_ID(v);
            o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
            o.positionCS = TransformWorldToHClip(o.positionWS);
            o.normalWS = TransformObjectToWorldNormal(v.normalOS);
            o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
            return o;
        }
        float Bayer4(float2 p)
        {
            uint2 q = (uint2)p & 3u;
            uint i = q.y * 4u + q.x;
            float m[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };
            return (m[i] + .5) / 16.0;
        }
        half4 Albedo(Varyings i) { return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv) * _BaseColor; }
        void Cut(Varyings i, half alpha)
        {
            if (_Cutoff > 0) clip(alpha - _Cutoff);
            clip(_CastCamVisibility - Bayer4(i.positionCS.xy) - .001);
        }
        ENDHLSL
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            half4 frag(Varyings i):SV_Target
            {
                half4 a = Albedo(i); Cut(i, a.a);
                float3 n = normalize(i.normalWS);
                Light l = GetMainLight();
                half3 lit = l.color * saturate(dot(n, l.direction)) * .85 + SampleSH(n);
                return half4(a.rgb * lit, 1);
            }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            half4 frag(Varyings i):SV_Target { Cut(i, Albedo(i).a); return i.positionCS.z; }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="DepthNormals" }
            ZWrite On
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            half4 frag(Varyings i):SV_Target { Cut(i, Albedo(i).a); return half4(normalize(i.normalWS), 0); }
            ENDHLSL
        }
        Pass
        {
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex vertShadow
            #pragma fragment fragShadow
            #pragma multi_compile_instancing
            float3 _LightDirection;
            Varyings vertShadow(Attributes v)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(v);
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                float4 cs = TransformWorldToHClip(ApplyShadowBias(o.positionWS, o.normalWS, _LightDirection));
                #if UNITY_REVERSED_Z
                cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                o.positionCS = cs;
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                return o;
            }
            half4 fragShadow(Varyings i):SV_Target { if (_Cutoff > 0) clip(Albedo(i).a - _Cutoff); return 0; }
            ENDHLSL
        }
    }
}
