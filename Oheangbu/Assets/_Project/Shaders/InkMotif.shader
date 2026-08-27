// 술식 모티프 셰이더 — SPEC-ART-INK-LOOK §4.5 확장(속성 모티프 Bloom).
// 입력 텍스처는 「흰 바탕 위 수묵담채 그림」이다(Recraft 생성) — 밝기(luma)가 곧 여백이므로
// 알파를 (1 - luma)로 만든다. 배경 제거 후처리가 필요 없고, 먹의 농담이 그대로 투명도가 된다.
// 그림 원색(담채)을 살리되 속성색으로 살짝 물들이고, HDR 부스트로 Bloom이 집게 한다.
Shader "Oheangbu/InkMotif"
{
    Properties
    {
        _MainTex("Motif (흰 바탕 수묵담채)", 2D) = "white" {}
        _Tint("Tint (속성색)", Color) = (1, 1, 1, 1)
        _TintMix("Tint Mix (원색→속성색 물듦)", Range(0, 1)) = 0.3
        _Desaturate("Desaturate (담채 절제 — 글자보다 튀지 않게)", Range(0, 1)) = 0.5
        _Opacity("Opacity (전체 투명도 — 글자 가독 우선)", Range(0, 1)) = 0.55
        _Boost("HDR Boost (Bloom이 집는 배율)", Float) = 1.3
        _AlphaGain("Alpha Gain (여백 판정 민감도)", Float) = 1.25
        _Strength("Strength (0..1 — 플래시 타임라인)", Range(0, 1)) = 0
        _FadeMul("Fade Multiplier", Range(0, 1)) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkMotif"
            Tags { "LightMode" = "UniversalForwardOnly" }
            Blend One OneMinusSrcAlpha // 프리멀티 — 획(InkStroke)과 같은 합성 계열
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                half4 _Tint;
                half  _TintMix;
                half  _Desaturate;
                half  _Opacity;
                half  _Boost;
                half  _AlphaGain;
                half  _Strength;
                half  _FadeMul;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings   { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv).rgb;
                // 흰 바탕 = 여백: 밝을수록 투명. 먹·담채가 진할수록 불투명.
                // _Opacity로 전체를 반투명하게 — 모티프는 글자의 보조 언어이지 주인공이 아니다(가독 우선)
                half luma = dot(tex, half3(0.299, 0.587, 0.114));
                half alpha = saturate((1.0 - luma) * _AlphaGain) * _Opacity * _Strength * _FadeMul;
                clip(alpha - 0.004);

                // 속성색으로 살짝 물들인 뒤 채도를 눌러 담채로 가라앉힌다 — 글자보다 튀지 않게
                half3 rgb = lerp(tex, _Tint.rgb, _TintMix);
                half grey = dot(rgb, half3(0.299, 0.587, 0.114));
                rgb = lerp(rgb, grey.xxx, _Desaturate) * _Boost;
                return half4(rgb * alpha, alpha);
            }
            ENDHLSL
        }
    }
}
