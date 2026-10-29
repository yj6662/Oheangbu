// D308-153c sprite waves (variants R / I): an unlit cut-out card for painted 2-D wave elements (mound / crest / claw / splash / ink stroke) that the part stands round the caster.
// The sprite colours are shown exactly as painted (only an optional _Tint). Alpha is clipped at _Cutoff and the edge is then softened over _SoftW, so overlapping cards in 2 - 3 depth
// rows sort by the depth buffer (ZWrite on) and not by draw order; the ink stroke material turns ZWrite off and uses a low cutoff so brush edges stay soft.
// Per-card state rides in the vertex colour (the part batches every card of one texture into one mesh): r = local erosion 0..1, g = noise seed 0..1, b = unused, a = fade.
// _Erode is a global extra erosion (kept 0 by the part). uv0 = the card's own 0..1 (v up).
// Rules kept (hard experience of the old routes): Queue Transparent-1 and the forward pass ONLY, no DepthOnly / DepthNormals passes (the ink-wash and fog post must not see the card
// as scene geometry: it would paint it as a dark silhouette); no `out`-param function inside ?: (both arms execute). Look only.
Shader "Oheangbu/SpellFx/Sprite"
{
    Properties
    {
        _MainTex ("Sprite (rgba, straight alpha)", 2D) = "white" {}
        _Tint ("Tint (rgb multiplies; default white = as painted)", Color) = (1, 1, 1, 1)
        _Cutoff ("Alpha cutoff", Range(0, 1)) = 0.5
        _SoftW ("Edge softness above the cutoff", Range(0.02, 1)) = 0.3
        _Erode ("Extra erosion 0..1", Range(0, 1)) = 0
        _NoiseScale ("Erosion noise scale (per card)", Float) = 6
        [HideInInspector] _ZWrite ("ZWrite", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent-1" "RenderType" = "Transparent" "IgnoreProjector" = "True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
        CBUFFER_START(UnityPerMaterial)
            float4 _MainTex_ST;
            half4 _Tint;
            float _Cutoff, _SoftW, _Erode, _NoiseScale, _ZWrite;
        CBUFFER_END

        struct Attributes { float4 pos : POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; };
        struct Varyings { float4 pos : SV_POSITION; float4 col : COLOR; float2 uv : TEXCOORD0; };

        Varyings Vert(Attributes i)
        {
            Varyings o;
            o.pos = TransformObjectToHClip(i.pos.xyz);      // the part builds every mesh in WORLD space under a root at the origin
            o.col = i.col; o.uv = i.uv;
            return o;
        }

        float Hash21(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
        float VNoise(float2 p)
        {
            float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
            return lerp(lerp(Hash21(i), Hash21(i + float2(1, 0)), f.x), lerp(Hash21(i + float2(0, 1)), Hash21(i + float2(1, 1)), f.x), f.y);
        }
        ENDHLSL

        Pass
        {
            Name "Sprite"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite [_ZWrite]
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            half4 Frag(Varyings i) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
                float er = saturate(max(_Erode, i.col.r));
                float seed = i.col.g;
                // noise in the card's own space: two octaves, a different pattern per card (seed)
                float n = VNoise(i.uv * _NoiseScale + seed * 37.0) * 0.65 + VNoise(i.uv * _NoiseScale * 3.1 + seed * 11.0) * 0.35;
                float th = lerp(-0.12, 1.1, er);                                   // er 0 keeps everything, er 1 removes everything
                float a = tex.a * smoothstep(th - 0.10, th + 0.04, n);
                clip(a - _Cutoff);
                float soft = saturate((a - _Cutoff) / max(_SoftW, 0.02));
                return half4(tex.rgb * _Tint.rgb, soft * i.col.a * _Tint.a);
            }
            ENDHLSL
        }
        // no DepthOnly / DepthNormals passes on purpose (see the header)
    }
    FallBack Off
}
