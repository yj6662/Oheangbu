// 생목 실체 셰이더 — SPEC-SPELL-FX-REWORK §2·§3.4 (고 — InkBody).
// 생성 모델(덩굴 가닥) 실체용 프로젝트 소유 URP HLSL. 무텍스처·_EmissionColor 프로퍼티 자체 없음·무PBR
// (Metallic/Smoothness/큐브맵 미사용 — FX-ASSETS §4.2-4 문면 만족)·광원 색 미곱(색=팔레트 _BaseColor 단일 출처).
// 출력 = _BaseColor × 명도 배율(음영·먹선·광택·먹 침전)뿐 — 최대 명도 = 틴트 × (1 + _Sheen) < Bloom threshold 1.0.
//
// 메시 규약(FX-ASSETS §4.2-6): 뿌리 피벗 z=0 · 첨단 +Z · z∈[0,1] 정규화. 정점 변형 DeformOS는
//   · 성장 전선(_Grow 0→1): 전선 앞 정점을 축(또는 정점색 rg 척추)으로 수렴시켜 「첨단이 첨단인 채 뻗는다」
//   · sway(_SwayAmp·_SwayFreq·_SwaySeed): 뿌리 고정·끝만 z² 가중 감쇠 흔들림
//   를 3패스(Forward/DepthOnly/DepthNormals)가 공유한다 — 깊이·법선 버퍼가 보이는 형상과 일치(SSAO Source=DepthNormals 계약).
// 본별 값(_Grow·_SwayAmp·_SwaySeed·_Value)은 MaterialPropertyBlock — 전 프로퍼티는 UnityPerMaterial CBUFFER(SRP Batcher).
// 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkBody"
{
    Properties
    {
        [MainColor] _BaseColor("Base Color (팔레트 틴트 슬롯 — 런타임 세팅)", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color("Color (호환 — TintHierarchy)", Color) = (1, 1, 1, 1)

        [Header(Ink Shade TEST)]
        _InkShade("Ink Shade (그늘 면 먹 농도 — 명도 감산)", Range(0, 1)) = 0.45
        _BandSoft("Band Soft (명암 경계 부드러움)", Range(0.005, 0.5)) = 0.08
        _GrainScale("Grain Scale (결 밀도 — 축 방향 셀/단위 길이)", Float) = 9
        _GrainStretch("Grain Stretch (결의 축 방향 늘림 비율)", Float) = 4
        _GrainStrength("Grain Strength (결이 명암 경계를 흔드는 폭)", Range(0, 1)) = 0.18
        _RimWidth("Rim Width (실루엣 먹선 폭 — N·V 임계)", Range(0, 1)) = 0.18
        _RimDark("Rim Dark (먹선 농도)", Range(0, 1)) = 0.35
        _SheenWidth("Sheen Width (생목 축축함 밴드 폭)", Range(0, 0.5)) = 0.06
        _Sheen("Sheen (밝은 면 명도 가산 상한 — ART-INK-LOOK 플래시 +10~15% 안)", Range(0, 0.15)) = 0.05
        _FacetMix("Facet Mix (0=보간 법선 1=면 법선 — 결 각짐)", Range(0, 1)) = 0.3
        _AoStrength("AO Strength (SSAO 수용 강도 — 0=무시)", Range(0, 1)) = 0.5

        [Header(Growth per vine MPB)]
        _Grow("Grow (성장 전선 0=매장 1=완성 — 본별 MPB)", Range(0, 1)) = 1
        _GrowWindow("Grow Window (전선 앞 수렴 창 — z 단위)", Range(0.01, 0.5)) = 0.12
        [ToggleUI] _SpineMode("Spine Mode (정점색 rg=척추 xy 베이크 시 1)", Float) = 0
        _SpineRange("Spine Range (정점색 rg 복원 범위 ±)", Float) = 0.5
        _SwayAmp("Sway Amp (흔들림 진폭 — 본별 MPB)", Float) = 0
        _SwayFreq("Sway Freq (Hz)", Float) = 3.5
        _SwayYRatio("Sway Y Ratio (흔들림 두 번째 축 비율 — 0=한 축)", Range(0, 1)) = 0.6
        _SwaySeed("Sway Seed (본별 위상 — 결 시드 겸용)", Float) = 0
        _Value("Value (먹 침전 — 명도 배율, 시듦 1→0.55)", Range(0, 1)) = 1

        [Header(Vine wood and writhe TEST)]
        _TipColor("Tip Color (성장 끝·가시 = 속성 강조색 — 줄기는 _BaseColor 목질색)", Color) = (1, 1, 1, 1)
        _TipStart("Tip Start (끝 색이 서기 시작하는 z)", Range(0, 1)) = 0.45
        _CurlAmp("Curl Amp (감김 — 축을 따라 한쪽으로 휘는 폭, 본별 부호)", Float) = 0
        _SerpAmp("Serpentine Amp (사행 — 줄기가 좌우로 구불거리는 폭)", Float) = 0
        _SerpFreq("Serpentine Freq (줄기 하나에 실리는 물결 수)", Float) = 1.8
        _SerpDir("Serp Dir (구불거림이 눕는 오브젝트 평면 방향 xy — 본별 MPB. 지면에 눕히려면 C#이 준다)", Vector) = (1, 0, 0, 0)
        _ChainZ("Chain Z (이 마디가 전체 체인에서 차지하는 z 구간 — 본별/마디별 MPB. (0,1)=단일 오브젝트)", Vector) = (0, 1, 0, 0)
        [ToggleUI] _LeafMode("Leaf Mode (정점색 b=잎 마스크로 읽는다 — 잎 없는 메시는 0 유지: 백색 정점색이 전부 잎이 된다)", Float) = 0
        _WaveK("Wave K (꿈틀 파수 — z를 따라 위상이 밀린다. 0=제자리 스윙)", Float) = 4

        [Header(Girth and sprout TEST)]
        _Maturity("Maturity (본 성숙도 0=발아 1=완성 — 본별 MPB. 굵기·새싹·녹화의 공통 시계)", Range(0, 1)) = 1
        _Spine("Spine (마디 단면 중심 — xy=z0 끝, zw=z1 끝. 메시별 실측 MPB. 정규화가 원점을 보증하지 않는다)", Vector) = (0, 0, 0, 0)
        _GirthMin("Girth Min (발아 순간 굵기 배수 — 1=변화 없음)", Range(0.05, 1)) = 1
        _GirthTip("Girth Tip (체인 끝으로 갈수록 가늘어지는 배수 — 1=균일)", Range(0.1, 1.5)) = 1
        _SproutStart("Sprout Start (새싹이 트기 시작하는 성숙도 — 예준 「50% 시점」)", Range(0, 1)) = 0.5
        _SproutSpan("Sprout Span (새싹 하나가 다 펴지는 성숙도 폭)", Range(0.02, 0.6)) = 0.18
        _GreenMix("Green Mix (줄기 녹화 0→1 — 본별 MPB)", Range(0, 1)) = 0
        _GreenFromZ("Green From Z (녹화가 서기 시작하는 chainZ — 밑동 목질을 남긴다)", Range(0, 1)) = 0.25
        _MossColor("Moss Color (녹화 종점 — 팔레트 파생, 런타임 세팅)", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull (2=Back 줄기 / 0=Off 새싹)", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
            "IgnoreProjector" = "True"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        // 주광 방향만 쓴다(GetMainLight().direction) — 광원 색은 곱하지 않는다(담채 드리프트 방지). SSAO 샘플도 여기 경유
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RealtimeLights.hlsl"

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
            float4 color      : COLOR;      // rg=척추 xy(_SpineMode 1일 때만 — 없으면 백(1,1,1,1) → 축)
                                            // b=잎/새싹 마스크(_LeafMode) · a=새싹 발아 순서(0=먼저)
            float4 uv         : TEXCOORD0;  // 새싹 전용: xyz=줄기 표면 부착점(오브젝트). 줄기 FBX엔 UV가 없다 —
                                            // 정규화가 UV·정점색을 제거했으므로 _LeafMode 분기 밖에서 절대 읽지 않는다
        };

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Color;
            half  _InkShade;
            half  _BandSoft;
            half  _GrainScale;
            half  _GrainStretch;
            half  _GrainStrength;
            half  _RimWidth;
            half  _RimDark;
            half  _SheenWidth;
            half  _Sheen;
            half  _FacetMix;
            half  _AoStrength;
            half  _Grow;
            half  _GrowWindow;
            half  _SpineMode;
            half  _SpineRange;
            half  _SwayAmp;
            half  _SwayFreq;
            half  _SwayYRatio;
            half  _SwaySeed;
            half  _Value;
            half4 _TipColor;
            half  _TipStart;
            half  _CurlAmp;
            half  _WaveK;
            half  _SerpAmp;
            half  _SerpFreq;
            float4 _SerpDir;
            half  _LeafMode;
            float4 _ChainZ;
            half   _Maturity;
            float4 _Spine;
            half   _GirthMin;
            half   _GirthTip;
            half   _SproutStart;
            half   _SproutSpan;
            half   _GreenMix;
            half   _GreenFromZ;
            half4  _MossColor;
            half   _Cull;
        CBUFFER_END

        // 정수 해시 value noise 2옥타브 — InkStroke.shader 이식(텍스처 0). 이름은 URP 내장과의 충돌 회피용 접두
        float InkHash(float2 p)
        {
            p = frac(p * float2(123.34, 456.21));
            p += dot(p, p + 45.32);
            return frac(p.x * p.y);
        }

        float InkValueNoise(float2 p)
        {
            float2 i = floor(p);
            float2 f = frac(p);
            f = f * f * (3.0 - 2.0 * f);
            return lerp(
                lerp(InkHash(i), InkHash(i + float2(1, 0)), f.x),
                lerp(InkHash(i + float2(0, 1)), InkHash(i + float2(1, 1)), f.x), f.y);
        }

        float InkFbm(float2 p)
        {
            return InkValueNoise(p) * 0.6 + InkValueNoise(p * 2.13 + 17.7) * 0.4;
        }

        // 공용 정점 변형 — 3패스 동일(깊이·법선 버퍼 = 보이는 형상).
        // 성장 전선 front = _Grow × (1 + 창): _Grow=1이면 front > 1(=첨단)이라 완성형은 무변형,
        // _Grow=0이면 전부 z=0·축으로 붕괴(면적 0 — 매장 상태를 지면 아래 숨김 없이 표현).
        // 전선 앞(z > front−창) 정점은 축/척추로 수렴 → 어느 순간에도 끝이 뾰족한 채 「뻗는다」.
        // 3패스가 공유한다 — 깊이·법선 버퍼가 보이는 형상과 일치해야 SSAO(Source=DepthNormals)가 맞는다.
        // 굵기가 0.28→1.0으로 변하는데 법선을 두면 음영이 형상과 통째로 어긋나므로 **야코비안 역전치로 이송**한다.
        //   변형: xy' = c(z) + (xy − c(z))·s(z) + dir·f(z) · z' = min(z, front)
        //   J = [[s,0,A],[0,s,B],[0,0,1]],  A = cD.x(1−s) + d0.x·s' + dir.x·f',  B = 동형(y)
        //   n' = (J⁻¹)ᵀn = (n.x/s, n.y/s, n.z − (A·n.x + B·n.y)/s)
        void DeformOS(inout float3 p, inout float3 n, float4 col, float4 uv)
        {
            float window = max(_GrowWindow, 1e-4);
            float front  = _Grow * (1.0 + window);
            // 변형이 눕는 평면 — 마디 로컬 X가 항상 수평이라(LookRotation) 미배선이면 x축 폴백으로 충분하다
            float2 dir = dot(_SerpDir.xy, _SerpDir.xy) > 1e-6 ? normalize(_SerpDir.xy) : float2(1.0, 0.0);
            float curlSign = frac(_SwaySeed * 0.618) < 0.5 ? -1.0 : 1.0;
            float girthRun = lerp(_GirthMin, 1.0, _Maturity);   // 「얇은 뿌리가 굵어진다」의 본 단위 시계

            // ---------- 새싹(풀·잔가지·잎) ----------
            // 병진 + 등방 스케일뿐이라 법선 불변. 형상은 굵기에 끌려가면 안 된다 —
            // 부착점만 줄기를 따라 움직이고, 날의 모양은 open(발아 진행)으로만 자란다
            if (_LeafMode > 0.5)
            {
                float3 a3  = uv.xyz;                       // 줄기 표면 부착점(오브젝트 공간)
                float  az  = saturate(a3.z);
                float  czA = saturate(lerp(_ChainZ.x, _ChainZ.y, az));

                float taperA = saturate((front - a3.z) / window);
                float sA     = taperA * girthRun * lerp(1.0, _GirthTip, czA);
                float2 cA    = lerp(_Spine.xy, _Spine.zw, az);

                float fA = (sin(a3.z * _SerpFreq * TWO_PI + _SwaySeed) - sin(_SwaySeed)) * _SerpAmp
                         + _CurlAmp * curlSign * a3.z * a3.z
                         + sin(_Time.y * _SwayFreq * TWO_PI - a3.z * _WaveK + _SwaySeed) * _SwayAmp * a3.z * a3.z;

                float3 base = float3(cA + (a3.xy - cA) * sA + dir * fA, min(a3.z, front));

                // 발아 순서: col.a 0=먼저. 전부 _Maturity 1.0 안에서 끝나도록 상한을 당겨 둔다
                float thr  = lerp(_SproutStart, 1.0 - _SproutSpan, saturate(col.a));
                float open = saturate((_Maturity - thr) / max(_SproutSpan, 1e-3)) * taperA;

                p = base + (p - a3) * open;
                return;                                    // n 무변경
            }

            // ---------- 줄기 ----------
            float pz    = saturate(p.z);
            float czRaw = lerp(_ChainZ.x, _ChainZ.y, pz);
            float cz    = saturate(czRaw);
            // chainZ의 z 미분 — 구간 밖(포화)에서는 0이라야 법선 이송이 맞는다
            float dcz   = (p.z > 0.0 && p.z < 1.0 && czRaw > 0.0 && czRaw < 1.0) ? (_ChainZ.y - _ChainZ.x) : 0.0;

            float taper  = saturate((front - p.z) / window);
            float taperD = (front - p.z > 0.0 && front - p.z < window) ? (-1.0 / window) : 0.0;

            float girth  = girthRun * lerp(1.0, _GirthTip, cz);
            float girthD = girthRun * (_GirthTip - 1.0) * dcz;
            float s      = taper * girth;
            float sD     = taperD * girth + taper * girthD;
            float sSafe  = max(s, 1e-3);                   // 매장 마디는 taper=0이라 s가 정확히 0이 된다

            // 단면 중심 — **z 선형 척추**. 실측상 A·B는 z=0 단면도 원점이 아니므로 양 끝점을 다 받는다
            // (_SpineMode 1이면 종전대로 정점색 rg를 쓴다 — 그 경로는 dc/dz를 모르므로 0으로 둔다)
            float2 c  = _SpineMode > 0.5 ? (col.rg - 0.5) * _SpineRange : lerp(_Spine.xy, _Spine.zw, pz);
            float2 cD = _SpineMode > 0.5 ? float2(0.0, 0.0)
                                         : ((p.z > 0.0 && p.z < 1.0) ? (_Spine.zw - _Spine.xy) : float2(0.0, 0.0));
            float2 d0 = p.xy - c;

            // 사행(정적 형태) — 뿌리(z=0)에서 정확히 0이 되도록 시드 위상값을 뺀다. 曲直의 曲
            float serp  = (sin(p.z * _SerpFreq * TWO_PI + _SwaySeed) - sin(_SwaySeed)) * _SerpAmp;
            float serpD = cos(p.z * _SerpFreq * TWO_PI + _SwaySeed) * _SerpFreq * TWO_PI * _SerpAmp;
            // 감김 — 축을 따라 한쪽으로 더 휘어 나간다(시드 부호로 본마다 반대)
            float curl  = _CurlAmp * curlSign * p.z * p.z;
            float curlD = 2.0 * _CurlAmp * curlSign * p.z;
            // 꿈틀(진행파) — 위상이 z를 따라 밀린다. 뿌리 고정은 z² 가중이 유지한다
            float phase = _Time.y * _SwayFreq * TWO_PI - p.z * _WaveK + _SwaySeed;
            float wave  = sin(phase) * _SwayAmp * p.z * p.z;
            float waveD = _SwayAmp * (2.0 * p.z * sin(phase) - p.z * p.z * _WaveK * cos(phase));

            float f  = serp + curl + wave;
            float fD = serpD + curlD + waveD;

            p.xy = c + d0 * s + dir * f;
            p.z  = min(p.z, front);

            float A = cD.x * (1.0 - s) + d0.x * sD + dir.x * fD;
            float B = cD.y * (1.0 - s) + d0.y * sD + dir.y * fD;
            n = normalize(float3(n.x / sSafe, n.y / sSafe, n.z - (A * n.x + B * n.y) / sSafe));
        }
        ENDHLSL

        // ---- (1) 색 — 담채 2단 먹 음영 + 실루엣 먹선 + 절차 결 + 축축함 밴드 ----
        Pass
        {
            Name "InkBodyForward"
            Tags { "LightMode" = "UniversalForwardOnly" }

            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 posOS      : TEXCOORD2;  // 결 노이즈 도메인(변형 전 — 성장 중에도 결이 흐르지 않는다)
                float  leaf       : TEXCOORD3;  // 잎 마스크(정점색 b) — 줄기 목질색과 잎 강조색을 가른다
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 p   = v.positionOS.xyz;
                float3 nOS = v.normalOS;
                DeformOS(p, nOS, v.color, v.uv);
                o.positionWS = TransformObjectToWorld(p);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(nOS);
                o.posOS = v.positionOS.xyz;
                o.leaf = _LeafMode > 0.5 ? saturate(v.color.b) : 0.0;
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // 법선: 보간 법선 ↔ 화면 미분 면 법선 혼합(_FacetMix) — 세장 삼각형의 면 노이즈는 0으로 후퇴 가능.
                // 미분 법선의 부호는 플랫폼(ddy 방향)에 따라 뒤집히므로 보간 법선 쪽으로 정렬한다
                float3 nS = normalize(i.normalWS);
                float3 nF = normalize(cross(ddy(i.positionWS), ddx(i.positionWS)));
                nF *= dot(nF, nS) < 0.0 ? -1.0 : 1.0;
                float3 n = normalize(lerp(nS, nF, _FacetMix));

                float3 L = GetMainLight().direction;   // 방향만 — 색은 곱하지 않는다
                float3 V = GetWorldSpaceNormalizeViewDir(i.positionWS);
                float ndl = dot(n, L) * 0.5 + 0.5;     // half-Lambert — 담채는 완전한 검정 그늘을 두지 않는다

                // 절차 결(ART-INK-LOOK §4.3 요철 언어): 축 방향으로 늘어난 셀 — 줄기를 따라 흐르는 갈필 결.
                // 도메인은 변형 전 오브젝트 좌표(성장 중 결 고정), 시드는 본별 _SwaySeed 재사용(같은 메시 12본의 반복 티 제거).
                // 0.5(y 도메인 혼합 반감)·7.31(시드→도메인 오프셋 배율)은 룩 손잡이가 아닌 고정 상수 — 튜너블은 _Grain*·_SwaySeed뿐
                float2 dom;
                dom.x = i.posOS.z * _GrainScale + i.posOS.y * (_GrainScale * _GrainStretch * 0.5);
                dom.y = i.posOS.x * (_GrainScale * _GrainStretch);
                float grain = InkFbm(dom + _SwaySeed * 7.31);

                // 담채 2단 먹 음영: 결이 명암 경계를 흔든다(먹이 종이에 스민 경계)
                float band = smoothstep(0.5 - _BandSoft, 0.5 + _BandSoft, ndl + (grain - 0.5) * _GrainStrength);
                float shade = lerp(1.0 - _InkShade, 1.0, band);

                // 실루엣 먹선 — 획 굵기 언어(반전 헐 없음). 가장자리로 갈수록 먹이 진해진다
                float ndv = saturate(dot(n, V));
                float rim = 1.0 - smoothstep(0.0, _RimWidth, ndv);
                shade *= 1.0 - rim * _RimDark;

                // 생목 축축함 — 먹선 바로 안쪽 좁은 밴드에 밝은 면만 명도 +_Sheen(반사 프로브·Metallic·Smoothness 없음)
                float sheenBand = smoothstep(_RimWidth, _RimWidth + _SheenWidth, ndv)
                    * (1.0 - smoothstep(_RimWidth + _SheenWidth, _RimWidth + 2.0 * _SheenWidth, ndv));
                shade += band * _Sheen * sheenBand;

                // SSAO 수용(PC_Renderer Source=DepthNormals·AfterOpaque 0 — 셰이더가 직접 샘플해야 자기 요철·밑동 접지가 어두워진다)
                #if defined(_SCREEN_SPACE_OCCLUSION)
                float ao = GetScreenSpaceAmbientOcclusion(GetNormalizedScreenSpaceUV(i.positionCS)).indirectAmbientOcclusion;
                shade *= lerp(1.0, ao, _AoStrength);
                #endif

                // 몸통색: 뿌리·줄기 = 목질(갈색 _BaseColor) → 끝·가시 = 속성 강조색(_TipColor).
                // 두 색 모두 팔레트 소유(ART-COLOR 기본 갈색 + 오행 강조색)이므로 색 단일 출처 유지 —
                // 실제 덩굴의 목질 줄기를 살리면서 속성 식별(색+형태 이중 채널)을 끝에서 지킨다
                // 줄기: 뿌리 목질(갈색) → 끝 강조색. 잎: 정점색 b가 1인 곳은 통째로 강조색(청록) —
                // 참고 이미지의 「갈색 덩굴 매트 위에 청록 잎이 흩어진」 구조가 이 두 갈래로 나온다
                // 마디가 체인의 어느 구간인지로 환산 — 안 하면 마디마다 갈색→청록이 반복돼 줄무늬가 된다.
                // (0,1) 기본값이면 chainZ == posOS.z 이므로 단일 오브젝트 사용처는 동작 불변
                float chainZ = lerp(_ChainZ.x, _ChainZ.y, saturate(i.posOS.z));
                half3 stem = lerp(_BaseColor.rgb, _TipColor.rgb, smoothstep(_TipStart, 1.0, chainZ));
                half3 body = lerp(stem, _TipColor.rgb, i.leaf);
                // 줄기 녹화 — 뿌리가 자라며 이끼·새순이 앉는다. 밑동은 목질로 남긴다(#144 「줄기=갈색」 불변).
                // _MossColor도 팔레트 파생이므로 색 단일 출처 무손상. 가산 없음 — lerp라 상한이 두 색 사이로 클램프된다
                body = lerp(body, _MossColor.rgb, _GreenMix * (1.0 - i.leaf) * smoothstep(_GreenFromZ, 1.0, chainZ));

                // 출력 = 몸통색 × 명도 배율 × 먹 침전 — HDR 가산 없음(Bloom threshold 미달), 알파 1(불투명)
                return half4(body * shade * _Value, 1.0);
            }
            ENDHLSL
        }

        // ---- (2) 깊이 — URP DepthOnlyPass.hlsl 계약(ColorMask R·positionCS.z 반환) ----
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthOnlyVert
            #pragma fragment DepthOnlyFrag

            struct VaryingsDepth
            {
                float4 positionCS : SV_POSITION;
            };

            VaryingsDepth DepthOnlyVert(Attributes v)
            {
                VaryingsDepth o;
                float3 p   = v.positionOS.xyz;
                float3 nOS = v.normalOS;                 // 버린다 — 3패스가 같은 위치 함수를 쓰게 하는 것이 목적
                DeformOS(p, nOS, v.color, v.uv);
                o.positionCS = TransformObjectToHClip(p);
                return o;
            }

            half DepthOnlyFrag(VaryingsDepth i) : SV_TARGET
            {
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // ---- (3) 깊이+법선 — URP DepthNormalsPass.hlsl 계약(SV_Target0 = half4(normalWS, 0), OCT 변형·렌더링 레이어 동형) ----
        // PC_Renderer SSAO(Source=DepthNormals)가 덩굴을 인식해 밑동 주변 지면 접지 차폐를 만든다. 누락 시 덩굴이 AO에서 투명 취급
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex DepthNormalsVert
            #pragma fragment DepthNormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #include_with_pragmas "Packages/com.unity.render-pipelines.universal/ShaderLibrary/RenderingLayers.hlsl"

            struct VaryingsDN
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD2;
            };

            VaryingsDN DepthNormalsVert(Attributes v)
            {
                VaryingsDN o;
                float3 p   = v.positionOS.xyz;
                float3 nOS = v.normalOS;
                DeformOS(p, nOS, v.color, v.uv);
                o.positionCS = TransformObjectToHClip(p);
                o.normalWS = NormalizeNormalPerVertex(TransformObjectToWorldNormal(nOS));
                return o;
            }

            void DepthNormalsFrag(
                VaryingsDN input
                , out half4 outNormalWS : SV_Target0
            #ifdef _WRITE_RENDERING_LAYERS
                , out uint outRenderingLayers : SV_Target1
            #endif
            )
            {
                #if defined(_GBUFFER_NORMALS_OCT)
                float3 normalWS = normalize(input.normalWS);
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                outNormalWS = half4(packedNormalWS, 0.0);
                #else
                float3 normalWS = NormalizeNormalPerPixel(input.normalWS);
                outNormalWS = half4(normalWS, 0.0);
                #endif

                #ifdef _WRITE_RENDERING_LAYERS
                outRenderingLayers = EncodeMeshRenderingLayer();
                #endif
            }
            ENDHLSL
        }
    }
}
