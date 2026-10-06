// 마석 자동차 먹 소환·회수 껍질 — SPEC-VEHICLE-UX-308 §5 · D308-8 [TEST].
// VehicleInkPresentation308이 차 메시(LOD0)를 이 재질로 Graphics.RenderMesh 한 번 더 그린다(새 오브젝트 없음, MPB로 값 전달).
// 모습: 먹이 아래에서 차 모양으로 차오른다(소환) · 먹이 번지듯 물러나며 차가 드러난다(역번짐) · 먹이 번져 차를 덮고 위에서부터 흩어진다(회수).
//   순서값 m = lerp(노이즈, lerp(높이 h, 노이즈, _NoiseAmount), _HeightOrder)  — 낮은 m이 먼저 선다.
//   덮임 t = _Coverage × (1 + _EdgeWidth); m < t인 곳만 그린다. 앞가장자리(t − m < _EdgeWidth)는 더 짙은 먹, 알파는 얇아진다(번짐).
// 발광 상한(ART-INK, D308-8): 먹·한지 색만 받는다. 최종 최대 채널 ≤ .85(LDR), HDR 가산·Emission 없음, 블룸 문턱 아래.
// 투명 큐(InkWash297 뒤), UniversalForwardOnly 하나 — ShadowCaster·DepthOnly·DepthNormals 패스가 없어 깊이·법선 먹선과 그림자에 끼지 않는다.
// 키워드 변형 없음 · 에디터 동기 컴파일(#pragma editor_sync_compilation — 첫 사용 프레임의 하늘색 대체 셰이더 방지). 노이즈는 텍스처 없이 월드 값 노이즈(차와 함께 움직이지 않아도 1초 연출이라 무방). 인식 불가침: 표현 전용.
Shader "Oheangbu/VehicleInkShell308"
{
    Properties
    {
        _InkColor("Ink", Color) = (0.095, 0.088, 0.080, 1)
        _EdgeColor("Wet Edge Ink", Color) = (0.035, 0.032, 0.030, 1)
        _Coverage("Coverage (0 none - 1 full)", Range(0, 1)) = 0
        _HeightOrder("Height Order (1 bottom-up, 0 noise only)", Range(0, 1)) = 1
        _Ground("Ground Y (world m)", Float) = 0
        _Height("Height (m)", Float) = 2.5
        _NoiseScale("Noise Scale (1/m)", Float) = 3.5
        _NoiseAmount("Noise Share in Height Order", Range(0, 1)) = 0.35
        _EdgeWidth("Edge Width", Range(0.005, 0.3)) = 0.08
        _Inflate("Inflate (m along normal)", Range(0, 0.1)) = 0.02
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
            Name "VehicleInkShell"
            Tags { "LightMode" = "UniversalForwardOnly" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite On
            ZTest LEqual
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            // 에디터 동기 컴파일: 이 셰이더는 1초 연출의 첫 프레임에 처음 쓰인다. 비동기 컴파일이면 에디터가 그동안
            // 하늘색 대체 셰이더(clip 없음 → 차 메시 전체가 반투명 하늘색 다각형)로 그린다 — 먹(ART-INK)이 아닌 색이 보인다.
            // 빌드에는 영향 없음(빌드 셰이더는 미리 컴파일된다). #308 Vehicle308Checks summon_gather 캡처에서 확인.
            #pragma editor_sync_compilation
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            CBUFFER_START(UnityPerMaterial)
                half4 _InkColor;
                half4 _EdgeColor;
                float _Coverage;
                float _HeightOrder;
                float _Ground;
                float _Height;
                float _NoiseScale;
                float _NoiseAmount;
                float _EdgeWidth;
                float _Inflate;
                float _Seed;
            CBUFFER_END

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

            Varyings vert(Attributes v)
            {
                Varyings o;
                float3 n = TransformObjectToWorldNormal(v.normalOS);
                float3 p = TransformObjectToWorld(v.positionOS.xyz) + n * _Inflate;
                o.positionWS = p;
                o.normalWS = n;
                o.positionCS = TransformWorldToHClip(p);
                return o;
            }

            half4 frag(Varyings i) : SV_Target
            {
                // keep the grain small next to kilometre-scale world coordinates
                float3 q = frac(i.positionWS * (1.0 / 512.0)) * 512.0 * max(_NoiseScale, 0.01) + _Seed;
                float n = Noise3(q) * 0.65 + Noise3(q * 2.7 + 11.0) * 0.35;
                float h = saturate((i.positionWS.y - _Ground) / max(_Height, 0.1));
                float m = lerp(n, lerp(h, n, saturate(_NoiseAmount)), saturate(_HeightOrder));
                float edgeWidth = max(_EdgeWidth, 0.005);
                float t = saturate(_Coverage) * (1.0 + edgeWidth);
                float inside = t - m;
                clip(inside);
                float edge = 1.0 - saturate(inside / edgeWidth);
                float3 nrm = normalize(i.normalWS);
                float shade = 0.80 + 0.20 * saturate(dot(nrm, normalize(float3(-0.4, 0.8, -0.3))));
                half3 rgb = lerp(saturate(_InkColor.rgb) * shade, saturate(_EdgeColor.rgb), edge * 0.8);
                half alpha = saturate(_InkColor.a) * lerp(1.0, 0.55, edge);
                return half4(min(rgb, 0.85), alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
