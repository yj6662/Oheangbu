// 술식 전개 층의 먹 획 — SPEC-SPELL-DEPLOY-308 §3 (D308-10, D308-10c) [TEST].
// 버스트 하나 = 메시 하나(InkBurstMeshBuilder308). 메시는 Begin에서 한 번 조립되고, 셀 틱마다 InkDeployRuntime308이
// MaterialPropertyBlock 값(_Cel · _Advance · _Melt · _ColumnRise · _ColumnDrain · _Glow)만 바꾼다.
// 정점 계약:
//   POSITION  = 최종 위치(효과 로컬)        NORMAL = 바늘 선의 옆 방향(단위)        TANGENT = xyz 전진 벡터(과녁까지), w 전진 가중
//   COLOR     = r 담채 가중 · g 걷힘 편차 · b 획을 가로지른 자리(0 한쪽 변 .. 1 반대쪽 변; 몸통 매개가 0이면 쓰지 않는다)
//   TEXCOORD0 = 획 uv (몸통: x 진행 0 꼬리..1 머리, y 0 윤곽..1 등뼈 / 꼬리·얼룩: 아틀라스 칸 uv / 기둥: x 폭, y 높이)
//   TEXCOORD1 = (종류, 탄생 셀(음수 = 점화 표식: 0셀부터 보이고 -값 셀에 사라진다), 씨앗 0..1, 매개) — 종류 0 몸통 · 1 갈필 꼬리(아틀라스) · 2 바늘 · 3 먹 기둥 · 4 얼룩(아틀라스)
//               매개 = 아틀라스 칸 번호(1·4) / 반폭 m, 부호 = 옆(2) / 띠 폭÷높이(3) / 몸통(0): 0 = 그대로, 1 + 젖은 몸통이 시작하는 진행값 =
//               몸통의 시작을 갈필 털로 빗어 낸다(look2 2회차 — 꼬리 위에 반듯하게 잘린 몸통 시작 단을 없앤다. 넣은 획만 바뀐다)
// 순간 발광만(D308-10c, ART-INK 광원 3등급의 중등 "획이 그어지는 순간 빛나고 먹으로 가라앉는다"):
//   발광 = _Glow × _GlowColor(속성 색조) × 감쇠, 감쇠 = saturate(1 − (지금 셀 − 태어난 셀) ÷ _GlowCels). 태어난 셀은 TEXCOORD1.y다
//   (점화 표식은 0셀에 태어난 것으로 친다). _GlowCels 셀이 지나면 0이고, 런타임이 버스트 박자 밖(머묾·걷힘·서 있는 방벽)에서는
//   _Glow 자체를 0으로 준다. 지속 발광 없음. 블렌드는 Off(가산 블렌드 없음)이고 HDR 출력도 없다: 발광이 올릴 수 있는 값은
//   선형 .45(화면 .70)까지라 블룸의 부드러운 무릎(월드 프로필 선형 .68, 프로젝트 최저 .5) 아래다. 속성색 담채는 LDR로 35 % 이하만 섞고, 발광이 없는 화소의
//   최대 채널은 ≤ .85(기존 먹 셰이더와 같은 상한)다. 고인 테는 _GlowRim만큼만 받는다(테가 몸통보다 어둡다는 구조가 남는다).
// 컷아웃(ZWrite On + clip) — 굵은 획이 겹쳐도 덮어 그리기는 깊이로 줄어든다. ShadowCaster·DepthOnly·DepthNormals 패스 없음:
//   그림자와 깊이·법선 먹선에 끼지 않는다. VfxAfterFog(20) 층에 놓여 영역 안개 뒤에 그려진다(Renderer297의 VfxAfterFog300_Opaque).
// 1인칭: 카메라 _NearClip 안은 그리지 않고 _NearFade까지 디더로 옅어진다. 인식 불가침: 표현 전용.
Shader "Oheangbu/InkBurst308"
{
    Properties
    {
        _Atlas("Deploy atlas (R distance field, G wet, B dry order; linear)", 2D) = "gray" {}
        _BodyColor("Wet body ink", Color) = (0.22, 0.205, 0.19, 1)
        _RimColor("Pooled rim ink", Color) = (0.05, 0.046, 0.042, 1)
        _RimShare("Rim share of the half width", Range(0, 0.5)) = 0.12
        _EdgeJitter("Edge jitter", Range(0, 0.3)) = 0.06
        _NoiseScale("Wet grain scale (1/m)", Float) = 9
        _NeedleMinPixels("Needle minimum width (px)", Range(0.5, 4)) = 1.5
        _NearClip("Near clip (m)", Float) = 0.45
        _NearFade("Near fade end (m)", Float) = 0.9
        [Header(Per burst values set by the runtime property block)]
        _Cel("Revealed cel", Float) = 1000
        _Advance("Advance (1 = the authored reach)", Float) = 1
        _Melt("Melt (0 whole - 1 gone, tail first)", Range(0, 1)) = 0
        _ColumnRise("Column rise", Range(0, 1)) = 1
        _ColumnDrain("Column drain", Range(0, 1)) = 0
        _Tint("Element wash (LDR)", Color) = (0, 0, 0, 1)
        _TintAmount("Wash amount (<= .35)", Range(0, 0.35)) = 0
        _Seed("Seed", Float) = 0
        [Header(Momentary glow D308 10c set per burst by the runtime)]
        _Glow("Glow amount on the birth cel (linear, <= .35)", Range(0, 0.35)) = 0
        _GlowCels("Cels the glow takes to sink into ink", Range(0, 4)) = 2
        _GlowColor("Glow hue (LDR, brightest channel 1)", Color) = (1, 0.97, 0.9, 1)
        _GlowRim("Share of the glow the pooled rim takes", Range(0, 1)) = 0.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "TransparentCutout"
            "Queue" = "AlphaTest"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "InkBurst"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend Off
            ZWrite On
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            // first use is the first frame of a cast: without this the editor would show the cyan placeholder for a moment
            #pragma editor_sync_compilation
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "InkCommon308.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                float4 data       : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 uvData     : TEXCOORD0;   // xy uv, z kind, w param
                float4 misc       : TEXCOORD1;   // x seed, y tint weight, z melt bias, w eye depth
                float4 positionOS : TEXCOORD2;   // xyz effect-local position, w momentary glow (the same for a whole stroke)
                float across      : TEXCOORD3;   // body strokes with a combed start: 0 one edge .. 1 the other
            };

            TEXTURE2D(_Atlas);
            SAMPLER(sampler_Atlas);

            CBUFFER_START(UnityPerMaterial)
                float4 _Atlas_ST;
                half4 _BodyColor;
                half4 _RimColor;
                float _RimShare;
                float _EdgeJitter;
                float _NoiseScale;
                float _NeedleMinPixels;
                float _NearClip;
                float _NearFade;
                float _Cel;
                float _Advance;
                float _Melt;
                float _ColumnRise;
                float _ColumnDrain;
                half4 _Tint;
                float _TintAmount;
                float _Seed;
                float _Glow;
                float _GlowCels;
                half4 _GlowColor;
                float _GlowRim;
            CBUFFER_END

            Varyings vert(Attributes v)
            {
                Varyings o;
                float kind = v.data.x;
                // the head of an advancing stroke and everything authored at the target ride the advance vector
                float3 p = v.positionOS.xyz + v.tangentOS.xyz * (v.tangentOS.w * (_Advance - 1.0));
                float3 ws = TransformObjectToWorld(p);
                float4 cs = TransformWorldToHClip(ws);
                if (abs(kind - 2.0) < 0.5)
                {
                    // needle lines never get thinner than _NeedleMinPixels on screen
                    float halfWidth = abs(v.data.w);
                    float worldPerPixel = abs(cs.w) * 2.0 / max(abs(UNITY_MATRIX_P[1][1]) * _ScreenParams.y, 1.0);
                    float add = max(0.0, 0.5 * _NeedleMinPixels * worldPerPixel - halfWidth);
                    float3 side = TransformObjectToWorldDir(v.normalOS);
                    ws += side * (add * sign(v.data.w));
                    cs = TransformWorldToHClip(ws);
                }
                o.misc = float4(v.data.z, v.color.r, v.color.g, abs(cs.w));
                // not born yet: all three vertices of the triangle leave the clip volume (a stroke appears, it does not grow).
                // A negative birth is an ignition mark: shown from cel 0 and gone once the cel reaches -birth.
                float birth = v.data.y;
                bool hidden = birth >= 0.0 ? birth > _Cel + 0.5 : _Cel > -birth - 0.5;
                if (hidden) cs = float4(2.0, 2.0, 2.0, 1.0);
                o.positionCS = cs;
                o.uvData = float4(v.uv, kind, v.data.w);
                // D308-10c momentary glow: full on the cel the stroke is born, stepping down to nothing _GlowCels cels later
                // (an ignition mark counts as born on cel 0). The runtime gives _Glow = 0 outside the burst beat, so nothing
                // glows in the hold, the melt or a standing ward.
                float glowAge = max(_Cel - max(birth, 0.0), 0.0);
                float glow = saturate(1.0 - glowAge / max(_GlowCels, 0.001)) * step(0.001, _GlowCels) * clamp(_Glow, 0.0, OH_GLOW_MAX);
                o.positionOS = float4(p, glow);
                o.across = v.color.b;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                float kind = i.uvData.z;
                float2 uv = i.uvData.xy;
                float seed = i.misc.x;

                // first person: nothing inside the near clip, dithered out to the near fade
                float visible = OhNearVisibility(i.misc.w, _NearClip, _NearFade);
                clip(visible - OhDither(i.positionCS.xy) * 0.999 - 0.0005);

                // sampled outside the branches (gradients); non-atlas kinds ignore it
                float4 rect = OhCellRect(i.uvData.w);
                half3 cell = SAMPLE_TEXTURE2D(_Atlas, sampler_Atlas, rect.xy + saturate(uv) * rect.zw).rgb;
                float grain = OhFbm(i.positionOS.xy * _NoiseScale + i.positionOS.z * _NoiseScale * 0.37 + (seed + _Seed) * 17.3);

                float wet = 0.0;       // 0 = the lighter wet body, 1 = the pooled dark rim
                float along = uv.x;    // melt order: the tail (0) goes first
                float keep = 1.0;
                if (kind < 0.5)
                {
                    // capsule body: uv.y = 0 on the outline, 1 on the spine
                    keep = uv.y - grain * _EdgeJitter;
                    wet = 1.0 - smoothstep(_RimShare, _RimShare + 0.06, uv.y);
                    wet = max(wet, saturate((grain - 0.62) * 3.0) * 0.55);
                    // brush hairs: darker runs along the stroke, so the body is ink and not a flat fill
                    float run = OhValueNoise(float2(uv.x * 2.6 + seed * 13.0, uv.y * 11.0 + seed * 31.0));
                    wet = max(wet, smoothstep(0.52, 0.80, run) * 0.6);
                    if (i.uvData.w > 0.5)
                    {
                        // look2 round 2: the wet body does not begin with a straight cut - over its first share it is combed
                        // out of the dry tail as brush hairs (long thin runs along the stroke), dark like the tail
                        // param = 1 + start share (+ 2 x n: the hairs run over n twentieths of the body; n = 0: over .26 of it)
                        float twenties = floor(i.uvData.w * 0.5 + 0.001);
                        float u0 = i.uvData.w - 2.0 * twenties - 1.0;
                        float share = twenties > 0.5 ? twenties * 0.05 : 0.26;
                        float t = saturate((uv.x - u0) / max((1.0 - u0) * share, 0.001));
                        float hairs = OhValueNoise(float2(i.across * 41.0 + seed * 37.0, uv.x * 1.3 + seed * 5.0)) * 0.65
                                    + OhValueNoise(float2(i.across * 97.0 + seed * 11.0, uv.x * 2.3)) * 0.35;
                        keep = min(keep, hairs + t * 1.5 - 0.72);
                        wet = max(wet, 1.0 - t * 1.25);
                    }
                }
                else if (kind < 1.5)
                {
                    // dry-brush tail (atlas row 0): the paper shows between the hairs
                    keep = cell.r - 0.5;
                    wet = cell.g;
                    along = uv.x * 0.35;   // dry hairs go in the first third of the melt
                }
                else if (kind < 2.5)
                {
                    // needle line: solid, its taper is geometry
                    wet = 0.85;
                }
                else if (kind < 3.5)
                {
                    // ink column strip: brush strokes pulled upward - wet and full at the foot, thin and dry toward the tip.
                    // Rises in cels and drains. uvData.w = strip width / height: a wide strip (ward wall) is several strokes.
                    float aspect = max(i.uvData.w, 0.001);
                    float count = max(1.0, floor(aspect * 6.0 + 0.5));
                    float cx = uv.x * count;
                    float id = floor(cx);
                    float lx = cx - id;
                    float r = frac(seed * 7.13 + 0.17 + id * 0.618);
                    float top = saturate(_ColumnRise * (0.72 + 0.28 * r)) * (1.0 - saturate(_ColumnDrain) * (0.55 + 0.45 * frac(r * 5.7 + 0.31)));
                    top *= 1.0 - step(0.999, _ColumnDrain);
                    float h = saturate(uv.y / max(top, 0.001));
                    float sway = (OhValueNoise(float2(uv.y * 2.3 + r * 9.1, r * 3.7)) - 0.5) * 0.34 * h;
                    float halfW = lerp(0.56, 0.08, smoothstep(0.45, 1.0, h)) + (grain - 0.5) * 0.12;
                    float hairs = OhValueNoise(float2((lx - sway) * 3.4 + r * 41.0, uv.y * 1.6 + r * 5.3));
                    float dryness = smoothstep(0.30, 0.95, h);
                    float inside = step(abs(lx - 0.5 - sway), halfW) * step(uv.y, top) * step(dryness * 0.62, hairs + (1.0 - dryness) * 0.8);
                    keep = min(inside, step(0.002, top)) - 0.5;
                    wet = saturate(1.0 - h * 0.85);
                    along = 1.0;
                }
                else
                {
                    // atlas smear / star / ring: melts by its own dry order
                    keep = cell.r - 0.5;
                    wet = cell.g;
                    along = cell.b;
                }
                clip(keep);

                // melt: dries from the tail toward the head, stepped by the runtime in cels
                float order = along * 0.72 + grain * 0.2 + i.misc.z * 0.08;
                clip(order - _Melt * 1.04 + step(_Melt, 0.0005));

                half3 ink = lerp(_BodyColor.rgb, _RimColor.rgb, saturate(wet));
                // the wash shifts the ink's hue and keeps its darkness: a bright element colour mixed in by weight would
                // read as paint (red far lighter than green). Amount .35 = the full hue shift.
                half amount = saturate(min(_TintAmount, 0.35) / 0.35) * saturate(i.misc.y);
                half3 hue = saturate(_Tint.rgb) / max(OhLuma(saturate(_Tint.rgb)), 0.05);
                half3 rgb = min(ink * lerp(half3(1.0, 1.0, 1.0), min(hue, 3.0), amount), OH_INK_CEILING_OUT);
                // momentary glow: the ink is lit in the element hue for its first cels and sinks back. The pooled rim takes
                // less, so the stroke keeps its ink structure. It may lift a pixel to OH_GLOW_CEILING_OUT at most (LDR, under
                // the bloom knee) and never darkens one.
                half glowHere = i.positionOS.w * lerp(1.0, saturate(_GlowRim), saturate(wet));
                half3 lit = min(rgb + saturate(_GlowColor.rgb) * glowHere, OH_GLOW_CEILING_OUT);
                return half4(max(rgb, lit), 1.0);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
