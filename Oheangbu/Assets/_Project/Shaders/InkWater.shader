// 먹물 파도 셰이더 — SPEC-SPELL-FX-REWORK §2·§3.3(오). 생성 모델용 「먹 그라디언트 계열 프로젝트 소유 URP 셰이더」.
// 소비 계약: 메시 축 +Z 높이·+Y 전진(립)·+X 폭, 바닥 피벗(FX-ASSETS §4.2-6). _HeightOS는 코드(WaterWaveEffect.Begin)가
//   메시 bounds.size.z를 세팅 — hOS = posOS.z / _HeightOS 가 「바닥 0 → 립 1」 높이 좌표다. UV 미사용(Meshy UV 불신).
// 노이즈 도메인 규약: 흐름·먹 농담 = posWS(물이 세상을 지나는 느낌) /
//   립 침식·디졸브 = posOS + 느린 시간(메시에 붙는다 — 6m/s 전진 중 침식 패턴이 스크롤돼 어른거리는 것을 막는다).
// 색 규약: 색 슬롯은 _BaseColor 하나(코드가 팔레트 tint 세팅). _Color는 HasProperty 호환용 별칭 — 셰이더는 읽지 않는다(tint² 없음).
//   출력 = _BaseColor × 명도 배율뿐(농담 밴드·반 램버트 음영·바닥 고임·프레넬 림 — 색상·채도 불변, 제2 색 출처 없음).
//   림은 명도 +_RimLift 상한 → 현색(0.2,0.278,0.361) 최대 채널 ≈0.47, 백색·시안 포화 불가.
// Emission·Metallic·Smoothness 프로퍼티 없음 — 발광 상한(ART-INK)·무PBR(FX-ASSETS §4.2-4). 텍스처 0(InkStroke 해시 노이즈 이식).
// 패스: UniversalForwardOnly(Opaque+clip) · DepthOnly · DepthNormals — PC_Renderer SSAO(Source=DepthNormals)와 깊이 텍스처에
//   정점 오프셋·clip을 동일 적용해 컬러와 어긋나지 않게 한다. ShadowCaster 없음 = 그림자를 드리우지 않는다(고체감 제거 — 의도).
//   투명 블렌드 미채택 — 로브 3 관통면의 순서 의존 이음선 회피(물은 먹처럼 불투명하게 그린다).
// 인식 불가침: 표현 전용 — 무엇을 바꿔도 인식·판정은 불변이다.
Shader "Oheangbu/InkWater"
{
    Properties
    {
        _BaseColor("Base Color (팔레트 틴트 — 코드 세팅)", Color) = (1, 1, 1, 1)
        [HideInInspector] _Color("Color (호환 별칭 — 미독)", Color) = (1, 1, 1, 1)
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("Cull", Float) = 2

        _NoiseScale("Noise Scale (월드 m당 결 밀도)", Float) = 1.4
        _FlowSpeed("Flow Speed (표면 흐름 /s)", Float) = 0.6
        _WaveAmp("Wave Amp (정점 노이즈 오프셋 m)", Float) = 0.06
        _WaveFloor("Wave Floor (오프셋 시작 높이 비율 — 지면 접점 고정)", Range(0, 0.95)) = 0.35
        _HeightOS("Height OS (메시 높이 — 코드 세팅)", Float) = 0.85
        _ToneContrast("Tone Contrast (먹 농담 밴드 ±)", Range(0, 0.5)) = 0.12
        _Shade("Shade (반 램버트 형태 음영 비중)", Range(0, 1)) = 0.35
        _DeepMul("Deep Mul (바닥 먹 고임 명도)", Range(0, 1)) = 0.6
        _DeepHeight("Deep Height (고임 높이 비율)", Range(0, 1)) = 0.3
        _RimLift("Rim Lift (프레넬 명도 상승 — 색상 불변)", Range(0, 0.5)) = 0.3
        _RimPower("Rim Power", Float) = 2.5
        _LipErode("Lip Erode (립 갈라짐 세기)", Range(0, 0.25)) = 0.12
        _LipBand("Lip Band (침식 높이 비율 — 위에서부터)", Range(0.02, 0.5)) = 0.15
        _Dissolve("Dissolve (0=온전 → 1=소실 — 코드 세팅)", Range(0, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

        // 기술 상수 — 룩 노브가 아니라 노이즈 도메인 배율(프로퍼티는 Properties 블록이 정본)
        #define TONE_SCALE_MUL      2.3   // 먹 농담 밴드 = 흐름 노이즈보다 조밀
        #define TONE_FLOW_MUL       0.7   // 농담 밴드는 흐름과 반대·느리게
        #define LIP_NOISE_MUL       5.0   // 립 침식 결(posOS 도메인)
        #define LIP_NOISE_SPEED     0.5   // 립 침식 시간 — 느리게(어른거림 방지)
        #define LIP_ERODE_GAIN      4.0   // 립 첨단 임계 = _LipErode × 4 (0.12 → 0.48)
        #define DISSOLVE_NOISE_MUL  3.0
        #define DISSOLVE_NOISE_AMP  0.6   // 갈라짐 폭(±0.3)
        #define DISSOLVE_SWEEP      1.7   // _Dissolve 1에서 hOS 0까지 전부 소실

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS   : NORMAL;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 posWS      : TEXCOORD0;
            float3 nWS        : TEXCOORD1;
            float4 posOSh     : TEXCOORD2; // xyz = posOS(변위 전), w = hOS(바닥 0 → 립 1)
        };

        CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _Color;      // 별칭 — 미독(SRP Batcher 레이아웃용)
            half  _Cull;       // 렌더 스테이트 — 미독
            half  _NoiseScale;
            half  _FlowSpeed;
            half  _WaveAmp;
            half  _WaveFloor;
            half  _HeightOS;
            half  _ToneContrast;
            half  _Shade;
            half  _DeepMul;
            half  _DeepHeight;
            half  _RimLift;
            half  _RimPower;
            half  _LipErode;
            half  _LipBand;
            half  _Dissolve;
        CBUFFER_END

        // 정수 해시 value noise 2옥타브 — InkStroke.shader L101-121 원문(텍스처 없이 결을 만든다)
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

        // 정점: 상단(_WaveFloor 이상)만 법선 방향으로 흐름 노이즈 오프셋 — 지면 접점은 떨리지 않는다.
        // 법선은 비등방 배율(높이·폭 스케일 애니메이션)을 보정해 월드로 옮긴다
        Varyings vert(Attributes v)
        {
            Varyings o;
            float3 posWS = TransformObjectToWorld(v.positionOS.xyz);
            float3 nWS = TransformObjectToWorldNormal(v.normalOS);
            float hOS = v.positionOS.z / max(_HeightOS, 1e-3);
            float hMask = saturate((hOS - _WaveFloor) / max(1.0 - _WaveFloor, 1e-3));
            float n = Fbm(posWS.xz * _NoiseScale + _Time.y * _FlowSpeed) - 0.5;
            posWS += nWS * (n * _WaveAmp * hMask);
            o.positionCS = TransformWorldToHClip(posWS);
            o.posWS = posWS;
            o.nWS = nWS;
            o.posOSh = float4(v.positionOS.xyz, hOS);
            return o;
        }

        // 립 침식·디졸브 — 세 패스가 같은 마스크로 clip 한다(깊이·법선 텍스처와 컬러의 실루엣 일치)
        void ClipWater(float3 posOS, float hOS)
        {
            // 립 갈라짐: 위 _LipBand 구간에서 립으로 갈수록 임계가 오른다(첨단에서 _LipErode×4) — posOS 도메인·느린 시간
            float top = saturate((hOS - (1.0 - _LipBand)) / _LipBand);
            float erode = _LipErode * LIP_ERODE_GAIN * top * top;
            float e = Fbm(posOS.xz * _NoiseScale * LIP_NOISE_MUL + _Time.y * LIP_NOISE_SPEED);
            clip(e - erode);

            // 위에서부터 갈라지며 소실(InkStroke §7 선례). _Dissolve 0에서 최소값 = (1 − hOS) ≥ 0 → 손실 없음,
            // _Dissolve 1에서 최대값 = −hOS ≤ 0 → 전부 소실
            float d = (1.0 - hOS) + (Fbm(posOS.xy * DISSOLVE_NOISE_MUL) - 0.5) * DISSOLVE_NOISE_AMP
                    + DISSOLVE_NOISE_AMP * 0.5 - _Dissolve * DISSOLVE_SWEEP;
            clip(d);
        }
        ENDHLSL

        Pass
        {
            Name "InkWater"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite On
            ZTest LEqual
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            half4 frag(Varyings i) : SV_Target
            {
                float3 posOS = i.posOSh.xyz;
                float  hOS = i.posOSh.w;
                ClipWater(posOS, hOS);

                half3 nWS = normalize(i.nWS);
                half3 rgb = _BaseColor.rgb;

                // 먹 농담 밴드 — 명도만(표면 흐름의 읽기), posWS 도메인
                float tone = Fbm(i.posWS.xz * _NoiseScale * TONE_SCALE_MUL - _Time.y * _FlowSpeed * TONE_FLOW_MUL);
                rgb *= 1.0 - _ToneContrast * (tone - 0.5) * 2.0;

                // 반 램버트 형태 음영 — M_SpellBodyWave 「음영으로 덩어리 교정」 선례의 무PBR 계승(스펙큘러 없음, 광원색 미곱)
                Light mainLight = GetMainLight();
                half hl = dot(nWS, half3(mainLight.direction)) * 0.5 + 0.5;
                rgb *= lerp(1.0, hl, _Shade);

                // 바닥 먹 고임 — _DeepHeight 0 가드(0/0 NaN 방지)
                rgb *= lerp(_DeepMul, 1.0, smoothstep(0.0, max(_DeepHeight, 1e-3), hOS));

                // 명도 프레넬 림 — 담채 물비침. 색상·채도 불변, 상한 +_RimLift(REWORK §8-3)
                half3 V = GetWorldSpaceNormalizeViewDir(i.posWS);
                half fres = pow(1.0 - saturate(dot(nWS, V)), _RimPower);
                rgb *= 1.0 + _RimLift * fres;

                return half4(rgb, 1.0);
            }
            ENDHLSL
        }

        // 깊이 텍스처(소프트 파티클·후처리) — 정점 오프셋·clip 동일
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepth

            half fragDepth(Varyings i) : SV_Target
            {
                ClipWater(i.posOSh.xyz, i.posOSh.w);
                return i.positionCS.z;
            }
            ENDHLSL
        }

        // 법선 텍스처(PC_Renderer SSAO Source=DepthNormals) — URP LitDepthNormalsPass 출력 규약과 동일
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment fragDepthNormals
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 fragDepthNormals(Varyings i) : SV_Target
            {
                ClipWater(i.posOSh.xyz, i.posOSh.w);
                float3 normalWS = normalize(i.nWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                    return half4(packedNormalWS, 0.0);
                #else
                    return half4(NormalizeNormalPerPixel(normalWS), 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
