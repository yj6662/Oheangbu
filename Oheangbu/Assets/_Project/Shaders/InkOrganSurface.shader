// 적 기관 표면 덧칠 — SPEC-TELEGRAPH-ORGAN-308 · D308-4 (ART-INK 광원 3등급의 2등급 = 적의 술식 순간 발광) + D308-4d 결정 둘레 마석 오염.
// 예고는 새 오브젝트가 아니라 적 모델의 기관 표면에서 선다: EnemyElementTelegraph(OrganSurfaceOverlay)가 기관이 가리키는
// 렌더러의 재질 배열 끝에 이 재질 사본을 붙이면, 하나뿐인 서브메시가 이 패스로 한 번 더 그려진다(투명 큐, 깊이·그림자 패스 없음).
// 모습 = InkOrganHalo의 역번짐 그대로, 반지름 식을 기관 구 거리 d(= |화소 − 기관 중심| / 반지름)에 옮겼다:
//   먹 원판의 가운데가 물러나며 한지가 드러나고 가운데 속성색 심이 선다 · 절정에 고리가 닫힌다(_Pulse) ·
//   방어 성공 획이 닿으면 먹 얼룩으로 꺼진다(state.z blot, state.w burst) · 개화는 고리가 깨진다(_Break).
// 영역 = 마스크 × (1 − smoothstep(1 − 깃, 1, d)). 마스크: 부위(Part)·구(Sphere) 기관 = 1, 마스크(Mask) 기관 = _MaskTex(몸 UV0, R8 선형).
// D308-4d 오염 층(몸 렌더러, 평소에도 붙음 — 거리 예산 안): 값 v = 결 × 결정 가까움(_ContamTex 몸 UV0 R8, contam308.py 측지 굽기;
//   _ContamMode 2 = UV가 쓸 수 없는 몸의 대체 — 결정 중심 월드 거리 + 3D 잡음 결). 있음 = smoothstep(.02, .06, v).
//   평소: 몸 색 × (1 − 어둡힘) + 오염 먹(팔레트 검보라·묵색) × 섞음 × 어둡힘 — 곱해 누르는 무광 번짐(밝히지 않는다, 무채, 발광 없음).
//   예고 창: 측지 거리 비 t ≈ 1 − v 가 번짐 앞머리(_ContamState.x)보다 작은 결에 속성색이 선다(같은 LDR 상한) — 결정에서 바깥으로.
//   _ContamState.w = 평소 어둡힘의 세기(거리로 풀림: 붙이는 거리에서 0 — 붙고 떨어질 때 튀지 않는다). 속성 물듦은 세기를 타지 않는다.
//   밉 상한(_ContamMaxLod): 몸 UV0는 작은 섬이 많은 아틀라스라 거친 밉이 오염 섬을 이웃 섬(몸의 다른 곳)으로 번지게 한다 — 밉 단계를 막는다.
// 합성: 미리 곱한 알파(Blend One OneMinusSrcAlpha). 오염(어둡힘) → 오염 물듦 → 기관(먼 것부터) 순으로 위에 얹는다.
// 발광 상한(InkLightSource·InkOrganHalo와 같은 규율): 속성색 × _Brightness(≤ 1), 한지 × _PaperBrightness(≤ 1), 최종(미리 곱하기 전) 색의
// 최대 채널 ≤ _Brightness — HDR 가산 없음, 블룸 문턱 아래(LDR ≤ .85). 「빛은 전구가 아니라 먹이다」. 「오염은 빛나지 않는다」.
// InkWash297(BeforeRenderingTransparents) 뒤에 그려지므로 속성색이 씻기지 않는다. ShadowCaster·DepthOnly·DepthNormals 패스가 없어
// 깊이·법선 먹선과 그림자에 끼지 않는다. 키워드 변형 없음(분기는 uniform) — 빌드에서 벗겨질 shader_feature가 없다.
// 기관 배열은 material.SetVectorArray(렌더러마다 런타임 사본, MPB 아님)로 쓴다: FolkloreReadability298의 SH MPB와 겹치지 않는다.
// 배열은 UnityPerMaterial 밖에 둔다(재질 배열 + SRP Batcher 비호환 = 일반 경로, 켜진 렌더러는 많아야 몇 개).
// 한글 [Tooltip] 금지. 인식 불가침: 표현 전용.
Shader "Oheangbu/InkOrganSurface"
{
    Properties
    {
        _Tint("Tint (Element colour, ElementPaletteSO, LDR)", Color) = (0.306, 0.502, 0.412, 1)
        _Ink("Ink", Color) = (0.165, 0.149, 0.133, 1)
        _Paper("Paper", Color) = (0.969, 0.945, 0.894, 1)
        _Pulse("Pulse (peak: ring closes)", Range(0, 1)) = 0
        _Break("Break (blossom: ring breaks)", Range(0, 1)) = 0
        _Seed("Seed", Float) = 0
        _Brightness("Brightness (LDR cap <= 1)", Range(0, 1)) = 0.85
        _PaperBrightness("Paper Brightness (<= 1)", Range(0, 1)) = 0.78
        [NoScaleOffset] _MaskTex("Mask (R8 linear, body UV0)", 2D) = "white" {}
        _MaskMode("Mask Mode (0 part or sphere / 1 texture)", Float) = 0
        _MaskThreshold("Mask Threshold", Range(0, 1)) = 0.1
        _RingScale("Ring Scale (ring radius = organ radius x this)", Range(0.2, 2)) = 1
        _RingWidth("Ring Width (d units)", Range(0.02, 0.6)) = 0.18
        _Feather("Feather (region edge, d units)", Range(0.01, 0.6)) = 0.12
        _OrganCount("Organ Count (<= 16)", Float) = 0
        _ZBias("Depth Bias (Offset units)", Float) = -1
        _DebugView("Debug View (0 normal / 1 organ region white / 2 contamination region white)", Float) = 0
        [NoScaleOffset] _ContamTex("Contamination (R8 linear, body UV0: veins x closeness)", 2D) = "black" {}
        _ContamMode("Contamination Mode (0 none / 1 texture / 2 world sphere)", Float) = 0
        _ContamSphere("Contamination centre xyz + radius (m)", Vector) = (0, 0, 0, 1)
        _ContamState("Contamination flow front, tint level, blot, strength", Vector) = (0, 0, 0, 1)
        _ContamColor("Contamination ink (ElementPaletteSO corrupt ink)", Color) = (0.2, 0.169, 0.212, 1)
        _ContamDark("Contamination darken near, far, ink mix, tint alpha", Vector) = (0.72, 0.3, 0.35, 0.75)
        _ContamFlowSoft("Contamination flow front softness", Range(0.01, 0.5)) = 0.15
        _ContamMaxLod("Contamination max mip level (atlas bleed guard)", Range(0, 6)) = 2
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
            Name "InkOrganSurface"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset [_ZBias], [_ZBias]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            #define ORGAN_MAX 16

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 noisePos : TEXCOORD2;   // object-anchored metres (the pattern rides with the body, not the world)
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _Tint;
                half4 _Ink;
                half4 _Paper;
                half _Pulse;
                half _Break;
                float _Seed;
                half _Brightness;
                half _PaperBrightness;
                half _MaskMode;
                half _MaskThreshold;
                half _RingScale;
                half _RingWidth;
                half _Feather;
                float _OrganCount;
                float _ZBias;
                half _DebugView;
                half _ContamMode;
                float4 _ContamSphere;
                float4 _ContamState;
                half4 _ContamColor;
                half4 _ContamDark;
                half _ContamFlowSoft;
                float _ContamMaxLod;
                float4 _ContamTex_TexelSize;   // x 1/w, y 1/h, z w, w h (set by Unity for the bound texture)
            CBUFFER_END

            // per organ (OrganSurfaceOverlay): sphere = world centre xyz + radius (m) · state = level, open, blot, burst ·
            // param = useMask (0/1), seed offset, -, -
            float4 _OrganSphere[ORGAN_MAX];
            float4 _OrganState[ORGAN_MAX];
            float4 _OrganParam[ORGAN_MAX];

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);
            TEXTURE2D(_ContamTex);
            SAMPLER(sampler_ContamTex);

            float Hash3(float3 p)
            {
                p = frac(p * 0.3183099 + 0.1);
                p *= 17.0;
                return frac(p.x * p.y * p.z * (p.x + p.y + p.z));
            }

            float Noise3(float3 x)
            {
                float3 i = floor(x);
                float3 f = frac(x);
                f = f * f * (3.0 - 2.0 * f);
                float a = lerp(Hash3(i), Hash3(i + float3(1, 0, 0)), f.x);
                float b = lerp(Hash3(i + float3(0, 1, 0)), Hash3(i + float3(1, 1, 0)), f.x);
                float c = lerp(Hash3(i + float3(0, 0, 1)), Hash3(i + float3(1, 0, 1)), f.x);
                float d = lerp(Hash3(i + float3(0, 1, 1)), Hash3(i + float3(1, 1, 1)), f.x);
                return lerp(lerp(a, b, f.y), lerp(c, d, f.y), f.z);
            }

            // D308-4d _ContamMode 2 (no usable UV0): veins from the world distance to the crystal (t) and object-anchored ridged noise —
            // the same encoding as the baked mask (v = coverage x (1 - .96 t)); crust next to the crystal, lines thinning and breaking outward
            float ContamSphereValue(float3 positionWS, float3 noisePos)
            {
                float radius = max(_ContamSphere.w, 1e-3);
                float t = distance(positionWS, _ContamSphere.xyz) / radius;
                if (t >= 1.2) return 0.0;
                float3 q = noisePos / (0.11 * radius);
                float n = Noise3(q + 3.7);
                float ridge = 1.0 - abs(2.0 * n - 1.0);
                float tn = t + 0.12 * (Noise3(noisePos / (0.45 * radius) + 9.1) - 0.5);
                float w = lerp(0.40, 0.08, saturate(tn));
                float vein = smoothstep(1.0 - w, 1.0 - w * 0.5, ridge);
                float crust = 1.0 - smoothstep(0.06, 0.16, tn);
                float brk = Noise3(noisePos / (0.30 * radius) + 5.3);
                float keep = smoothstep(0.70 * tn - 0.08, 0.70 * tn + 0.08, brk);
                float cov = max(crust, vein * keep) * (1.0 - smoothstep(0.86, 1.0, tn));
                return cov * (1.0 - 0.96 * saturate(t));
            }

            Varyings vert(Attributes v)
            {
                Varyings o;
                VertexPositionInputs p = GetVertexPositionInputs(v.positionOS.xyz);
                o.positionCS = p.positionCS;
                o.positionWS = p.positionWS;
                float4x4 m = GetObjectToWorldMatrix();
                float scale = length(float3(m[0].x, m[1].x, m[2].x));
                o.noisePos = v.positionOS.xyz * scale;
                o.uv = v.uv;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float pulse = saturate(_Pulse), brk = saturate(_Break);
                half3 tint = saturate(_Tint.rgb) * _Brightness;
                half3 paper = lerp(saturate(_Paper.rgb) * _PaperBrightness, tint, 0.22);
                half3 ink = saturate(_Ink.rgb);

                float3 premul = 0;     // premultiplied "over" composite: contamination first, organs over it
                float alpha = 0;
                float region = 0, contamRegion = 0;

                // ---- D308-4d contamination (uniform branch: mode 0 = none). The texture is sampled outside the branch (like _MaskTex):
                // a derivative inside flow control is a compiler warning on some targets (X4121) — default "black" = 0.
                // Mip clamp: the body UV0 is an atlas of many small islands; a coarse mip averages a contaminated island into its UV
                // neighbours (clean skin elsewhere on the body). Offline estimate on the six masks: clean texels reading >= .02 are
                // <= 0.46 % at mip 2 but up to 4.5 % at mip 4 — so the level of detail is computed here and clamped to _ContamMaxLod
                float2 contamTexel = i.uv * _ContamTex_TexelSize.zw;
                float2 contamDx = ddx(contamTexel), contamDy = ddy(contamTexel);
                float contamLod = clamp(0.5 * log2(max(max(dot(contamDx, contamDx), dot(contamDy, contamDy)), 1e-8)), 0.0, max(_ContamMaxLod, 0.0));
                float contamTex = SAMPLE_TEXTURE2D_LOD(_ContamTex, sampler_ContamTex, i.uv, contamLod).r;
                if (_ContamMode > 0.5)
                {
                    float v = _ContamMode < 1.5 ? contamTex : ContamSphereValue(i.positionWS, i.noisePos);
                    float presence = smoothstep(0.02, 0.06, v);
                    contamRegion = presence;
                    float strength = saturate(_ContamState.w);
                    // idle: multiplicative darkening toward the palette corrupt ink (never brightens a dark body, no hue of its own beyond the ink)
                    float dark = presence * lerp(saturate(_ContamDark.y), saturate(_ContamDark.x), saturate(v)) * strength;
                    premul = saturate(_ContamColor.rgb) * saturate(_ContamDark.z) * dark;
                    alpha = dark;
                    // telegraph window: the element tint stands on the veins whose geodesic distance (t ~ 1 - v) is inside the flow front
                    float tEst = 1.0 - v;
                    float front = _ContamState.x;
                    float soft = max(_ContamFlowSoft, 1e-3);
                    float tintA = presence * (1.0 - smoothstep(front - soft, front, tEst)) * saturate(_ContamState.y)
                                * (1.0 - smoothstep(0.55, 1.0, saturate(_ContamState.z))) * saturate(_ContamDark.w);
                    premul = tint * tintA + premul * (1.0 - tintA);
                    alpha = tintA + alpha * (1.0 - tintA);
                }

                // ---- organs (D308-4 telegraph surface)
                float texMask = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, i.uv).r;
                float thr = saturate(_MaskThreshold);
                texMask = smoothstep(thr, min(1.0, thr + 0.2), texMask);
                float maskMode = saturate(_MaskMode);
                float feather = max(_Feather, 1e-3);
                float ringScale = max(_RingScale, 0.05);
                int count = (int)min(_OrganCount, (float)ORGAN_MAX);

                [loop] for (int k = 0; k < count; k++)
                {
                    float4 sphere = _OrganSphere[k];
                    float4 state = _OrganState[k];
                    float4 param = _OrganParam[k];
                    float radius = max(sphere.w, 1e-4);
                    float d = distance(i.positionWS, sphere.xyz) / radius;
                    float mask = lerp(1.0, texMask, maskMode * saturate(param.x));
                    float area = mask * (1.0 - smoothstep(1.0 - feather, 1.0, d));
                    region = max(region, area);
                    float level = saturate(state.x);
                    if (area <= 0.0 || level <= 0.0) continue;

                    float open = saturate(state.y), blot = saturate(state.z), burst = saturate(state.w);
                    float seed = _Seed + param.y;
                    float3 q = i.noisePos / radius;   // radius-normalised, so the ink grain has the same size on every organ

                    // 먹 가장자리 요철 — 종이 위 먹의 거침
                    float edge = (Noise3(q * 3.0 + seed) - 0.5) * 0.14
                               + (Noise3(q * 7.0 + seed * 1.7) - 0.5) * 0.06;
                    float r = d / ringScale;
                    float rr = r + edge;

                    // 반지름(d / RingScale 단위): InkOrganHalo의 쿼드 반지름 × 1.25 — 열린 고리 바깥이 .90, 한지 구멍이 .90 − RingWidth.
                    // 절정·터짐의 부풂은 영역 깃(1 − Feather … 1)까지만 보인다(기관 밖 몸으로 번지지 않는다)
                    float outerR = 0.78 + 0.12 * open + 0.06 * pulse + 0.10 * burst;
                    float paperR = max(0.0, 0.90 - _RingWidth) * open * (1.0 - 0.38 * pulse) * (1.0 - blot);
                    float coreR = paperR * (0.48 + 0.18 * pulse);

                    float inkMask = 1.0 - smoothstep(outerR - 0.08, outerR, rr);
                    float paperMask = 1.0 - smoothstep(paperR - 0.07, paperR, rr);
                    float coreMask = 1.0 - smoothstep(coreR * 0.55, coreR, r);

                    // 개화 = 고리 조각이 빠진다 · 터짐 = 바깥으로 먹 점이 흩어진다
                    float seg = Noise3(q * 2.2 + seed * 3.1);
                    float ringZone = inkMask * (1.0 - paperMask);
                    float keep = 1.0 - brk * smoothstep(0.45, 0.6, seg);
                    float speck = step(0.78, Noise3(q * 8.0 + seed * 5.3)) * burst
                                * smoothstep(outerR - 0.05, outerR + 0.05, r) * (1.0 - smoothstep(outerR + 0.1, outerR + 0.34, r));

                    half3 col = lerp(ink, paper, paperMask);
                    col = lerp(col, tint, coreMask * paperMask);

                    float a = max(inkMask * lerp(1.0, keep, ringZone), speck);
                    a *= level * (1.0 - smoothstep(0.55, 1.0, blot)) * area;
                    premul = premul * (1.0 - a) + col * a;
                    alpha = a + alpha * (1.0 - a);
                }

                if (_DebugView > 0.5)
                {
                    float show = _DebugView > 1.5 ? contamRegion : region;
                    clip(show - 0.004);
                    return half4(show, show, show, saturate(show));   // premultiplied white
                }

                clip(alpha - 0.004);
                half3 colour = saturate(premul / max(alpha, 1e-4));
                float peak = max(colour.r, max(colour.g, colour.b));
                colour *= min(1.0, saturate(_Brightness) / max(peak, 1e-4));   // LDR cap: no channel above _Brightness (≤ .85)
                float outA = saturate(alpha);
                return half4(colour * outA, outA);                               // premultiplied (Blend One OneMinusSrcAlpha)
            }
            ENDHLSL
        }
    }
}
