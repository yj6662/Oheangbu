// 먹 획 셰이더 — SPEC-SPIKE-INK-LOOKDEV §5.
// 소비하는 계약(§2): UV0.U=획 진행률 / UV0.V=폭 방향(가장자리 0/1, 중앙 0.5) /
//   정점색 rgb=먹색·a=먹 농도 / UV1.x=점 탄생 시각(번짐 나이).
// 메시는 (1+_EdgeMargin)배 폭으로 온다(_widthMultiplier 훅) — V 재매핑으로 명목 가장자리를 안쪽에 둔다.
// 인식 불가침: 이 파일은 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkStroke"
{
    Properties
    {
        // 스텐실 Ref는 렌더 스테이트라 MaterialPropertyBlock로는 세팅 불가 —
        // 어댑터가 획별 머티리얼 인스턴스에 SetFloat로 넣는다(§11.1).
        [IntRange] _StencilRef("Stencil Ref (획별 인스턴스 세팅)", Range(0, 255)) = 0

        _NoiseSeed("Noise Seed (획별)", Float) = 0
        _NoiseScale("Noise Scale (갈필 결 밀도)", Float) = 40
        _CrackStrength("Crack Strength (갈필 세기)", Range(0, 1)) = 0.55
        _EdgeMargin("Edge Margin (번짐 여유 폭 비율)", Range(0, 0.5)) = 0.15
        _EdgeSoftness("Edge Softness (가장자리 부드러움)", Range(0.01, 0.6)) = 0.12
        _BleedTau("Bleed Tau (번짐 시정수 s — 작을수록 빠르게 번짐)", Float) = 0.45
        _BleedStartWidth("Bleed Start Width (그리는 순간 폭 비율)", Range(0.3, 1)) = 0.85

        _EdgeJitter("Edge Jitter (가장자리 요철 — 종이 위 먹의 거침)", Range(0, 0.3)) = 0.07

        _DissolveT("Dissolve (0=온전 → 1=증발 완료)", Range(0, 1)) = 0
        _FadeMul("Fade Multiplier (소멸 페이드)", Range(0, 1)) = 1
        _FlashColor("Flash Color (술식 속성색 — 담채)", Color) = (0, 0, 0, 1)
        _FlashStrength("Flash Strength (0..1 = 곡선×위력)", Range(0, 1)) = 0
        _FlashTint("Flash Tint (먹→속성색 물듦 비율)", Range(0, 1)) = 0.85
        _FlashAdd("Flash Add (HDR 가산 — Bloom이 집는 성분)", Float) = 1.5
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
            Name "InkStroke"
            Tags { "LightMode" = "UniversalForwardOnly" }

            // 프리멀티 알파 — 플래시를 알파와 독립된 가산 성분으로 얹기 위함(§11.1)
            Blend One OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            // 획 내 자기 겹침(캡·꺾임) 픽셀당 1회만 — 획 사이 겹침은 Ref가 달라 자연 누적(§5.3)
            Stencil
            {
                Ref [_StencilRef]
                Comp NotEqual
                Pass Replace
            }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color      : COLOR;      // rgb=먹색(sRGB), a=먹 농도
                float2 uv         : TEXCOORD0;  // U=진행률, V=폭 방향
                float2 uv1        : TEXCOORD1;  // x=점 탄생 시각
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float3 posOS      : TEXCOORD1;  // 노이즈 도메인(§11.1 — U 사용 금지)
                float  birth      : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                half  _StencilRef;
                half  _NoiseSeed;
                half  _NoiseScale;
                half  _CrackStrength;
                half  _EdgeMargin;
                half  _EdgeSoftness;
                half  _BleedTau;
                half  _BleedStartWidth;
                half  _EdgeJitter;
                half  _DissolveT;
                half  _FadeMul;
                half4 _FlashColor;
                half  _FlashStrength;
                half  _FlashTint;
                half  _FlashAdd;
            CBUFFER_END

            // 전역 시계 — 어댑터가 프레임당 1회 Shader.SetGlobalFloat("_InkNow", Time.time).
            // 번짐은 점 나이로 셰이더가 자율 진행한다(획별 갱신 없음 — §5.2)
            float _InkNow;

            // 정수 해시 value noise 2옥타브 — 텍스처 없이 갈필 결을 만든다(§5.1)
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

            float Fbm(float2 p)
            {
                return ValueNoise(p) * 0.6 + ValueNoise(p * 2.13 + 17.7) * 0.4;
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                o.posOS = v.positionOS.xyz;
                o.birth = v.uv1.x;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // Color32 정점색은 Linear 프로젝트에서 무변환으로 올라온다 — 여기서 교정(§11.1)
                half3 inkColor = SRGBToLinear(i.color.rgb);
                half density = i.color.a;

                // ---- 점 나이 번짐(§5.2): 그리는 순간 얇고, 빠르게 번지다 느려진다 ----
                float age = max(_InkNow - i.birth, 0.0);
                float bleed = 1.0 - exp(-age / max(_BleedTau, 1e-3));

                // V 재매핑: s = 중앙 0 → 명목 가장자리 1 → 물리 가장자리 1+margin
                float s = abs(i.uv.y - 0.5) * 2.0 * (1.0 + _EdgeMargin);
                // 가장자리 지터(ART-INK-LOOK §4.3) — 종이 위 먹의 요철. 농도와 무관하게 항상 있고,
                // 캡도 같은 V 규약이라 함께 거칠어진다(§4.4의 기필 반원 완화가 여기서 공짜로 온다)
                s += (Fbm(i.posOS.xy * _NoiseScale * 1.7 + _NoiseSeed + 31.7) - 0.5) * 2.0 * _EdgeJitter;
                // 보이는 가장자리가 시작 폭에서 여유 폭 끝까지 번져 나간다
                float edge = lerp(_BleedStartWidth, 1.0 + _EdgeMargin, bleed);
                // 번질수록 가장자리가 부드러워진다(먹이 종이에 스민 흔적)
                float soft = _EdgeSoftness * (1.0 + bleed);
                float edgeMask = 1.0 - smoothstep(edge - soft, edge, s);

                // ---- 갈필(§3-2): 농도가 낮을수록 가장자리부터 갈라진다. 번짐이 틈을 메운다 ----
                float noise = Fbm(i.posOS.xy * _NoiseScale + _NoiseSeed);
                float dryness = 1.0 - density;
                float crack = _CrackStrength * dryness * saturate(s);
                // 번짐이 틈을 메우는 건 「젖은 먹」뿐이다 — 마른 붓(먹 부족)의 갈필은 남아야
                // 먹 미터의 보조 언어(ART-UI: 먹 부족 시 갈필)가 시간이 지나도 읽힌다.
                crack *= 1.0 - 0.6 * bleed * density;
                // 증발 디졸브(§7): 같은 노이즈 임계를 전면으로 쓸어올려 갈필 틈부터 사라진다
                float threshold = saturate(crack + _DissolveT * 1.25);
                float inkMask = smoothstep(threshold, threshold + 0.08, noise);

                float alpha = density * edgeMask * inkMask * _FadeMul;
                // 사실상 투명한 프래그먼트는 스텐실 스탬프도 남기지 않는다(§11.1)
                clip(alpha - 0.004);

                // ---- 술식 플래시(ART-INK-LOOK §4.2·§4.5): 틴트가 주역, HDR 가산은 Bloom 몫 ----
                // 틴트: 먹이 속성색으로 「물든다」 — LDR 안전, 피크에서도 속성이 색으로 읽힌다(백색 포화 금지).
                // 가산: 담채 속성색 × _FlashAdd(HDR) — Bloom threshold를 넘겨 속성색 글로우가 번진다.
                half3 baseColor = lerp(inkColor, _FlashColor.rgb, saturate(_FlashTint * _FlashStrength));
                half3 rgb = baseColor * alpha;
                rgb += _FlashColor.rgb * (_FlashAdd * _FlashStrength * edgeMask * inkMask * _FadeMul);
                return half4(rgb, alpha);
            }
            ENDHLSL
        }
    }
}
