// 2026-10-30 초성 ㄱ(목): the bite flash of SpellFxVinePart - a camera-facing card at the hit: a bright core and a few sharp rays (PBZ "충돌 불꽃"),
// drawn as colour because the lab look tone-maps (bloom never reaches the frame). _Flash 1 -> 0 over the flash, _Seed turns the rays.
Shader "Oheangbu/SpellFx/BiteFlash"
{
    Properties
    {
        _Core ("Core (HDR)", Color) = (1.0, 1.15, 0.95, 1)
        _Ray ("Rays (HDR)", Color) = (0.6, 1.2, 0.7, 1)
        _Flash ("Flash 0..1", Range(0, 1)) = 0
        _Seed ("Seed", Float) = 0
        _Rays ("Ray count", Float) = 7
        _RayAmt ("Rays 0..1 (r2 off: Fable r1 #3 - a star reads as a game flare, not ink)", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent+5" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Core, _Ray;
                float _Flash, _Seed, _Rays, _RayAmt;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; };
            V Vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            half4 Frag(V i) : SV_Target
            {
                float2 p = i.uv * 2 - 1;
                float r = length(p), ang = atan2(p.y, p.x);
                float f = saturate(_Flash);
                float core = exp(-r * r * 60.0) * f + exp(-r * 9.0) * 0.35 * f;
                float rays = 0;
                [unroll] for (int k = 0; k < 2; k++)
                {
                    float n = _Rays + k * 4;
                    float a = ang * n * 0.5 + _Seed * (7.3 + k * 3.1);
                    float spike = pow(abs(cos(a)), 60.0 + k * 40.0);
                    rays += spike * saturate(1.0 - r / (0.95 - k * 0.35)) * (k == 0 ? 1.0 : 0.6);
                }
                rays *= f * f * _RayAmt;
                float3 col = _Core.rgb * core + _Ray.rgb * rays;
                float a = saturate(core * 0.9 + rays * 0.8);
                return half4(col * a, a * 0.85);
            }
            ENDHLSL
        }
    }
}
