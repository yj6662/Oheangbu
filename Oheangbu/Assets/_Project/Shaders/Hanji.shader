// 한지 머티리얼 — SPEC-SPIKE-INK-LOOKDEV §5.4.
// 한지색(#F7F1E4 — ART-COLOR LOCKED) 위에 저주파 섬유 노이즈를 얹은 정적 배경.
// Recraft 생성 텍스처로의 교체는 선택 사항(키 기입 후) — 그때도 이 셰이더의 색 규약은 유지.
Shader "Oheangbu/Hanji"
{
    Properties
    {
        _BaseColor("Base Color (한지 소지)", Color) = (0.9686, 0.9451, 0.8941, 1) // #F7F1E4
        _FiberColor("Fiber Color (섬유 결)", Color) = (0.9059, 0.8706, 0.8000, 1)
        _FiberScale("Fiber Scale", Float) = 18
        _FiberStrength("Fiber Strength", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "Hanji"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _FiberColor;
                half  _FiberScale;
                half  _FiberStrength;
            CBUFFER_END

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float ValueNoise(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(
                    lerp(Hash(i), Hash(i + float2(1, 0)), f.x),
                    lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 이방성 도메인 — 한지 섬유는 한 방향으로 길게 눕는다
                float n = ValueNoise(i.uv * float2(_FiberScale, _FiberScale * 3.7)) * 0.65
                        + ValueNoise(i.uv * float2(_FiberScale * 4.1, _FiberScale * 9.3)) * 0.35;
                half3 rgb = lerp(_BaseColor.rgb, _FiberColor.rgb, n * _FiberStrength);
                return half4(rgb, 1);
            }
            ENDHLSL
        }
    }
}
