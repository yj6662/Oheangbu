// D308-153 우 route H, round 9 (Fable r8: "보이는 왼쪽 밑단에 접지 띠가 전혀 없음 … 띠를 돔 메시 밑단이 아니라 바닥 데칼로"): the dome's mark on the ground.
// A flat annulus the part builds (uv.x = 0 inner .. 1 outer edge, the dome's foot at _Foot), lying 1 cm above the ground: a fine cream line at the foot, a soft glow spilling
// inward, ink grain. Same clock as the dome (_Age, _Fade, set by SpellFxHexDomePart). Transparent queue, no depth write. Look only.
Shader "Oheangbu/SpellFx/HexGround"
{
    Properties
    {
        _Line ("Foot line colour (HDR)", Color) = (1.6, 1.48, 1.2, 1)
        _Glow ("Inner glow colour / alpha", Color) = (0.95, 0.86, 0.66, 0.2)
        _Foot ("Foot position across the band (0..1)", Range(0, 1)) = 0.8
        _LineW ("Foot line width (band units)", Range(0.002, 0.1)) = 0.016
        _Age ("Age (s)", Float) = 0
        _Fade ("Fade 0..1", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-2" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Cull Off ZWrite Off ZTest LEqual Blend SrcAlpha OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Line, _Glow;
                float _Foot, _LineW, _Age, _Fade;
            CBUFFER_END
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 posWS : TEXCOORD1; };
            float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float VNoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
            }
            V Vert(A i) { V o; o.posWS = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.posWS); o.uv = i.uv; return o; }
            half4 Frag(V i) : SV_Target
            {
                float on = saturate(_Age / 0.12) * (1 - smoothstep(0.3, 0.62, _Fade));         // the mark is there before the dome rises and leaves first
                float x = i.uv.x;
                float fw = max(fwidth(x), 1e-4);
                float w = max(_LineW, fw * 0.6);
                float line_ = smoothstep(w + fw, w * 0.3, abs(x - _Foot));
                float glow = pow(saturate((x - (_Foot - 0.6)) / 0.6), 2.0) * step(x, _Foot) * _Glow.a;   // a soft light spilling inward from the foot, nothing outside
                float ink = 0.6 + 0.55 * VNoise(i.posWS.xz * 6.0);
                float a = saturate(line_ * ink + glow * ink) * on;
                float3 col = lerp(_Glow.rgb, _Line.rgb, saturate(line_ / max(line_ + glow, 1e-3)));
                return half4(col, a);
            }
            ENDHLSL
        }
    }
}
