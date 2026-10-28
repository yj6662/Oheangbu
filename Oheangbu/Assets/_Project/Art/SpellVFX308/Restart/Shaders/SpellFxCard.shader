// D308-153 restart: a painted / pre-rendered card of a spell look (route A = a rendered frame sequence in a Texture2DArray, route B = one painted still).
// Straight alpha, no depth write, depth-tested against the world (the far card stays behind the bodies, the near card in front of them).
// The still is animated here: it grows away from its waterline (_Rise), ripples (_Flow) and eats away at the end (_Dissolve).
Shader "Oheangbu/SpellFx/Card"
{
    Properties
    {
        _MainTex ("Still", 2D) = "white" {}
        _Seq ("Sequence", 2DArray) = "" {}
        _UseSeq ("Use sequence", Float) = 0
        _Frame ("Frame (fractional)", Float) = 0
        _Frames ("Frame count", Float) = 1
        _Alpha ("Alpha", Float) = 1
        _Rise ("Rise 0..1", Float) = 1
        _RiseLine ("Waterline (uv y)", Float) = 0.5
        _Flow ("Ripple", Float) = 0
        _Age ("Age seconds", Float) = 0
        _Dissolve ("Dissolve 0..1", Float) = 0
        _DepthSeq ("Depth sequence (r = view depth / range)", 2DArray) = "" {}
        _UseDepth ("Use depth", Float) = 0
        _DepthRange ("Depth range (m)", Float) = 12
        _Swirl ("Swirl angle (rad)", Float) = 0
        _SwirlC ("Swirl centre xy, radii zw (uv)", Vector) = (0.54, 0.45, 0.45, 0.28)
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Card"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma require 2darray
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            TEXTURE2D_ARRAY(_Seq); SAMPLER(sampler_Seq);
            TEXTURE2D_ARRAY(_DepthSeq); SAMPLER(sampler_DepthSeq);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST, _SwirlC;
                float _UseSeq, _Frame, _Frames, _Alpha, _Rise, _RiseLine, _Flow, _Age, _Dissolve, _UseDepth, _DepthRange, _Swirl, _ZTest;
            CBUFFER_END

            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 sp : TEXCOORD1; };

            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; o.sp = ComputeScreenPos(o.pos); return o; }

            float hash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
            float vnoise(float2 p)
            {
                float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                return lerp(lerp(hash(i), hash(i + float2(1, 0)), f.x), lerp(hash(i + float2(0, 1)), hash(i + float2(1, 1)), f.x), f.y);
            }

            half4 frag(V i) : SV_Target
            {
                float2 uv = i.uv;
                // route B whirl: the painted ring turns along its own ellipse (strongest on the band, none at the centre or far outside)
                if (abs(_Swirl) > 1e-4)
                {
                    float2 e = (uv - _SwirlC.xy) / _SwirlC.zw;
                    float rr = length(e), ang = atan2(e.y, e.x);
                    float band = smoothstep(0.45, 0.9, rr) * (1 - smoothstep(1.5, 2.2, rr));
                    ang -= _Swirl * band;
                    uv = _SwirlC.xy + float2(cos(ang), sin(ang)) * rr * _SwirlC.zw;
                    if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1) return 0;      // r3: never sample past the picture (r2 smeared its edge into stripes)
                }
                // grow away from the waterline: at rise r the card shows its band within r of the line, stretched back to full size
                float r = max(_Rise, 0.02);
                uv.y = _RiseLine + (uv.y - _RiseLine) / r;
                if (uv.y < 0 || uv.y > 1) return 0;
                // ripple: a travelling sideways sway, stronger high up (the crests), plus a little noise
                float n = vnoise(uv * float2(18, 9) + float2(_Age * 2.3, -_Age * 1.1));
                uv.x += _Flow * (0.004 * sin(uv.y * 38 + _Age * 7) + 0.004 * (n - 0.5));
                uv.y += _Flow * 0.003 * sin(uv.x * 29 - _Age * 5);
                half4 c;
                if (_UseSeq > 0.5)
                {
                    float f0 = clamp(floor(_Frame), 0, _Frames - 1), f1 = min(f0 + 1, _Frames - 1), k = saturate(_Frame - f0);
                    c = lerp(SAMPLE_TEXTURE2D_ARRAY(_Seq, sampler_Seq, uv, f0), SAMPLE_TEXTURE2D_ARRAY(_Seq, sampler_Seq, uv, f1), k);
                }
                else c = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                float d = vnoise(uv * float2(26, 14)) * 0.7 + vnoise(uv * float2(70, 40)) * 0.3;
                c.a *= _Alpha * smoothstep(_Dissolve - 0.08, _Dissolve + 0.02, d);
                // route A: the render's own view depth per pixel against the scene - bodies, caster and ground occlude the water exactly
                if (_UseDepth > 0.5)
                {
                    float f0 = clamp(floor(_Frame), 0, _Frames - 1), f1 = min(f0 + 1, _Frames - 1), k = saturate(_Frame - f0);
                    float mine = lerp(SAMPLE_TEXTURE2D_ARRAY(_DepthSeq, sampler_DepthSeq, i.uv, f0).r, SAMPLE_TEXTURE2D_ARRAY(_DepthSeq, sampler_DepthSeq, i.uv, f1).r, k) * _DepthRange;
                    float2 suv = i.sp.xy / i.sp.w;
                    float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                    c.a *= mine > 0.05 ? saturate((scene - mine) / 0.12 + 0.5) : 1;
                }
                return c;
            }
            ENDHLSL
        }
    }
}
