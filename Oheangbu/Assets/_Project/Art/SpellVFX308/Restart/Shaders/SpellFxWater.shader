// D308-153 route C (in-engine, real time): the 우 wave sheet. Deep glassy teal body that thins to see-through at grazing depth, sky reflection by fresnel,
// a sharp sun glint, crest foam from vertex colour r (x animated noise lace), visibility from vertex colour a. Soft where it meets the ground (scene depth).
// No refraction: the project's opaque texture is off and turning it on is a project-wide change.
Shader "Oheangbu/SpellFx/Water"
{
    Properties
    {
        _Deep ("Deep", Color) = (0.07, 0.13, 0.15, 1)
        _Shallow ("Shallow", Color) = (0.30, 0.41, 0.43, 1)
        _Sky ("Sky reflection", Color) = (0.82, 0.80, 0.74, 1)
        _Foam ("Foam", Color) = (0.95, 0.96, 0.95, 1)
        _Opacity ("Opacity", Range(0, 1)) = 0.86
        _Age ("Age", Float) = 0
        _Lace ("Foam lace scale", Float) = 9
        _Soft ("Ground soft fade (m)", Float) = 0.25
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" "RenderType" = "Transparent" }
        Pass
        {
            Name "Water"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Deep, _Shallow, _Sky, _Foam;
                float _Opacity, _Age, _Lace, _Soft;
            CBUFFER_END

            struct A { float4 pos : POSITION; float3 n : NORMAL; float4 col : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; float3 wp : TEXCOORD0; float3 n : TEXCOORD1; float4 col : COLOR; float2 uv : TEXCOORD2; float4 sp : TEXCOORD3; };

            V vert(A i)
            {
                V o; VertexPositionInputs p = GetVertexPositionInputs(i.pos.xyz);
                o.pos = p.positionCS; o.wp = p.positionWS; o.n = TransformObjectToWorldNormal(i.n); o.col = i.col; o.uv = i.uv; o.sp = ComputeScreenPos(p.positionCS);
                return o;
            }

            float hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            float vnoise(float3 p)
            {
                float3 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
                float a = lerp(lerp(hash(i), hash(i + float3(1, 0, 0)), f.x), lerp(hash(i + float3(0, 1, 0)), hash(i + float3(1, 1, 0)), f.x), f.y);
                float b = lerp(lerp(hash(i + float3(0, 0, 1)), hash(i + float3(1, 0, 1)), f.x), lerp(hash(i + float3(0, 1, 1)), hash(i + float3(1, 1, 1)), f.x), f.y);
                return lerp(a, b, f.z);
            }
            float fbm(float3 p) { return vnoise(p) * 0.55 + vnoise(p * 2.1) * 0.28 + vnoise(p * 4.3) * 0.17; }

            half4 frag(V i, bool front : SV_IsFrontFace) : SV_Target
            {
                float3 V_ = normalize(GetWorldSpaceViewDir(i.wp));
                float3 N = normalize(i.n); if (dot(N, V_) < 0) N = -N;
                // ripple normals: the surface streams along the flow (uv.x = along the ring, scrolled by the part)
                float3 q = float3(i.uv.x * 3.0, i.uv.y * 6.0, _Age * 0.9);
                float e = 0.05, h0 = fbm(q * 2.0);
                float hx = fbm((q + float3(e, 0, 0)) * 2.0), hy = fbm((q + float3(0, e, 0)) * 2.0);
                float3 T = normalize(cross(N, float3(0, 1, 0)) + 1e-4), B = normalize(cross(T, N));
                N = normalize(N + (T * (h0 - hx) + B * (h0 - hy)) * 6.0);

                float fres = pow(1 - saturate(dot(N, V_)), 4);
                Light sun = GetMainLight();
                float3 H = normalize(sun.direction + V_);
                float spec = pow(saturate(dot(N, H)), 220) * 3.0;
                float wrap = saturate(dot(N, sun.direction) * 0.5 + 0.5);
                half3 body = lerp(_Deep.rgb, _Shallow.rgb, saturate(i.uv.y * 1.1)) * (0.55 + 0.6 * wrap) * sun.color;
                half3 c = lerp(body, _Sky.rgb, fres * 0.75) + spec * sun.color;

                // foam: vertex r is the crest/lip weight, the lace thresholds animated noise
                float lace = fbm(float3(i.uv.x * _Lace, i.uv.y * _Lace * 1.8, _Age * 1.6));
                float foam = saturate(smoothstep(1.12, 1.3, lace + i.col.r)) * step(0.01, i.col.r);      // r3: lace, not slabs
                c = lerp(c, _Foam.rgb * (0.75 + 0.35 * wrap), foam);

                float a = lerp(_Opacity, 1.0, max(foam, fres * 0.6)) * i.col.a;
                // soft against the ground
                float2 suv = i.sp.xy / i.sp.w;
                float scene = LinearEyeDepth(SampleSceneDepth(suv), _ZBufferParams);
                float self = LinearEyeDepth(i.pos.z, _ZBufferParams);
                a *= saturate((scene - self) / max(_Soft, 1e-3));
                return half4(c, a);
            }
            ENDHLSL
        }
    }
}
