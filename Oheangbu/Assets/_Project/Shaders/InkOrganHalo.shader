// 적 속성 기관 먹 테 — SPEC-PLAYTEST-306 #12 · D306 (ART-INK 광원 3등급의 2등급 = 적의 술식 순간 발광).
// Unlit 쿼드(카메라를 본다). 모습 = 역번짐: 먹 원판의 가운데가 물러나며 한지가 드러나고, 그 가운데에 속성색 심이 선다.
//   _Open  0 → 1  먹 고리가 물러남(오름) · _Pulse 절정 맥동(고리가 닫힘) · _Blot 먹 얼룩으로 꺼짐(방어 획 도착·가라앉음)
//   _Burst 도착 터짐(고리가 부풀며 먹 점으로 흩어짐) · _Break 개화(고리가 깨짐)
// 발광 상한(InkLightSource와 같은 규율): 색 = 팔레트 속성색 × _Brightness(≤ 1), 한지 × _PaperBrightness(≤ 1), 최종 saturate —
// HDR 가산 없음, 블룸 문턱 아래. 「빛은 전구가 아니라 먹이다」 — 빛의 번짐은 밝기가 아니라 먹이 물러나는 모양으로 보인다.
// 지속 발광 금지: 켜짐은 공격 준비부터 충돌 직후까지만(EnemyElementTelegraph가 _Alpha로 끈다). 오염된 몸은 빛나지 않는다.
// DepthNormals 광원 마커(−1)를 쓰지 않는다 — 광원이 아니다(포스트 씻김이 톤을 눌러도 상한 쪽이라 안전).
// 한글 [Tooltip] 금지. 인식 불가침: 표현 전용.
Shader "Oheangbu/InkOrganHalo"
{
    Properties
    {
        _Tint("Tint (Element colour, ElementPaletteSO, LDR)", Color) = (0.306, 0.502, 0.412, 1)
        _Ink("Ink", Color) = (0.165, 0.149, 0.133, 1)
        _Paper("Paper", Color) = (0.969, 0.945, 0.894, 1)
        _Open("Open (ink ring recedes)", Range(0, 1)) = 0
        _Pulse("Pulse (peak: ring closes)", Range(0, 1)) = 0
        _Blot("Blot (goes out as ink)", Range(0, 1)) = 0
        _Burst("Burst (arrival)", Range(0, 1)) = 0
        _Break("Break (blossom: ring breaks)", Range(0, 1)) = 0
        _Alpha("Alpha", Range(0, 1)) = 0
        _Brightness("Brightness (LDR cap <= 1)", Range(0, 1)) = 0.85
        _PaperBrightness("Paper Brightness (<= 1)", Range(0, 1)) = 0.78
        _Seed("Seed", Float) = 0
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
            Name "InkOrganHalo"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _Ink;
                half4 _Paper;
                half _Open;
                half _Pulse;
                half _Blot;
                half _Burst;
                half _Break;
                half _Alpha;
                half _Brightness;
                half _PaperBrightness;
                float _Seed;
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
                return lerp(lerp(Hash(i), Hash(i + float2(1, 0)), f.x), lerp(Hash(i + float2(0, 1)), Hash(i + float2(1, 1)), f.x), f.y);
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
                float2 p = i.uv * 2.0 - 1.0;
                float r = length(p);
                float2 dir = p / max(r, 1e-4);
                // 먹 가장자리 요철 — 종이 위 먹의 거침(방향 벡터로 뽑아 둘레에 이음매가 없다)
                float edge = (ValueNoise(dir * 3.0 + _Seed) - 0.5) * 0.14
                           + (ValueNoise(dir * 7.0 + _Seed * 1.7 + r * 2.0) - 0.5) * 0.06;
                float rr = r + edge;

                float open = saturate(_Open), pulse = saturate(_Pulse), blot = saturate(_Blot);
                float burst = saturate(_Burst), brk = saturate(_Break);

                // 반지름: 한지 구멍(역번짐) · 속성색 심 · 먹 원판 바깥
                float paperR = 0.60 * open * (1.0 - 0.38 * pulse) * (1.0 - blot);
                float coreR = paperR * (0.48 + 0.18 * pulse);
                // 쿼드 안에 머문다(최대 0.92 < 1): 터짐의 부풂은 스크립트의 쿼드 크기가 맡는다 — 사각 가장자리에 잘리지 않게
                float outerR = 0.60 + 0.12 * open + 0.08 * pulse + 0.12 * burst;

                float inkMask = 1.0 - smoothstep(outerR - 0.07, outerR, rr);
                float paperMask = 1.0 - smoothstep(paperR - 0.06, paperR, rr);
                float coreMask = 1.0 - smoothstep(coreR * 0.55, coreR, r);

                // 개화 = 고리 조각이 빠진다(각도 마스크) · 터짐 = 바깥으로 먹 점이 흩어진다
                float seg = ValueNoise(dir * 2.2 + _Seed * 3.1);
                float ringZone = inkMask * (1.0 - paperMask);
                float keep = 1.0 - brk * smoothstep(0.45, 0.6, seg);
                float speck = step(0.78, ValueNoise(p * 9.0 + _Seed * 5.3)) * burst
                            * smoothstep(outerR - 0.05, outerR + 0.05, r) * (1.0 - smoothstep(outerR + 0.1, outerR + 0.34, r));

                half3 tint = saturate(_Tint.rgb) * _Brightness;
                half3 paper = lerp(saturate(_Paper.rgb) * _PaperBrightness, tint, 0.22);
                half3 col = lerp(_Ink.rgb, paper, paperMask);
                col = lerp(col, tint, coreMask * paperMask);

                float alpha = max(inkMask * lerp(1.0, keep, ringZone), speck);
                alpha *= _Alpha * (1.0 - smoothstep(0.55, 1.0, blot)) * (1.0 - smoothstep(0.9, 1.0, r));
                clip(alpha - 0.004);
                return half4(saturate(col), saturate(alpha));
            }
            ENDHLSL
        }
    }
}
